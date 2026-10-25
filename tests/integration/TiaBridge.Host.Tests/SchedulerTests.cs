using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using TiaBridge.Core.Identity;
using TiaBridge.Core.Operations;
using TiaBridge.Core.Transport.V1;
using TiaBridge.Host.Operations;
using TiaBridge.Host.Scheduling;
using TiaBridge.Host.Storage;
using TiaBridge.Host.Supervision;
using TiaBridge.Ipc;
using Xunit;

namespace TiaBridge.Host.Tests;

internal sealed class ManualClock : TimeProvider
{
    private long ticks = DateTimeOffset.UtcNow.UtcTicks;
    public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref ticks), TimeSpan.Zero);
    internal void Advance(TimeSpan duration) => Interlocked.Add(ref ticks, duration.Ticks);
}
internal sealed class FixedJitter(double value = 0) : IJitter { public double NextUnit() => value; }
internal sealed class FakeProfile : IExecutionProfile
{
    internal Dictionary<OperationId, ExecutionSpec> Specs { get; } = new();
    public ExecutionSpec? Resolve(StoredOperation operation) => Specs.GetValueOrDefault(operation.Report.OperationId);
}
internal sealed class CapturingSupervisor(IWorkerSupervisor inner) : IWorkerSupervisor
{
    internal CapturingConnection? Connection { get; private set; }
    public async Task<IWorkerConnection> StartAsync(CancellationToken ct) => Connection = new(await inner.StartAsync(ct));
}
internal sealed class CapturingConnection(IWorkerConnection inner) : IWorkerConnection
{
    private (Guid? Id, long Fence, JsonElement Body)? dispatch;
    private readonly Queue<WireMessage> held = new();
    public Guid Epoch => inner.Epoch;
    public int Pid => inner.Pid;
    public long StartTicks => inner.StartTicks;
    public string WorkerId => inner.WorkerId;
    public bool HasExited => inner.HasExited;
    public bool Connected => inner.Connected;
    public DateTimeOffset LastHeartbeat => inner.LastHeartbeat;
    public DateTimeOffset? LastProgress => inner.LastProgress;
    public bool TryRead(out WireMessage message) => held.TryDequeue(out message!) || inner.TryRead(out message);
    public Task SendAsync(MessageKind kind, Guid? operationId, long fence, JsonElement body, CancellationToken ct)
    {
        if (kind == MessageKind.Dispatch) dispatch = (operationId, fence, body.Clone());
        return inner.SendAsync(kind, operationId, fence, body, ct);
    }
    internal Task ResendIdenticalDispatch()
    { var sent = dispatch!.Value; return inner.SendAsync(MessageKind.Dispatch, sent.Id, sent.Fence, sent.Body, CancellationToken.None); }
    internal async Task HoldUntilReceipt()
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromSeconds(10))
        {
            if (inner.TryRead(out var message)) { held.Enqueue(message); if (message.Kind == MessageKind.Receipt) return; }
            await Task.Delay(10);
        }
        throw new TimeoutException("Controlled late receipt did not arrive.");
    }
    public ValueTask DisposeAsync() => inner.DisposeAsync();
}
internal sealed class SchedulerFixture : IAsyncDisposable
{
    internal StoreFixture Files { get; } = new();
    internal ManualClock Clock { get; } = new();
    internal FakeProfile Profile { get; } = new();
    internal SqliteOperationStore Store { get; }
    internal OperationScheduler Scheduler { get; }
    internal CapturingSupervisor Supervisor { get; }
    internal static string WorkerPath(StoreFixture f) => Path.Combine(f.Repository, "src", "TiaBridge.FakeWorker", "bin", new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name, "net10.0", "TiaBridge.FakeWorker.dll");
    internal SchedulerFixture()
    {
        Store = new(Files.Location, clock: Clock);
        Supervisor = new(new FakeWorkerSupervisor(WorkerPath(Files), Files.Root, Clock));
        Scheduler = new(Store, Supervisor, Profile, Clock, new FixedJitter());
    }
    internal OperationId Admit(FakeScenario scenario = FakeScenario.Success, bool mutation = false, int timeout = 30, int delay = 0, bool qualified = true, ErrorCode failure = ErrorCode.TIA_UNAVAILABLE, bool differentProject = false)
    {
        string method = mutation ? "sync.apply" : "project.inspect";
        var node = JsonNode.Parse(StoreFixture.Wire(method))!; node["idempotency_key"] = Guid.NewGuid().ToString(); node["params"]!["timeout_s"] = timeout;
        if (differentProject) node["project_key"] = Guid.NewGuid().ToString();
        byte[] wire = JsonSerializer.SerializeToUtf8Bytes(node); Store.RegisterProject(ContractJson.ReadRequest(wire).Identity.Project!);
        var admitted = Store.Accept("synthetic-principal", wire); var id = admitted.Operation.Report.OperationId;
        byte[] report = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "response." + method + ".json"));
        var asset = Store.Artifacts.Put(id, new MemoryStream(report));
        Profile.Specs[id] = new("synthetic-portal", "fake.1." + method, qualified, asset, scenario, delay, failure);
        Clock.Advance(TimeSpan.FromMilliseconds(1));
        return id;
    }
    internal async Task Until(Func<bool> predicate, int milliseconds = 10000)
    {
        var deadline = Stopwatch.StartNew();
        while (!predicate())
        {
            if (deadline.ElapsedMilliseconds > milliseconds) throw new TimeoutException("Synthetic scheduler predicate not reached: " + Scheduler.Observation.State);
            await Scheduler.PumpOnceAsync(); await Task.Delay(10);
        }
    }
    internal async Task Running(OperationId id) => await Until(() => Store.Read(id).Report.Phase == "EXECUTING");
    internal async Task Advance(int seconds)
    { for (int i = 0; i < seconds; i++) { Clock.Advance(TimeSpan.FromSeconds(1)); await Scheduler.PumpOnceAsync(); } }
    public async ValueTask DisposeAsync() { await Scheduler.DisposeAsync(); Files.Dispose(); }
}

