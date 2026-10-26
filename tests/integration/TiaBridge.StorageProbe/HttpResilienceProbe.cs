using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using TiaBridge.Core.Operations;
using TiaBridge.Host.Api;
using TiaBridge.Host.Operations;
using TiaBridge.Host.Scheduling;
using TiaBridge.Host.Storage;
using TiaBridge.Host.Supervision;
using TiaBridge.Ipc;

internal static class HttpResilienceProbe
{
    internal static async Task Run(string root, string mode)
    {
        if (!OperatingSystem.IsWindows()) throw new InvalidOperationException("Windows test lane required.");
        var config = HostConfiguration.Read(Path.Combine(root, "host-config.json"));
        if (Path.GetFullPath(config.StatePath) != Path.GetFullPath(root)) throw new ArgumentException("Probe ownership mismatch.");
        var control = new Control(root, mode);
        await HostApi.RunAsync(config, new HostRuntimeHooks(control,
            Profile: profile => new Profile(profile, control), Supervisor: supervisor => new Supervisor(supervisor, control), Ready: () => control.Armed = true));
    }
    internal static async Task<int> HoldLock(string root, string repository)
    {
        _ = StorageLocation.Validate(root, repository);
        using var file = new FileStream(Path.Combine(root, "resilience-resource.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        using var own = Process.GetCurrentProcess();
        File.WriteAllText(Path.Combine(root, "child.json"), JsonSerializer.Serialize(new { pid = own.Id, start_ticks = own.StartTime.ToUniversalTime().Ticks }));
        var timer = Stopwatch.StartNew();
        while (!File.Exists(Path.Combine(root, "child.release")))
        { if (timer.Elapsed > TimeSpan.FromSeconds(90)) return 88; await Task.Delay(10); }
        return 0;
    }
    private sealed class Control(string root, string mode) : IStorageFaults
    {
        internal string Root => root;
        internal string Mode => mode;
        internal volatile bool Armed, Completion;
        private int fired;
        private readonly ConcurrentDictionary<string, bool> visited = new();
        internal void Hit(string point)
        {
            if (!visited.TryAdd(point, true)) return;
            if (mode == "crash." + point) { Mark(point); Environment.Exit(86); }
            bool admissionBarrier = point == "profile" && mode is "crash.dispatch-intent" or "crash.dispatch-sent" or "crash.receipt-received" or "crash.WorkerReceiptCommitted" or "crash.result-before" or "crash.result-after" or "crash.ArtifactPublished";
            if (mode != "barrier." + point && !(mode == "pipe-loss" && point == "dispatch-sent") && !(mode == "artifact" && point == "receipt-received") && !(mode == "stale" && point == "dispatch-sent") && !admissionBarrier) return;
            Mark(point); var timer = Stopwatch.StartNew();
            while (!File.Exists(Path.Combine(root, point + ".release")))
            { if (timer.Elapsed > TimeSpan.FromSeconds(40)) throw new IOException("Test barrier was not released."); Thread.Sleep(5); }
        }
        private void Mark(string point)
        {
            using var own = Process.GetCurrentProcess();
            File.WriteAllText(Path.Combine(root, point + ".reached"), JsonSerializer.Serialize(new { point, pid = own.Id, start_ticks = own.StartTime.ToUniversalTime().Ticks, at = DateTimeOffset.UtcNow }));
        }
        public void Checkpoint(StorageFaultPoint point)
        {
            if (!Armed) return;
            string? wanted = mode.StartsWith("crash.", StringComparison.Ordinal) || mode.StartsWith("fail.", StringComparison.Ordinal) ? mode[(mode.IndexOf('.') + 1)..] : null;
            if (mode == "crash.result-before" && Completion) wanted = "BeforeCommit";
            if (mode == "crash.result-after" && Completion) wanted = "AfterCommit";
            if (wanted != point.ToString() || Interlocked.Exchange(ref fired, 1) != 0) return;
            Mark(point.ToString());
            if (mode.StartsWith("crash.", StringComparison.Ordinal)) Environment.Exit(86);
            if (mode == "fail.BeforeCommit") throw new Microsoft.Data.Sqlite.SqliteException("Synthetic disk full", 13);
            throw new IOException("Synthetic storage statement failure.");
        }
    }
    private sealed class Profile(IExecutionProfile inner, Control control) : IExecutionProfile
    {
        private Guid? first;
        public ExecutionSpec? Resolve(StoredOperation operation)
        {
            control.Hit("profile"); var spec = inner.Resolve(operation);
            if (control.Mode == "artifact" && spec is not null)
            {
                var store = new SqliteOperationStore(StorageLocation.Open(control.Root, Directory.GetCurrentDirectory()));
                var proof = store.Artifacts.Put(operation.Report.OperationId, new MemoryStream("synthetic proof"u8.ToArray()));
                var bytes = Protocol.ReadArtifact(control.Root, new(spec.SuccessReport!.ArtifactId, spec.SuccessReport.Sha256), Protocol.MaxReceiptBytes);
                var report = System.Text.Json.Nodes.JsonNode.Parse(bytes)!;
                report["artifact_refs"] = JsonSerializer.SerializeToNode(new[] { new { artifact_id = proof.ArtifactId, sha256 = proof.Sha256 } });
                var template = store.Artifacts.Put(operation.Report.OperationId, new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(report)));
                File.WriteAllText(Path.Combine(control.Root, "result-artifact.json"), JsonSerializer.Serialize(proof));
                return spec with { SuccessReport = template };
            }
            if (control.Mode != "stale" || spec is null) return spec;
            first ??= operation.Report.OperationId.Value;
            if (operation.Report.OperationId.Value == first) return spec;
            // Explicit synthetic adapter state. No TIA reader, baseline promotion or planner is claimed.
            using var state = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(control.Root, "synthetic-state.json")));
            string actualP = state.RootElement.GetProperty("p").GetString()!, actualB = state.RootElement.GetProperty("b").GetString()!;
            File.WriteAllText(Path.Combine(control.Root, "synthetic-preconditions.json"), JsonSerializer.Serialize(new
            {
                expected_p = new string('a', 64),
                expected_b = new string('b', 64),
                actual_p = actualP,
                actual_b = actualB,
                first_operation = first,
                second_operation = operation.Report.OperationId.Value,
                reason = "STALE_PLAN"
            }));
            return actualP != new string('a', 64) || actualB != new string('b', 64) ? spec with { Scenario = FakeScenario.FailBefore, FailureCode = ErrorCode.STALE_PLAN } : spec;
        }
    }
    private sealed class Supervisor(IWorkerSupervisor inner, Control control) : IWorkerSupervisor
    {
        public async Task<IWorkerConnection> StartAsync(CancellationToken ct)
        {
            var worker = await inner.StartAsync(ct);
            File.WriteAllText(Path.Combine(control.Root, "worker.json"), JsonSerializer.Serialize(new { pid = worker.Pid, start_ticks = worker.StartTicks, epoch = worker.Epoch }));
            if (control.Mode == "child")
            {
                var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                foreach (string arg in new[] { typeof(HttpResilienceProbe).Assembly.Location, control.Root, Directory.GetCurrentDirectory(), "lock-child", "none" }) start.ArgumentList.Add(arg);
                using var child = Process.Start(start) ?? throw new IOException("Own resource child did not start.");
            }
            control.Hit("before-dispatch"); return new Connection(worker, control);
        }
    }
    private sealed class Connection(IWorkerConnection inner, Control control) : IWorkerConnection
    {
        public Guid Epoch => inner.Epoch;
        public int Pid => inner.Pid;
        public long StartTicks => inner.StartTicks;
        public string WorkerId => inner.WorkerId;
        public bool HasExited => inner.HasExited;
        public bool Connected => inner.Connected;
        public DateTimeOffset LastHeartbeat => inner.LastHeartbeat;
        public DateTimeOffset? LastProgress => inner.LastProgress;
        public bool TryRead(out WireMessage message)
        {
            if (!inner.TryRead(out message!)) return false;
            if (message.Kind == MessageKind.Receipt)
            {
                control.Completion = true; control.Hit("receipt-received");
                if (control.Mode == "stale" && Protocol.Read<WorkerReply>(message.Body).Receipt is { Disposition: "Succeeded", EffectCount: 1 })
                    File.WriteAllText(Path.Combine(control.Root, "synthetic-state.json"), JsonSerializer.Serialize(new { p = new string('c', 64), b = new string('d', 64), revision = 1 }));
                if (control.Mode == "old-epoch") message = message with { WorkerEpoch = Guid.NewGuid() };
            }
            return true;
        }
        public async Task SendAsync(MessageKind kind, Guid? operationId, long fence, JsonElement body, CancellationToken ct)
        {
            if (kind == MessageKind.Dispatch) control.Hit("dispatch-intent");
            await inner.SendAsync(kind, operationId, fence, body, ct);
            if (kind == MessageKind.Dispatch)
            {
                control.Hit("dispatch-sent");
                if (control.Mode == "pipe-loss" && inner is WorkerConnection owned) await owned.DisconnectTransportAsync();
            }
        }
        public async ValueTask DisposeAsync()
        {
            await inner.DisposeAsync();
            if (control.Mode == "child")
            {
                try { using var file = new FileStream(Path.Combine(control.Root, "resilience-resource.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None); }
                catch (IOException) { File.WriteAllText(Path.Combine(control.Root, "cleanup-anomaly.json"), "{\"owned_resource_still_locked\":true}"); throw; }
            }
        }
    }
}
