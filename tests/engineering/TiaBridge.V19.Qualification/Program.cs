using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TiaBridge.Client;
using TiaBridge.Core.Identity;
using TiaBridge.Core.Operations;
using TiaBridge.Core.Transport.V1;
using TiaBridge.Host.Api;
using TiaBridge.Host.Operations;
using TiaBridge.Host.Storage;
using TiaBridge.Ipc;

namespace TiaBridge.V19.Qualification;

[SupportedOSPlatform("windows")]
internal static partial class Program
{
    private static readonly JsonSerializerOptions Json = new(Protocol.Json) { WriteIndented = true };
    private static string repository = "", root = "", worker = "";
    private static Process? host;
    private static readonly List<object> cases = new();
    private static readonly List<double> statusTimes = new(), healthTimes = new(), resultTimes = new(), eventTimes = new();
    private static async Task<int> Main(string[] args)
    {
        if (!OperatingSystem.IsWindows() || args.Length is not (4 or 6 or 8 or 10) || args[0] != "--project-root" || args[2] != "--cycles") return 2;
        string apartment = "STA"; string? existingWorker = null; bool faults = false;
        for (int i = 4; i < args.Length; i += 2)
        { if (args[i] == "--apartment") apartment = args[i + 1]; else if (args[i] == "--worker-exe") existingWorker = Path.GetFullPath(args[i + 1]); else if (args[i] == "--faults" && args[i + 1] == "all") faults = true; else return 2; }
        if (apartment is not ("STA" or "MTA")) return 2;
        string projects = Path.GetFullPath(args[1]); int cycles = int.Parse(args[3]);
        if (cycles is < 1 or > 100) return 2;
        repository = Directory.GetCurrentDirectory();
        root = Path.Combine(StorageLocation.ApplicationRoot, "dev", "x01", Guid.NewGuid().ToString("N"));
        _ = StorageLocation.Open(root, repository);
        Console.WriteLine(JsonSerializer.Serialize(new { evidence_root = root }));
        string configPath = Path.Combine(root, "host-config.json");
        var before = Snapshot(projects); Write("project-files-before.json", before);
        int resultCode = 1;
        try
        {
            string binaries = Path.Combine(repository, "src", "TiaBridge.Worker.V19", "bin", "Release", "net48");
            string deployed = existingWorker is null ? Path.Combine(root, "worker") : Path.GetDirectoryName(existingWorker)!;
            _ = StorageLocation.Validate(deployed, repository);
            DiscoveryFile.CreateProtectedDirectory(deployed);
            foreach (string file in Directory.GetFiles(binaries))
            {
                if (Path.GetFileName(file).StartsWith("Siemens", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Vendor copy forbidden.");
                string target = Path.Combine(deployed, Path.GetFileName(file));
                if (existingWorker is null) File.Copy(file, target);
                else if (!file.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase))
                {
                    using var expected = File.OpenRead(file); using var actual = File.OpenRead(target);
                    if (!SHA256.HashData(expected).SequenceEqual(SHA256.HashData(actual))) throw new InvalidOperationException("Existing worker build mismatch; no overwrite or automatic replacement.");
                }
            }
            worker = Path.Combine(deployed, "TiaBridge.Worker.V19.exe");
            var enumWatch = Stopwatch.StartNew();
            string enumeration = await RunProcess(worker, ["--enumerate"], 20); Write("enumeration.json", JsonDocument.Parse(enumeration).RootElement.Clone());
            double enumerationMs = enumWatch.Elapsed.TotalMilliseconds;
            using var doc = JsonDocument.Parse(enumeration);
            var paths = Directory.GetFiles(projects, "*.ap19", SearchOption.AllDirectories).Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var targets = doc.RootElement.EnumerateArray().Where(p => paths.Contains(p.GetProperty("project_path").GetString()!)).Select(p =>
                new V19Target(Guid.NewGuid().ToString("D"), p.GetProperty("pid").GetInt32(), p.GetProperty("start_ticks").GetInt64(), p.GetProperty("project_path").GetString()!)).OrderBy(t => t.ProjectPath, StringComparer.OrdinalIgnoreCase).ToArray();
            if (paths.Count != 2 || targets.Length != 2 || targets.Select(t => t.Pid).Distinct().Count() != 2) throw new InvalidOperationException("Two exact authorized instances required.");
            Write("bindings.json", new { targets, enumeration_ms = enumerationMs });
            var config = new HostConfiguration("1.0", root, repository, targets.Select(t => t.ProjectKey).ToArray(), V19WorkerExe: worker, V19: new V19Settings(targets, projects, apartment));
            Write("host-config.json", config);
            await StartHost(configPath);
            using var client = new BridgeClient(Path.Combine(root, "api", "host.json"));
            var store = new SqliteOperationStore(StorageLocation.Open(root, repository));
            for (int cycle = 0; cycle < cycles; cycle++)
                foreach (var target in targets)
                {
                    var result = await Inspect(client, target, cycle);
                    cases.Add(result); Write("run-progress.json", new { cases });
                    if (result.State != "SUCCEEDED") throw new InvalidOperationException("Read-only cycle did not succeed; preserve evidence and stop conflicting calls.");
                }
            // A real CLI process reads the same durable operation API; it has no executor of its own.
            string cli = Path.Combine(repository, "src", "TiaBridge.Cli", "bin", "Release", "net10.0", "TiaBridge.Cli.dll");
            string health = await RunProcess("dotnet", [cli, "health", "--discovery", Path.Combine(root, "api", "host.json"), "--json"], 10);
            Write("cli-health.json", JsonDocument.Parse(health).RootElement.Clone());
            for (int i = 0; i < 2; i++) AssertPortal(targets[i]);
            if (faults) { await StopHost(); await FaultMatrix(config, projects); }
            resultCode = 0;
        }
        catch (Exception e) { Write("failure.json", new { error_type = e.GetType().FullName, reason = e.Message }); }
        finally
        {
            try { await StopHost(); } catch (Exception e) { Write("cleanup-failure.json", new { error = e.GetType().FullName }); resultCode = 1; }
            var after = Snapshot(projects); Write("project-files-after.json", after);
            Write("qualification.json", new
            {
                status = resultCode == 0 ? "PASS" : "FAIL",
                cases,
                status_http_ms = Distribution(statusTimes),
                health_http_ms = Distribution(healthTimes),
                result_http_ms = Distribution(resultTimes),
                events_http_ms = Distribution(eventTimes),
                clients = 8,
                project_file_metadata_unchanged = JsonSerializer.Serialize(before) == JsonSerializer.Serialize(after),
                runtime = "NOT_RUN",
                qualification_scope = "read-only engineering; no project writes; not production stability"
            });
            Console.WriteLine(JsonSerializer.Serialize(new { status = resultCode == 0 ? "PASS" : "FAIL", evidence_root = root, cycles_completed = cases.Count }));
        }
        return resultCode;
    }
    private sealed record ReadCase(string ProjectKey, int Pid, int Cycle, string OperationId, string State, double SubmitMs, V19Observation? Observation, object HostResources, object? WorkerResources);
    private static async Task<ReadCase> Inspect(BridgeClient client, V19Target target, int cycle)
    {
        var request = JsonSerializer.SerializeToUtf8Bytes(new { schema_version = "1.0", method = "project.inspect", project_key = target.ProjectKey, idempotency_key = Guid.NewGuid().ToString("N"), @params = new { inventory_scope = "all_supported", fresh = true, timeout_s = 120 } });
        var timer = Stopwatch.StartNew(); var accepted = await client.SendAsync(HttpMethod.Post, "/v1/operations", request); double submit = timer.Elapsed.TotalMilliseconds;
        if (accepted.StatusCode != 202) throw new InvalidOperationException("Admission rejected.");
        var id = ContractJson.ReadAcceptance(accepted.Json).OperationId;
        OperationReport report;
        timer.Restart();
        do
        {
            async Task<(ClientResponse Response, double Ms)> Timed(string route)
            { var watch = Stopwatch.StartNew(); var response = await client.SendAsync(HttpMethod.Get, route); return (response, watch.Elapsed.TotalMilliseconds); }
            var statuses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Timed("/v1/operations/" + id)));
            statusTimes.AddRange(statuses.Select(s => s.Ms)); report = ContractJson.ReadReport(statuses[0].Response.Json);
            var health = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Timed("/v1/diagnostics/health"))); healthTimes.AddRange(health.Select(s => s.Ms));
            var result = await Timed("/v1/operations/" + id + "/result"); resultTimes.Add(result.Ms);
            var events = await Timed("/v1/operations/" + id + "/events"); eventTimes.Add(events.Ms);
            File.WriteAllText(Path.Combine(root, "independent-workspace.txt"), "synthetic edit during read; " + id);
            if (timer.Elapsed > TimeSpan.FromSeconds(125)) throw new TimeoutException("Bounded engineering wait expired.");
            await Task.Delay(100);
        } while (!OperationLifecycle.IsTerminal(report.State));
        _ = await client.SendAsync(HttpMethod.Get, "/v1/operations/" + id + "/events");
        var terminal = await client.SendAsync(HttpMethod.Get, "/v1/operations/" + id + "/result");
        Write("operation-" + id + ".json", JsonDocument.Parse(terminal.Json).RootElement.Clone());
        var final = ContractJson.ReadReport(terminal.Json);
        V19Observation? observation = null;
        if (final.State == OperationState.SUCCEEDED)
        {
            var store = new SqliteOperationStore(StorageLocation.Open(root, repository));
            var inspectResult = (ProjectInspectResult)final.Result!;
            using var proof = store.Artifacts.OpenVerified(final.ArtifactRefs.Single(a => !inspectResult.CoverageRefs.Contains(a.ArtifactId)).ArtifactId);
            using var data = JsonDocument.Parse(proof.Content); observation = Protocol.Read<V19Observation>(data.RootElement);
            AssertPortal(target);
        }
        Console.WriteLine(JsonSerializer.Serialize(new { cycle, target = target.Pid, state = final.State.ToString(), operation_id = id.ToString() }));
        using var runningHost = Process.GetProcessById(host!.Id);
        var resources = new { pid = runningHost.Id, ram_bytes = runningHost.WorkingSet64, handles = runningHost.HandleCount, cpu_ms = runningHost.TotalProcessorTime.TotalMilliseconds };
        object? workerResources = null;
        using (var sql = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(root, "operations.sqlite3"), Mode = SqliteOpenMode.ReadOnly }.ToString()))
        {
            sql.Open(); using var query = sql.CreateCommand(); query.CommandText = "SELECT pid,start_ticks,recorded_at FROM worker_processes ORDER BY recorded_at DESC LIMIT 1";
            using var reader = query.ExecuteReader();
            if (reader.Read())
            {
                using var child = Process.GetProcessById(reader.GetInt32(0));
                if (child.StartTime.ToUniversalTime().Ticks != reader.GetInt64(1) || !string.Equals(child.MainModule!.FileName, worker, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Resource observation identity mismatch.");
                workerResources = new
                {
                    pid = child.Id,
                    ram_bytes = child.WorkingSet64,
                    handles = child.HandleCount,
                    cpu_ms = child.TotalProcessorTime.TotalMilliseconds,
                    startup_registration_ms = (DateTimeOffset.Parse(reader.GetString(2)) - new DateTimeOffset(child.StartTime.ToUniversalTime())).TotalMilliseconds
                };
            }
        }
        return new(target.ProjectKey, target.Pid, cycle, id.ToString(), final.State.ToString(), submit, observation, resources, workerResources);
    }
    private static async Task StartHost(string config, string? mode = null)
    {
        string dll = Path.Combine(repository, "src", "TiaBridge.Host", "bin", "Release", "net10.0", "TiaBridge.Host.dll");
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        string probe = Path.Combine(repository, "tests", "integration", "TiaBridge.StorageProbe", "bin", "Release", "net10.0", "TiaBridge.StorageProbe.dll");
        foreach (string arg in mode is null ? new[] { dll, "--config", config, "--stdio" } : new[] { probe, root, repository, "http-resilience", mode }) start.ArgumentList.Add(arg);
        host = Process.Start(start)!;
        string? ready = await host.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20));
        if (ready != "{\"status\":\"READY\"}") throw new InvalidOperationException("Own Host startup failed: " + await host.StandardError.ReadToEndAsync());
    }
    private static async Task StopHost()
    {
        if (host is null) return;
        try { if (!host.HasExited) { await host.StandardInput.WriteLineAsync("stop"); host.StandardInput.Close(); await host.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); } }
        finally { host.Dispose(); host = null; }
    }
    private static async Task<string> RunProcess(string file, string[] args, int timeoutSeconds)
    {
        var start = new ProcessStartInfo(file) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!; var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(timeoutSeconds));
        if (process.ExitCode != 0) throw new InvalidOperationException("Own child failed: " + await error);
        return await output;
    }
    private static object Snapshot(string projects) => Directory.GetFiles(projects, "*", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase)
        .Select(f => { var info = new FileInfo(f); return new { relative = Path.GetRelativePath(projects, f), length = info.Length, mtime_utc = info.LastWriteTimeUtc }; }).ToArray();
    private static void AssertPortal(V19Target target)
    { using var process = Process.GetProcessById(target.Pid); if (process.HasExited || process.StartTime.ToUniversalTime().Ticks != target.StartTicks) throw new InvalidOperationException("TIA process identity changed."); }
    private static object Distribution(List<double> samples)
    {
        var sorted = samples.Order().ToArray();
        double? P(double p) => sorted.Length == 0 ? null : sorted[(int)Math.Ceiling(sorted.Length * p) - 1];
        return new { samples = sorted.Length, p50_ms = P(.5), p95_ms = P(.95), p99_ms = sorted.Length >= 100 ? P(.99) : null, maximum_ms = sorted.Length == 0 ? (double?)null : sorted[^1] };
    }
    private static void Write(string file, object value) => File.WriteAllText(Path.Combine(root, file), JsonSerializer.Serialize(value, Json), new UTF8Encoding(false));
}