public class SchedulerTests
{
    [Theory]
    [InlineData(FakeScenario.Success)]
    [InlineData(FakeScenario.DelayedSuccess)]
    [InlineData(FakeScenario.DuplicateDispatch)]
    public async Task Real_worker_completes_a_durable_operation_once(FakeScenario scenario)
    {
        await using var f = new SchedulerFixture(); var id = f.Admit(scenario, delay: 40);
        await f.Until(() => f.Store.ReadStatus(id).State == OperationStatusResult_State.SUCCEEDED);
        Assert.Equal(1, f.Store.AttemptCount(id)); Assert.NotNull(f.Files.Open().ReadResult(id));
        Assert.Equal(OperationState.SUCCEEDED, f.Store.Events(id).Last().Report.State);
        Assert.True(f.Scheduler.Observation.Pid > 0); Assert.Null(f.Scheduler.Observation.TiaProbeAt);
        using var c = f.Files.Connection(); using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT message_json FROM worker_receipts ORDER BY receipt_id LIMIT 1";
        var receipt = JsonSerializer.Deserialize<WireMessage>((byte[])cmd.ExecuteScalar()!, Protocol.Json)!;
        Assert.Equal(1, Protocol.Read<WorkerReply>(receipt.Body).Receipt.EffectCount);
    }

    [Fact]
    public async Task Queued_cancel_is_atomic_and_never_dispatches()
    {
        await using var f = new SchedulerFixture(); var id = f.Admit(); var operation = f.Store.Read(id);
        f.Store.Record(id, operation.Revision, operation.Report with { State = OperationState.QUEUED, Phase = "QUEUED" });
        Assert.Equal(OperationState.CANCELLED, f.Store.Cancel(id).Report.State);
        await f.Scheduler.PumpOnceAsync(); Assert.Equal(0, f.Store.AttemptCount(id)); Assert.Null(f.Scheduler.Observation.Pid);
    }

