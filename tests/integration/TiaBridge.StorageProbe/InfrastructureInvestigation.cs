using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Channels;
using TiaBridge.Client;
using TiaBridge.Diagnostics;
using TiaBridge.Host.Api;
using TiaBridge.Host.Storage;
using TiaBridge.Ipc;

/// <summary>Offline process experiment, not a scheduler, grant or vendor executor.</summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal static class InfrastructureInvestigation
{
    internal static readonly string[] Scenarios = ["A", "B-trace", "B-no-trace", "C", "D-on", "D-off", "E-sampler", "E-audit", "F-poll-only", "F-send-only", "F-timer-only", "GC-worker", "F-no-flush"];
    internal static async Task<int> Run(string root, string repo, string specification)
    {
        var spec = JsonSerializer.Deserialize<Specification>(File.ReadAllBytes(specification), Protocol.Json)!;
        if (!Scenarios.Contains(spec.Scenario) || spec.InitialIdleSeconds is < 1 or > 120 || spec.PostIdleSeconds is < 1 or > 240 || spec.Operations is < 1 or > 40) return 2;
        if (spec.ControlledGc != (spec.Scenario == "GC-worker")) return 2;
        StorageLocation.Validate(root, repo); Directory.CreateDirectory(root);
        using var external = new StreamWriter(new FileStream(Path.Combine(root, "external.jsonl"), FileMode.CreateNew, FileAccess.Write, FileShare.Read));
        var clock = Stopwatch.StartNew();
        using var own = Process.GetCurrentProcess();
        void Sample(string phase, Process? worker, Process? host = null)
        {
            object Metrics(Process p, string role)
            {
                p.Refresh(); if (p.HasExited) throw new IOException("Owned process lost");
                return new
                {
                    role,
                    pid = p.Id,
                    start_ticks = p.StartTime.ToUniversalTime().Ticks,
                    handles = p.HandleCount,
                    private_bytes = p.PrivateMemorySize64,
                    working_set_bytes = p.WorkingSet64,
                    threads = Threads(p)
                };
            }
            external.WriteLine(JsonSerializer.Serialize(new
            {
                phase,
                elapsed_ticks = clock.ElapsedTicks,
                frequency = Stopwatch.Frequency,
                utc = DateTimeOffset.UtcNow,
                observer = Metrics(own, "observer"),
                worker = worker is null ? null : Metrics(worker, "worker"),
                host = host is null ? null : Metrics(host, "host")
            }, Protocol.Json)); external.Flush();
        }
        async Task Window(string phase, int seconds, Process worker, Process? host = null)
        {
            File.WriteAllText(Path.Combine(root, "phase-" + phase), phase);
            var timer = Stopwatch.StartNew();
            while (timer.Elapsed < TimeSpan.FromSeconds(seconds)) { Sample(phase, worker, host); await Task.Delay(1000); }
            Sample(phase + ".returned", worker, host);
        }
        if (spec.Scenario == "C") return await Fake();
        string mode = spec.Scenario switch
        {
            "A" => "baseline",
            "B-trace" or "B-no-trace" or "GC-worker" => "worker",
            "D-on" => "heartbeat",
            "D-off" => "no-heartbeat",
            "E-sampler" => "sampler",
            "E-audit" => "audit",
            "F-poll-only" => "poll-only",
            "F-send-only" => "send-only",
            "F-no-flush" => "send-only-no-flush",
            _ => "timer-only"
        };
        await using var session = await ProbeSession.Start(root, repo, mode, spec.Scenario == "B-trace");
        var child = session.Child;
        await Window("initial-idle", spec.InitialIdleSeconds, child);
        File.WriteAllText(Path.Combine(root, "phase-activity"), "activity");
        var operations = new List<object>();
        for (int n = 0; n < spec.Operations; n++)
        {
            if (session.Transport is not null)
            {
                Guid id = Guid.NewGuid(); long fence = n + 1;
                var reply = await session.Dispatch(id, fence);
                operations.Add(new { operation_id = id, fence, reply });
            }
            Sample("activity", child); await Task.Delay(1000);
        }
        WriteNew(Path.Combine(root, "operations.json"), operations);
        await Window("post-idle", spec.PostIdleSeconds, child);
        if (spec.ControlledGc)
        {
            File.WriteAllText(Path.Combine(root, "collect-gc"), "Explicit isolated GC control, after standard observation");
            await Until(() => File.Exists(Path.Combine(root, "controlled-gc.json")), TimeSpan.FromSeconds(30));
            await Window("post-gc", 30, child);
        }
        File.WriteAllText(Path.Combine(root, "phase-shutdown"), "shutdown");
        await session.Stop();
        WriteNew(Path.Combine(root, "result.json"), new
        {
            status = "PASS",
            scenario = spec.Scenario,
            offline = true,
            vendor_loaded = false,
            child_pid = child.Id,
            exited = child.HasExited,
            exit_code = child.ExitCode,
            heartbeats = session.Heartbeats,
            operations = operations.Count,
            protocol = session.Transport is null ? "NONE" : "1.1",
            epoch = session.Epoch,
            build = Hash(child.StartInfo.FileName),
            duration_s = clock.Elapsed.TotalSeconds
        });
        return 0;

        async Task<int> Fake()
        {
            string state = Path.Combine(root, "state");
            var success = JsonSerializer.Deserialize<JsonElement>(File.ReadAllBytes(Path.Combine(repo, "examples/contracts/v1/response.project.inspect.json")));
            var config = new HostConfiguration("1.0", state, repo, ["11111111-1111-4111-8111-111111111111"],
                Path.Combine(repo, "src/TiaBridge.FakeWorker/bin/Release/net10.0/TiaBridge.FakeWorker.dll"), [new("project.inspect", FakeScenario.Success, 10, true, success)], MaxInFlight: 1);
            string path = Path.Combine(root, "config.json"); WriteNew(path, config, HostConfiguration.Json);
            var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string arg in new[] { Path.Combine(repo, "src/TiaBridge.Host/bin/Release/net10.0/TiaBridge.Host.dll"), "--config", path, "--stdio" }) start.ArgumentList.Add(arg);
            using var host = Process.Start(start)!;
            WriteNew(Path.Combine(root, "host-process.json"), new { pid = host.Id, start_ticks = host.StartTime.ToUniversalTime().Ticks, config = path });
            Task<string> stdout = host.StandardOutput.ReadToEndAsync(), stderr = host.StandardError.ReadToEndAsync();
            Process? worker = null;
            try
            {
                string discovery = Path.Combine(state, "api/host.json");
                await Until(() => File.Exists(discovery), TimeSpan.FromSeconds(15));
                using var client = new BridgeClient(DiscoveryFile.Read(discovery));
                // Cold admission creates the lazy Worker; preserve seed separately from the 20 activity operations.
                var ids = new List<string>();
                async Task Submit(string suffix)
                {
                    byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new
                    {
                        schema_version = "1.0",
                        method = "project.inspect",
                        project_key = config.RegisteredProjects[0],
                        idempotency_key = "offline-" + suffix,
                        @params = new { inventory_scope = "all_supported", fresh = true, timeout_s = 120, queue_timeout_s = 300 }
                    });
                    var acceptance = await client.SendAsync(HttpMethod.Post, "/v1/operations", bytes);
                    if (acceptance.StatusCode != 202) throw new IOException("Synthetic admission failed");
                    using var a = JsonDocument.Parse(acceptance.Json); string id = a.RootElement.GetProperty("operation_id").GetString()!; ids.Add(id);
                    var timer = Stopwatch.StartNew();
                    while (true)
                    {
                        var response = await client.SendAsync(HttpMethod.Get, "/v1/operations/" + id + "/result");
                        using var doc = JsonDocument.Parse(response.Json);
                        if (doc.RootElement.TryGetProperty("state", out var s) && s.GetString() == "SUCCEEDED") break;
                        if (timer.Elapsed > TimeSpan.FromSeconds(15)) throw new IOException("Synthetic result timeout");
                        await Task.Delay(100);
                    }
                }
                Sample("cold-host-empty", null, host); await Task.Delay(1000); await Submit("seed");
                using (var db = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = Path.Combine(state, "operations.sqlite3"), Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
                {
                    db.Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT pid FROM worker_processes ORDER BY recorded_at DESC LIMIT 1";
                    worker = Process.GetProcessById(Convert.ToInt32(cmd.ExecuteScalar()));
                }
                WriteNew(Path.Combine(root, "worker-process.json"), new { pid = worker.Id, start_ticks = worker.StartTime.ToUniversalTime().Ticks, kind = "FakeWorker" });
                await Window("initial-idle", spec.InitialIdleSeconds, worker, host);
                WriteNew(Path.Combine(root, "types-initial.json"), new { status = "NOT_AVAILABLE", reason = "Remote PSS disabled after preserved smoke native fault; no retry or workaround" });
                File.WriteAllText(Path.Combine(root, "phase-activity"), "activity");
                for (int n = 0; n < spec.Operations; n++) { await Submit(n.ToString()); Sample("activity", worker, host); await Task.Delay(1000); }
                WriteNew(Path.Combine(root, "operations.json"), ids);
                await Window("post-idle", spec.PostIdleSeconds, worker, host);
                var health = await client.SendAsync(HttpMethod.Get, "/v1/diagnostics/health"); File.WriteAllBytes(Path.Combine(root, "health-final.json"), health.Json);
                if (health.StatusCode != 200) throw new IOException("Synthetic Host health observation failed");
                await host.StandardInput.WriteLineAsync("stop");
                await host.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                worker.Refresh(); bool exited = worker.HasExited;
                File.WriteAllText(Path.Combine(root, "host.stdout"), await stdout); File.WriteAllText(Path.Combine(root, "host.stderr"), await stderr);
                WriteNew(Path.Combine(root, "result.json"), new
                {
                    status = exited && host.ExitCode == 0 ? "PASS" : "FAIL",
                    scenario = "C",
                    host_exit = host.ExitCode,
                    child_pid = worker.Id,
                    exited,
                    operations = ids,
                    managed_gc = "NOT_AVAILABLE: unmodified FakeWorker",
                    duration_s = clock.Elapsed.TotalSeconds
                });
                return exited && host.ExitCode == 0 ? 0 : 1;
            }
            finally
            {
                if (!host.HasExited) { await host.StandardInput.WriteLineAsync("stop"); await host.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
                worker?.Dispose();
            }
        }
    }
    internal sealed record Specification(string Scenario, int InitialIdleSeconds, int Operations, int PostIdleSeconds, bool ControlledGc = false);
    internal static int Threads(Process process)
    {
        var threads = process.Threads;
        try { return threads.Count; } finally { foreach (ProcessThread thread in threads) thread.Dispose(); }
    }
    internal static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    internal static void WriteNew(string path, object data, JsonSerializerOptions? options = null)
    { using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read); JsonSerializer.Serialize(file, data, options ?? Protocol.Json); file.Flush(true); }
    internal static async Task Until(Func<bool> condition, TimeSpan timeout)
    { var timer = Stopwatch.StartNew(); while (!condition()) { if (timer.Elapsed > timeout) throw new IOException("Probe observation timeout"); await Task.Delay(25); } }

    internal sealed class ProbeSession : IAsyncDisposable
    {
        internal required Process Child;
        internal FramedConnection? Transport;
        internal Guid Epoch;
        internal int Heartbeats;
        private readonly Channel<WireMessage> replies = Channel.CreateUnbounded<WireMessage>();
        private Task reader = Task.CompletedTask;
        private Task<string> stdout = Task.FromResult(""), stderr = Task.FromResult("");
        internal required string root;
        private bool stopped;
        internal static async Task<ProbeSession> Start(string root, string repo, string mode, bool trace)
        {
            string exe = Path.Combine(repo, "tests/integration/TiaBridge.Worker.InfrastructureProbe/bin/Release/net48/TiaBridge.Worker.InfrastructureProbe.exe");
            var start = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add(mode); start.ArgumentList.Add(root);
            start.Environment.Remove("TIA_BRIDGE_V19_RESOURCE_TRACE"); if (trace) start.Environment["TIA_BRIDGE_V19_RESOURCE_TRACE"] = "1";
            bool ipc = mode is "worker" or "heartbeat" or "no-heartbeat" or "poll-only" or "send-only" or "timer-only" or "send-only-no-flush";
            Guid epoch = Guid.NewGuid(), nonce = Guid.NewGuid(); byte[] secret = RandomNumberGenerator.GetBytes(32);
            using var self = Process.GetCurrentProcess();
            string pipeName = "tia-bridge-offline-" + Guid.NewGuid().ToString("N");
            var pipe = ipc ? new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly | PipeOptions.FirstPipeInstance) : null;
            var child = Process.Start(start)!;
            WriteNew(Path.Combine(root, "worker-process.json"), new { pid = child.Id, start_ticks = child.StartTime.ToUniversalTime().Ticks, executable = exe, sha256 = Hash(exe), mode, trace });
            var session = new ProbeSession { Child = child, root = root, Epoch = epoch, stdout = child.StandardOutput.ReadToEndAsync(), stderr = child.StandardError.ReadToEndAsync() };
            try
            {
                if (ipc)
                {
                    var setup = new Bootstrap(pipeName, Convert.ToBase64String(secret), epoch, nonce, self.Id, self.StartTime.ToUniversalTime().Ticks, root);
                    object document = setup;
                    if (mode == "worker")
                    {
                        string projectPath = Path.Combine(root, "synthetic.ap19"); File.WriteAllText(projectPath, "SYNTHETIC NOT A SIEMENS PROJECT");
                        document = new V19Bootstrap(setup, new([new("11111111-1111-4111-8111-111111111111", self.Id, self.StartTime.ToUniversalTime().Ticks, projectPath)], root));
                    }
                    await child.StandardInput.WriteLineAsync(JsonSerializer.Serialize(document, document.GetType(), Protocol.Json)); child.StandardInput.Close();
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    await pipe!.WaitForConnectionAsync(timeout.Token); PipePeer.Verify(pipe, child.Id, true);
                    session.Transport = new FramedConnection(pipe, secret, epoch, "1.1");
                    var hello = Protocol.Read<Hello>((await session.Transport.ReadAsync(timeout.Token)).Body);
                    if (hello.Pid != child.Id || hello.Nonce != nonce || hello.StartTicks != child.StartTime.ToUniversalTime().Ticks
                        || hello.WorkerId != (mode == "worker" ? "tia-bridge.v19.read-only" : "infrastructure-probe")
                        || hello.Build != (mode == "worker" ? "1.0+" + Hash(exe) : "1.0")) throw new ProtocolException();
                    WriteNew(Path.Combine(root, "hello.json"), hello);
                    await session.Transport.SendAsync(MessageKind.Ready, null, 0, Protocol.Body(new ReadyBody(true)), timeout.Token);
                    session.reader = session.Read();
                }
                await Until(() => File.Exists(Path.Combine(root, "ready.json")) || child.HasExited, TimeSpan.FromSeconds(15));
                if (child.HasExited) throw new IOException("Probe startup failed: " + await session.stderr);
                return session;
            }
            catch { pipe?.Dispose(); if (!child.HasExited) { child.Kill(); await child.WaitForExitAsync(); } child.Dispose(); throw; }
            finally { CryptographicOperations.ZeroMemory(secret); }
        }
        private async Task Read()
        {
            try
            {
                while (true)
                {
                    var message = await Transport!.ReadAsync();
                    if (message.Kind == MessageKind.Heartbeat) { Interlocked.Increment(ref Heartbeats); continue; }
                    await replies.Writer.WriteAsync(message);
                }
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException) { replies.Writer.TryComplete(e); }
        }
        internal async Task<WireMessage> Dispatch(Guid id, long fence)
        {
            byte[] request = JsonSerializer.SerializeToUtf8Bytes(new { schema_version = "1.0", method = "project.inspect", project_key = "11111111-1111-4111-8111-111111111111" });
            string aid = "artifact-" + Guid.NewGuid().ToString("N"), hash = Convert.ToHexStringLower(SHA256.HashData(request));
            Directory.CreateDirectory(Path.Combine(root, "artifacts")); File.WriteAllBytes(Path.Combine(root, "artifacts", aid + ".blob"), request);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await Transport!.SendAsync(MessageKind.Dispatch, id, fence, Protocol.Body(new InspectDispatch(new(aid, hash), 1, hash, null)), timeout.Token);
            while (true)
            {
                var response = await replies.Reader.ReadAsync(timeout.Token);
                if (response.OperationId != id || response.Fence != fence) throw new ProtocolException();
                if (response.Kind == MessageKind.Started) continue;
                if (response.Kind != MessageKind.Receipt || !Protocol.Read<WorkerReply>(response.Body).Receipt.Quiescent) throw new ProtocolException();
                return response;
            }
        }
        internal async Task Stop()
        {
            if (stopped) return; stopped = true;
            if (Transport is null) File.WriteAllText(Path.Combine(root, "stop"), "owned offline process");
            else
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await Transport.SendAsync(MessageKind.Stop, null, 0, Protocol.Body(new StopBody(false)), timeout.Token);
            }
            await Child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            if (Transport is not null) { await Transport.DisposeAsync(); await reader; }
            File.WriteAllText(Path.Combine(root, "child.stdout"), await stdout); File.WriteAllText(Path.Combine(root, "child.stderr"), await stderr);
            if (Child.ExitCode != 0) throw new IOException("Offline child exit " + Child.ExitCode);
        }
        public async ValueTask DisposeAsync() { try { await Stop(); } finally { Child.Dispose(); } }
    }
}
