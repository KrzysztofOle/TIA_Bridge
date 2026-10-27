using System.Text.Json;
using TiaBridge.Core.Operations;
using TiaBridge.Core.Transport.V1;
using TiaBridge.Host.Operations;
using TiaBridge.Host.Scheduling;
using TiaBridge.Host.Storage;
using TiaBridge.Host.Supervision;
using TiaBridge.Ipc;
using Xunit;

namespace TiaBridge.Host.Tests;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
[Collection("HTTP API process")]
public class LocalTrustedDispatchTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Revocation_after_durable_admission_or_at_either_dispatch_check_sends_no_worker_call(int revokeAtResolve)
    {
        using var f = new LocalTrustedFixture(); f.Enroll();
        var store = f.Files.Open(); var operation = f.Files.Accept(store, "project.inspect").Operation;
        var native = f.Profile(); var admitted = native.Resolve(operation); Assert.NotNull(admitted);
        var profile = new RevokingProfile(native, () => f.Tool.Revoke(f.Evidence.EvidenceId, 1, "synthetic controlled boundary"), revokeAtResolve);
        var supervisor = new CountingSupervisor(new FakeWorkerSupervisor(SchedulerFixture.WorkerPath(f.Files), f.Files.Root));
        await using (var scheduler = new OperationScheduler(store, supervisor, profile))
        {
            await scheduler.PumpOnceAsync();
            Assert.Equal(OperationState.FAILED, store.ReadResult(operation.Report.OperationId)!.State);
            Assert.Equal(0, supervisor.Dispatches);
            Assert.Equal(revokeAtResolve == 3 ? 1 : 0, store.AttemptCount(operation.Report.OperationId));
            Assert.False(native.Revalidate(operation, admitted));
            Assert.False(f.Assess().Available);
        }
        Assert.Equal(new[] { "ENROLLED", "REVOKED" }, f.Tool.Store.Events().Select(e => e.Action));
    }
    private sealed class RevokingProfile(IExecutionProfile inner, Action revoke, int at) : IExecutionProfile
    {
        private int resolves;
        public ExecutionSpec? Resolve(StoredOperation operation)
        {
            if (++resolves == at) revoke();
            return inner.Resolve(operation);
        }
    }
    private sealed class CountingSupervisor(IWorkerSupervisor inner) : IWorkerSupervisor
    {
        internal int Dispatches;
        public async Task<IWorkerConnection> StartAsync(CancellationToken ct) => new Connection(await inner.StartAsync(ct), this);
        private sealed class Connection(IWorkerConnection worker, CountingSupervisor owner) : IWorkerConnection
        {
            public Guid Epoch => worker.Epoch;
            public int Pid => worker.Pid;
            public long StartTicks => worker.StartTicks;
            public string WorkerId => worker.WorkerId;
            public bool HasExited => worker.HasExited;
            public bool Connected => worker.Connected;
            public DateTimeOffset LastHeartbeat => worker.LastHeartbeat;
            public DateTimeOffset? LastProgress => worker.LastProgress;
            public bool TryRead(out WireMessage message) => worker.TryRead(out message);
            public Task SendAsync(MessageKind kind, Guid? operationId, long fence, JsonElement body, CancellationToken ct)
            {
                if (kind == MessageKind.Dispatch) owner.Dispatches++;
                return worker.SendAsync(kind, operationId, fence, body, ct);
            }
            public ValueTask DisposeAsync() => worker.DisposeAsync();
        }
    }
}