    [Fact]
    public async Task Running_cancel_requires_a_worker_safe_boundary()
    {
        await using var f = new SchedulerFixture(); var id = f.Admit(FakeScenario.Cancellable, mutation: true);
        await f.Running(id); f.Store.Cancel(id);
        Assert.Equal(OperationStatusResult_State.RUNNING, f.Store.ReadStatus(id).State);
        await f.Until(() => f.Store.ReadStatus(id).State == OperationStatusResult_State.CANCELLED);
        Assert.False(f.Store.OwnerBlocked); Assert.True(f.Store.Read(id).Report.CancelRequested);
    }

    [Theory]
    [InlineData(FakeScenario.Hang)]
    [InlineData(FakeScenario.NoHeartbeat)]
    [InlineData(FakeScenario.HeartbeatWithoutProgress)]
    [InlineData(FakeScenario.Timeout)]
    [InlineData(FakeScenario.UnknownMutation)]
    public async Task Hung_mutation_times_out_to_durable_quarantine_without_retry(FakeScenario scenario)
    {
        await using var f = new SchedulerFixture(); var id = f.Admit(scenario, mutation: true, timeout: 4);
        var second = f.Admit(mutation: true); await f.Running(id); await f.Advance(5);
        Assert.Equal(OperationState.RECONCILIATION_REQUIRED, f.Store.ReadResult(id)!.State); Assert.True(f.Store.OwnerBlocked);
        Assert.Equal(1, f.Store.AttemptCount(id)); Assert.Equal(0, f.Store.AttemptCount(second)); Assert.Null(f.Store.TryClaim(second));
        for (int i = 0; i < 20; i++) await f.Advance(1);
        Assert.True(f.Files.Open().OwnerBlocked); Assert.Equal(0, f.Store.AttemptCount(second));
    }

    [Fact]
    public async Task Heartbeat_without_progress_does_not_complete_or_extend_deadline()
    {
        await using var f = new SchedulerFixture(); var id = f.Admit(FakeScenario.HeartbeatWithoutProgress, timeout: 10);
        await f.Running(id); var progress = f.Scheduler.Observation.LastProgressAt;
        await f.Advance(3); await Task.Delay(2200); await f.Scheduler.PumpOnceAsync();
        Assert.Equal(f.Clock.GetUtcNow(), f.Scheduler.Observation.WorkerHeartbeat); Assert.Equal(progress, f.Scheduler.Observation.LastProgressAt);
        Assert.True(f.Scheduler.Observation.ProgressAge(f.Clock.GetUtcNow()) >= TimeSpan.FromSeconds(3));
        Assert.Equal(OperationStatusResult_State.RUNNING, f.Store.ReadStatus(id).State);
        await f.Advance(8); Assert.Equal(OperationStatusResult_State.FAILED, f.Store.ReadStatus(id).State);
    }

    [Fact]
    public async Task Missing_heartbeat_is_detected_separately_from_the_execution_deadline()
    {
        await using var f = new SchedulerFixture(); var id = f.Admit(FakeScenario.NoHeartbeat, mutation: true, timeout: 30);
        await f.Running(id); await f.Advance(7);
        Assert.Equal(ErrorCode.WORKER_UNAVAILABLE, f.Store.Read(id).Report.Error!.Code);
        Assert.Equal(OperationState.RECONCILIATION_REQUIRED, f.Store.Read(id).Report.State);
    }

    [Theory]
    [InlineData(FakeScenario.Exit, false, OperationState.FAILED)]
    [InlineData(FakeScenario.Exit, true, OperationState.RECONCILIATION_REQUIRED)]
    [InlineData(FakeScenario.Disconnect, false, OperationState.FAILED)]
    [InlineData(FakeScenario.Disconnect, true, OperationState.RECONCILIATION_REQUIRED)]
    [InlineData(FakeScenario.FailAfter, true, OperationState.RECONCILIATION_REQUIRED)]
    public async Task Process_or_ipc_loss_never_loses_the_accepted_operation(FakeScenario scenario, bool mutation, OperationState expected)
    {
        await using var f = new SchedulerFixture(); var id = f.Admit(scenario, mutation);
        await f.Until(() => OperationLifecycle.IsTerminal(f.Store.Read(id).Report.State));
        Assert.Equal(expected, f.Files.Open().Read(id).Report.State); Assert.Equal(1, f.Store.AttemptCount(id));
    }

