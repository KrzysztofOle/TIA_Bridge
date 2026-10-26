using System.Diagnostics;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TiaBridge.Client;
using TiaBridge.Core.Operations;
using TiaBridge.Core.Transport.V1;
using TiaBridge.Host.Api;
using TiaBridge.Host.Storage;

namespace TiaBridge.V19.Qualification;

internal static partial class Program
{
    private static async Task FaultMatrix(HostConfiguration original, string projects)
    {
        string parent = root;
        foreach (string scenario in new[] { "queued-cancel", "running-cancel", "late-result", "old-epoch", "pipe-loss", "parent-loss", "worker-exit" })
        {
            root = Path.Combine(StorageLocation.ApplicationRoot, "dev", "x01", Guid.NewGuid().ToString("N"));
            _ = StorageLocation.Open(root, repository);
            var config = original with { StatePath = root };
            Write("host-config.json", config);
            string mode = scenario switch
            {
                "queued-cancel" or "worker-exit" => "barrier.before-dispatch",
                "running-cancel" or "late-result" => "barrier.receipt-received",
                "parent-loss" => "barrier.dispatch-sent",
                _ => scenario
            };
            var watch = Stopwatch.StartNew();
            try
            {
                await StartHost(Path.Combine(root, "host-config.json"), mode);
                using var client = new BridgeClient(Path.Combine(root, "api", "host.json"));
                byte[] request = Request(config.V19!.Targets[0].ProjectKey, scenario == "late-result" || scenario == "old-epoch" ? 10 : 60);
                string id = await Submit(client, request);
                string barrier = scenario switch
                {
                    "queued-cancel" or "worker-exit" => "before-dispatch",
                    "running-cancel" or "late-result" => "receipt-received",
                    _ => "dispatch-sent"
                };
                if (scenario != "old-epoch") await WaitFor(() => File.Exists(Path.Combine(root, barrier + ".reached")), 30);
                await WaitFor(() => File.Exists(Path.Combine(root, "worker.json")), 15);
                using var workerDoc = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "worker.json")));
                int pid = workerDoc.RootElement.GetProperty("pid").GetInt32();
                long ticks = workerDoc.RootElement.GetProperty("start_ticks").GetInt64();
                using var ownWorker = Process.GetProcessById(pid);
                if (ownWorker.StartTime.ToUniversalTime().Ticks != ticks || !string.Equals(ownWorker.MainModule!.FileName, worker, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Owned worker identity mismatch.");
                object workerResources = new { pid, start_ticks = ticks, epoch = workerDoc.RootElement.GetProperty("epoch").GetGuid(), ram_bytes = ownWorker.WorkingSet64, handles = ownWorker.HandleCount, cpu_ms = ownWorker.TotalProcessorTime.TotalMilliseconds };
                string? queued = null;
                if (scenario == "queued-cancel")
                {
                    queued = await Submit(client, Request(config.V19.Targets[1].ProjectKey, 60));
                    await client.SendAsync(HttpMethod.Post, "/v1/operations/" + queued + "/cancel");
                    Require((await Status(client, queued)).State == OperationState.CANCELLED, "Queued cancel must persist before dispatch.");
                }
                if (scenario == "running-cancel")
                {
                    await client.SendAsync(HttpMethod.Post, "/v1/operations/" + id + "/cancel");
                    Require((await Status(client, id)).State == OperationState.RUNNING, "Cancel intent must not report a false stop.");
                }
                if (scenario == "parent-loss" || scenario == "pipe-loss")
                {
                    // Observe a real vendor entry, not an arbitrary sleep. The read may finish before transport loss.
                    await WaitFor(() => AuditContains("TiaPortalProcess.Attach"), 15);
                }
                if (scenario == "late-result")
                {
                    // The authenticated real receipt is held after Dispose, until its explicit deadline has elapsed.
                    var held = Stopwatch.StartNew();
                    while (held.Elapsed < TimeSpan.FromSeconds(11))
                    {
                        var probes = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ => { var timer = Stopwatch.StartNew(); await Status(client, id); return timer.Elapsed.TotalMilliseconds; }));
                        statusTimes.AddRange(probes);
                        await client.SendAsync(HttpMethod.Get, "/v1/diagnostics/health");
                        await client.SendAsync(HttpMethod.Get, "/v1/operations/" + id + "/result");
                        await client.SendAsync(HttpMethod.Get, "/v1/operations/" + id + "/events");
                        File.WriteAllText(Path.Combine(root, "independent-workspace.txt"), "synthetic edit while receipt held");
                        await Task.Delay(100);
                    }
                    Require((await Status(client, id)).State == OperationState.RUNNING, "Receipt barrier owns only the scheduler, not status HTTP.");
                }
                if (scenario == "worker-exit")
                {
                    // Before dispatch: this exact owned child has never entered a vendor call.
                    ownWorker.Kill(); await ownWorker.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                }
                if (scenario == "parent-loss")
                {
                    host!.Kill(); await host.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); host.Dispose(); host = null;
                    await ownWorker.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
                    Require(AuditContains("TiaPortal.DisposeAttachedSession.Returned"), "Parent loss requires genuine Dispose evidence before recovery.");
                    await StartHost(Path.Combine(root, "host-config.json"));
                }
                else if (scenario != "old-epoch") File.WriteAllText(Path.Combine(root, barrier + ".release"), "owned test release");
                using var recovered = new BridgeClient(Path.Combine(root, "api", "host.json"));
                OperationReport final = await Terminal(recovered, id, 35);
                Require(final.State == (scenario is "queued-cancel" or "running-cancel" ? OperationState.SUCCEEDED : OperationState.FAILED), "Unexpected fault classification: " + final.State);
                if (scenario == "late-result") Require(final.Error?.Code.ToString() == "TIMEOUT", "Late read must retain timeout.");
                await StopHost();
                await ownWorker.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
                foreach (var target in config.V19.Targets) AssertPortal(target);
                using (var sql = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(root, "operations.sqlite3"), Mode = SqliteOpenMode.ReadOnly }.ToString()))
                {
                    sql.Open(); using var command = sql.CreateCommand();
                    command.CommandText = "SELECT COUNT(*) FROM dispatch_attempts WHERE operation_id=$id"; command.Parameters.AddWithValue("$id", id);
                    Require(Convert.ToInt32(command.ExecuteScalar()) <= 1, "No automatic retry of native call.");
                    if (queued is not null) { command.Parameters[0].Value = queued; Require(Convert.ToInt32(command.ExecuteScalar()) == 0, "Cancelled queued read was dispatched."); }
                    if (scenario == "old-epoch") { command.Parameters.Clear(); command.CommandText = "SELECT COUNT(*) FROM worker_receipts WHERE disposition='FENCED_REJECTED'"; Require(Convert.ToInt32(command.ExecuteScalar()) == 1, "Old epoch must be retained and fenced."); }
                }
                // Reopen the same durable operation and repeat its original idempotency key; no new dispatch.
                await StartHost(Path.Combine(root, "host-config.json"));
                using var restarted = new BridgeClient(Path.Combine(root, "api", "host.json"));
                Require(await Submit(restarted, request) == id && (await Status(restarted, id)).State == final.State, "Restart changed accepted operation/history.");
                await StopHost();
                var evidence = new { scenario, status = "PASS", operation_id = id, state = final.State.ToString(), error = final.Error?.Code.ToString(), elapsed_ms = watch.Elapsed.TotalMilliseconds, worker = workerResources, evidence_root = root };
                Write("fault-case.json", evidence); cases.Add(evidence);
                Console.WriteLine(JsonSerializer.Serialize(evidence));
            }
            catch (Exception e) { Write("fault-failure.json", new { scenario, error = e.GetType().FullName, reason = e.Message }); throw; }
            finally
            {
                // Release only this harness's barriers on failure; never leave a blocked pump behind.
                foreach (string point in new[] { "before-dispatch", "dispatch-sent", "receipt-received" })
                    if (File.Exists(Path.Combine(root, point + ".reached"))) File.WriteAllText(Path.Combine(root, point + ".release"), "owned cleanup release");
                try { await StopHost(); } finally { root = parent; Write("run-progress.json", new { cases }); }
            }
        }
    }
    private static byte[] Request(string project, int timeout) => JsonSerializer.SerializeToUtf8Bytes(new { schema_version = "1.0", method = "project.inspect", project_key = project, idempotency_key = Guid.NewGuid().ToString("N"), @params = new { inventory_scope = "all_supported", fresh = true, timeout_s = timeout } });
    private static async Task<string> Submit(BridgeClient client, byte[] request)
    {
        var reply = await client.SendAsync(HttpMethod.Post, "/v1/operations", request);
        Require(reply.StatusCode is 200 or 202, "Durable admission failed: HTTP " + reply.StatusCode);
        return reply.StatusCode == 202 ? ContractJson.ReadAcceptance(reply.Json).OperationId.ToString() : ContractJson.ReadReport(reply.Json).OperationId.ToString();
    }
    private static async Task<OperationReport> Status(BridgeClient client, string id) => ContractJson.ReadReport((await client.SendAsync(HttpMethod.Get, "/v1/operations/" + id)).Json);
    private static async Task<OperationReport> Terminal(BridgeClient client, string id, int seconds)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(seconds))
        {
            var reads = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ => { var timer = Stopwatch.StartNew(); var report = await Status(client, id); return (report, ms: timer.Elapsed.TotalMilliseconds); }));
            statusTimes.AddRange(reads.Select(r => r.ms));
            await client.SendAsync(HttpMethod.Get, "/v1/diagnostics/health");
            await client.SendAsync(HttpMethod.Get, "/v1/operations/" + id + "/result");
            await client.SendAsync(HttpMethod.Get, "/v1/operations/" + id + "/events");
            if (OperationLifecycle.IsTerminal(reads[0].report.State)) return reads[0].report;
            await Task.Delay(50);
        }
        throw new TimeoutException("Bounded fault scenario exceeded deadline.");
    }
    private static async Task WaitFor(Func<bool> condition, int seconds)
    { var timer = Stopwatch.StartNew(); while (!condition()) { if (timer.Elapsed > TimeSpan.FromSeconds(seconds)) throw new TimeoutException("Explicit fault barrier not reached."); await Task.Delay(20); } }
    private static void Require(bool condition, string reason) { if (!condition) throw new InvalidOperationException(reason); }
    private static bool AuditContains(string text) => Directory.GetFiles(root, "v19-audit-*.jsonl").Any(f =>
    {
        using var file = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (file.Length > 1024 * 1024) throw new IOException("Audit exceeds bounded test profile.");
        using var reader = new StreamReader(file); return reader.ReadToEnd().Contains(text, StringComparison.Ordinal);
    });
}