    [Fact]
    public async Task Timed_out_read_cannot_overlap_the_still_running_call()
    {
        await using var f = new SchedulerFixture(); var first = f.Admit(FakeScenario.Hang, timeout: 2); await f.Running(first);
        var second = f.Admit(); await f.Advance(3);
        Assert.Equal(OperationStatusResult_State.FAILED, f.Store.ReadStatus(first).State);
        for (int i = 0; i < 10; i++) await f.Scheduler.PumpOnceAsync();
        Assert.Equal(0, f.Store.AttemptCount(second)); Assert.Equal(1, f.Store.AttemptCount(first));
    }

    [Fact]
    public async Task Late_success_is_retained_without_changing_unknown_to_success()
    {
        await using var f = new SchedulerFixture(); var id = f.Admit(FakeScenario.LateSuccess, mutation: true, timeout: 2);
        await f.Running(id); f.Clock.Advance(TimeSpan.FromSeconds(3));
        await f.Supervisor.Connection!.ResendIdenticalDispatch(); // releases the already running synthetic call; no new attempt/effect.
        await f.Supervisor.Connection.HoldUntilReceipt();
        await f.Scheduler.PumpOnceAsync(); // receipt is already queued when the deadline check executes.
        await f.Until(() => f.Store.ReceiptCount(id) > 0);
        Assert.Equal(OperationState.RECONCILIATION_REQUIRED, f.Store.Read(id).Report.State);
        Assert.True(f.Store.OwnerBlocked); Assert.Equal(1, f.Store.AttemptCount(id));
    }

    [Fact]
    public async Task Qualified_read_retries_only_after_no_effect_receipt_and_backoff()
    {
        await using var f = new SchedulerFixture(); var id = f.Admit(FakeScenario.SuccessAfterRetry);
        await f.Until(() => f.Store.ReceiptCount(id) == 1); Assert.Equal(1, f.Store.AttemptCount(id));
        await f.Supervisor.Connection!.ResendIdenticalDispatch(); await f.Supervisor.Connection.HoldUntilReceipt();
        await f.Scheduler.PumpOnceAsync(); // a cached no-effect receipt is evidence, not another breaker failure/attempt.
        var breaker = JsonSerializer.Deserialize<CircuitState>(f.Store.ReadBreaker("synthetic-portal", "fake.1.project.inspect")!)!;
        Assert.Equal(1, breaker.Failures);
        await f.Scheduler.PumpOnceAsync(); Assert.Equal(1, f.Store.AttemptCount(id));
        await f.Advance(2); await f.Until(() => f.Store.ReadStatus(id).State == OperationStatusResult_State.SUCCEEDED);
        Assert.Equal(2, f.Store.AttemptCount(id));
    }

    [Theory]
    [InlineData(true, true, ErrorCode.TIA_UNAVAILABLE)]
    [InlineData(false, false, ErrorCode.TIA_UNAVAILABLE)]
    [InlineData(false, true, ErrorCode.IDENTITY_MISMATCH)]
    [InlineData(false, true, ErrorCode.ACCESS_DENIED)]
    [InlineData(false, true, ErrorCode.CONFLICT)]
    public async Task Mutations_unqualified_reads_and_policy_failures_have_no_retry(bool mutation, bool qualified, ErrorCode code)
    {
        await using var f = new SchedulerFixture(); var id = f.Admit(FakeScenario.FailBefore, mutation, qualified: qualified, failure: code);
        await f.Until(() => OperationLifecycle.IsTerminal(f.Store.Read(id).Report.State));
        Assert.Equal(1, f.Store.AttemptCount(id)); Assert.Equal(OperationOutcome.NOT_STARTED, f.Store.Read(id).Report.Outcome);
    }

    [Fact]
    public async Task Three_total_attempts_open_a_persisted_portal_capability_breaker()
    {
        await using var f = new SchedulerFixture(); var id = f.Admit(FakeScenario.FailBefore);
        await f.Until(() => f.Store.ReceiptCount(id) == 1); await f.Advance(2);
        await f.Until(() => f.Store.ReceiptCount(id) == 2); await f.Advance(3);
        await f.Until(() => f.Store.ReadStatus(id).State == OperationStatusResult_State.FAILED);
        Assert.Equal(3, f.Store.AttemptCount(id));
        var second = f.Admit(); await f.Scheduler.PumpOnceAsync();
        Assert.Equal(ErrorCode.CIRCUIT_OPEN, f.Store.Read(second).Report.Error!.Code); Assert.Equal(0, f.Store.AttemptCount(second));
        Assert.NotNull(f.Files.Open().ReadBreaker("synthetic-portal", "fake.1.project.inspect"));
    }

    [Fact]
    public async Task Host_status_remains_responsive_while_a_real_fake_process_is_hung()
    {
        await using var f = new SchedulerFixture(); var id = f.Admit(FakeScenario.Hang, timeout: 30);
        f.Scheduler.Start(); var deadline = Stopwatch.StartNew();
        while (f.Store.ReadStatus(id).Phase != "EXECUTING" && deadline.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(10);
        Assert.Equal("EXECUTING", f.Store.ReadStatus(id).Phase);
        var samples = new List<double>();
        for (int i = 0; i < 1000; i++) { var timer = Stopwatch.StartNew(); _ = f.Store.ReadStatus(id); samples.Add(timer.Elapsed.TotalMilliseconds); }
        samples.Sort(); Assert.True(samples[949] <= 100, "Storage status p95 exceeded 100 ms: " + samples[949]);
        Assert.Null(f.Scheduler.LastError); Assert.Equal(OperationStatusResult_State.RUNNING, f.Store.ReadStatus(id).State);
    }

    [Fact]
    public async Task Singleton_and_stale_fence_prevent_a_second_scheduler_owner()
    {
        await using var f = new SchedulerFixture();
        Assert.Equal(ErrorCode.NOT_OWNER, Assert.Throws<StorageException>(() => new OperationScheduler(f.Store, new FakeWorkerSupervisor(SchedulerFixture.WorkerPath(f.Files), f.Files.Root), f.Profile)).Code);
        var old = f.Scheduler.Owner; var newer = f.Store.BeginOwnership(); Assert.True(newer.Fence > old.Fence);
        Assert.False(f.Store.RenewOwner(old));
        Assert.Throws<StorageException>(() => f.Store.RegisterWorker(old, Guid.NewGuid(), Environment.ProcessId, 1, "synthetic"));
    }

    [Fact]
    public async Task Host_process_restart_quarantines_a_dispatched_mutation_and_preserves_epoch()
    {
        using var f = new StoreFixture(); var crashed = f.Probe("scheduler-crash"); Assert.True(crashed.Code == 76, crashed.Output);
        var store = f.Open(); var id = f.Accept(store).Operation.Report.OperationId;
        Assert.Equal(OperationState.RUNNING, store.Read(id).Report.State);
        await using var scheduler = new OperationScheduler(store, new FakeWorkerSupervisor(SchedulerFixture.WorkerPath(f), f.Root), new FakeProfile());
        Assert.Equal(OperationState.RECONCILIATION_REQUIRED, store.Read(id).Report.State); Assert.True(store.OwnerBlocked);
        using var c = f.Connection(); using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT worker_epoch FROM quarantines";
        Assert.True(Guid.TryParse((string?)cmd.ExecuteScalar(), out _)); Assert.Equal(1, store.AttemptCount(id));
    }
}
