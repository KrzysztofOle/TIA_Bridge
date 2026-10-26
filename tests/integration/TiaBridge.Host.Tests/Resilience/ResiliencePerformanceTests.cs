using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using TiaBridge.Core.Operations;
using TiaBridge.Core.Transport.V1;
using TiaBridge.Ipc;
using Xunit;

namespace TiaBridge.Host.Tests;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
[Collection("HTTP API process")]
public class ResiliencePerformanceTests
{
    internal static Dictionary<string, double> Distribution(IEnumerable<double> source)
    {
        double[] v = source.Order().ToArray(); return new() { ["samples"] = v.Length, ["p50_ms"] = v[(int)Math.Ceiling(v.Length * .5) - 1], ["p95_ms"] = v[(int)Math.Ceiling(v.Length * .95) - 1], ["p99_ms"] = v[(int)Math.Ceiling(v.Length * .99) - 1], ["maximum_ms"] = v[^1] };
    }
    private static object Resources(Process p) { p.Refresh(); return new { pid = p.Id, start_ticks = p.StartTime.ToUniversalTime().Ticks, working_set_bytes = p.WorkingSet64, private_bytes = p.PrivateMemorySize64, handles = p.HandleCount, threads = p.Threads.Count, cpu_ms = p.TotalProcessorTime.TotalMilliseconds }; }
    [Fact]
    public async Task Independent_workspace_save_and_replace_remains_available_during_a_hung_worker()
    {
        using var evidence = new ResilienceEvidence("workspace-replace", "NF-03", "RT-01");
        await using var f = new ApiFixture();
        using var performance = new PerformanceEvidence(f, "workspace-replace", nameof(Independent_workspace_save_and_replace_remains_available_during_a_hung_worker));
        var samples = new List<double>();
        performance.Register("nf03-ntfs-save-replace", () => samples, 100, 100, null, f.Files.Root);
        await f.Start(FakeScenario.Hang); using var client = f.Client(); var id = await f.Submit(client, ApiFixture.Request(timeout: 120)); await f.Until(client, id, r => r.Phase == "EXECUTING");
        string parent = Path.GetFullPath(Path.Combine(f.Files.Root, "workspaces")); Directory.CreateDirectory(parent);
        string root = Path.GetFullPath(Path.Combine(parent, Guid.NewGuid().ToString("N"))); Assert.StartsWith(parent + Path.DirectorySeparatorChar, root, StringComparison.OrdinalIgnoreCase); Directory.CreateDirectory(root);
        string target = Path.Combine(root, "editable.scl"), temporary = Path.Combine(root, "editable.tmp");
        byte[] bytes = Enumerable.Repeat((byte)0x41, 1024 * 1024).ToArray();
        try
        {
            File.WriteAllBytes(target, bytes);
            for (int i = 0; i < 100; i++)
            {
                bytes[0] = (byte)(65 + i % 26); var timer = Stopwatch.StartNew(); File.WriteAllBytes(temporary, bytes); File.Move(temporary, target, overwrite: true); samples.Add(timer.Elapsed.TotalMilliseconds);
                Assert.Equal(System.Security.Cryptography.SHA256.HashData(bytes), System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(target))); await Task.Delay(100);
            }
            var distribution = Distribution(samples); evidence.Data["save_replace_1mib"] = distribution; evidence.Data["filesystem"] = new DriveInfo(Path.GetPathRoot(root)!).DriveFormat; evidence.Data["interval_ms"] = 100;
            Assert.True(distribution["p95_ms"] <= 100, "NF-03 replace p95: " + distribution["p95_ms"]); evidence.Pass(); performance.Pass();
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); if (File.Exists(target)) File.Delete(target); Directory.Delete(root, recursive: false); }
    }
    [Fact]
    public async Task Repository_volume_replace_is_compared_with_and_without_a_worker_without_unsafe_fallback()
    {
        using var evidence = new ResilienceEvidence("repository-replace-control", "NF-03", "RT-01");
        await using var f = new ApiFixture();
        using var performance = new PerformanceEvidence(f, "repository-replace-control", nameof(Repository_volume_replace_is_compared_with_and_without_a_worker_without_unsafe_fallback));
        string parent = Path.GetFullPath(Path.Combine(f.Files.Repository, ".tia-bridge", "resilience", "workspaces")); Directory.CreateDirectory(parent);
        string root = Path.Combine(parent, Guid.NewGuid().ToString("N")); Assert.StartsWith(parent + Path.DirectorySeparatorChar, Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase); Directory.CreateDirectory(root);
        string target = Path.Combine(root, "owned-target.txt"), temporary = Path.Combine(root, "owned-source.tmp");
        var ownedPaths = new List<string>();
        async Task<object> Probe(string label)
        {
            byte[] original = Enumerable.Repeat((byte)0x41, 1024 * 1024).ToArray(), replacement = Enumerable.Repeat((byte)0x42, 1024 * 1024).ToArray();
            var times = new List<double>(); var errors = new List<int>();
            performance.Register("nf03-repository-" + label, () => times, 100, 100, null, root, () => errors.Count);
            for (int i = 0; i < 100; i++)
            {
                string sampleTarget = Path.Combine(root, label + "-target-" + i + ".txt"), sampleTemporary = Path.Combine(root, label + "-source-" + i + ".tmp");
                ownedPaths.Add(sampleTarget); ownedPaths.Add(sampleTemporary); File.WriteAllBytes(sampleTarget, original); var timer = Stopwatch.StartNew();
                try { File.WriteAllBytes(sampleTemporary, replacement); File.Move(sampleTemporary, sampleTarget, overwrite: true); Assert.Equal(replacement, File.ReadAllBytes(sampleTarget)); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                { errors.Add(error.HResult); Assert.Equal(original, File.ReadAllBytes(sampleTarget)); }
                times.Add(timer.Elapsed.TotalMilliseconds); await Task.Delay(100);
            }
            var distribution = Distribution(times);
            return new { success = errors.Count == 0 && distribution["p95_ms"] <= 100, refused_samples = errors.Count, hresults = errors.Distinct().ToArray(), distribution };
        }
        try
        {
            var baseline = await Probe("without-worker"); await f.Start(FakeScenario.Hang); using var client = f.Client(); var id = await f.Submit(client, ApiFixture.Request(timeout: 120)); await f.Until(client, id, r => r.Phase == "EXECUTING"); var during = await Probe("with-worker");
            evidence.Data["filesystem"] = new DriveInfo(Path.GetPathRoot(root)!).DriveFormat;
            evidence.Data["without_worker"] = baseline; evidence.Data["with_worker"] = during;
            evidence.Data["original_preserved_on_refusal"] = true; evidence.Data["sample_bytes"] = 1024 * 1024; evidence.Data["interval_ms"] = 100; evidence.Pass(); performance.Pass();
        }
        finally { foreach (string owned in ownedPaths) if (File.Exists(owned)) File.Delete(owned); Directory.Delete(root, recursive: false); }
    }
    [Fact]
    public async Task Health_result_events_and_recovery_are_measured_with_eight_clients_during_hang()
    {
        using var evidence = new ResilienceEvidence("http-readers-recovery", "NF-01", "NF-02", "NF-05", "NF-08");
        await using var f = new ApiFixture(); var start = Stopwatch.StartNew(); await f.Start(FakeScenario.Hang, resilienceMode: "observe");
        var starts = new List<double> { start.Elapsed.TotalMilliseconds }; using var client = f.Client(); var id = await f.Submit(client, ApiFixture.Request(timeout: 120)); await f.Until(client, id, r => r.Phase == "EXECUTING");
        using var raw = f.Raw();
        foreach (string path in new[] { "/v1/diagnostics/health", "/v1/operations/" + id + "/result", "/v1/operations/" + id + "/events" })
        {
            var times = new ConcurrentBag<double>();
            await Task.WhenAll(Enumerable.Range(0, 8).Select(async n =>
            {
                for (int i = 0; i < 125; i++)
                {
                    var watch = Stopwatch.StartNew(); using var response = await raw.GetAsync(path); _ = await response.Content.ReadAsByteArrayAsync(); times.Add(watch.Elapsed.TotalMilliseconds);
                    Assert.Contains((int)response.StatusCode, new[] { 200, 202 });
                }
            }));
            var distribution = Distribution(times); evidence.Data[path.EndsWith("health", StringComparison.Ordinal) ? "health_8" : path.EndsWith("events", StringComparison.Ordinal) ? "events_8" : "result_8"] = distribution;
            if (path.EndsWith("health", StringComparison.Ordinal)) { Assert.True(distribution["p95_ms"] <= 100, "Health p95: " + distribution["p95_ms"]); Assert.True(distribution["p99_ms"] <= 250, "Health p99: " + distribution["p99_ms"]); }
            Assert.True(distribution["maximum_ms"] < 5000); await Task.Delay(1100);
        }
        await ResilienceEvidence.IndependentWork(f); evidence.Data["host_during_hang"] = Resources(f.HostProcess);
        await f.Crash(externally: true); await ResilienceEvidence.WorkerAbsent(await ResilienceEvidence.Marker(f, "worker.json"));
        // Twenty process starts give p95 an actual tail sample; retain every value, including cold-process outliers.
        for (int i = 0; i < 19; i++)
        {
            start.Restart(); await f.Start(); using var current = f.Client(); Assert.Equal(200, (await current.SendAsync(HttpMethod.Get, "/v1/operations/" + id)).StatusCode); starts.Add(start.Elapsed.TotalMilliseconds);
            if (i < 18) await f.Stop();
        }
        var recovered = Distribution(starts); evidence.Data["startup_history"] = recovered; evidence.Data["startup_samples_ms"] = starts; Assert.True(recovered["p95_ms"] <= 5000, "NF-05 startup p95: " + recovered["p95_ms"]);
        using var final = f.Client(); start.Restart(); var next = await f.Submit(final, ApiFixture.Request()); await f.Until(final, next, r => r.State == OperationState.SUCCEEDED); Assert.True(start.Elapsed < TimeSpan.FromSeconds(30));
        evidence.Data["safe_read_after_recovery_ms"] = start.Elapsed.TotalMilliseconds;
        evidence.Data["environment"] = new { os = RuntimeInformation.OSDescription, runtime = RuntimeInformation.FrameworkDescription, cpu_count = Environment.ProcessorCount, architecture = RuntimeInformation.ProcessArchitecture.ToString(), state_fs = new DriveInfo(Path.GetPathRoot(f.Files.Root)!).DriveFormat };
        evidence.Pass();
    }
    [Fact]
    public async Task Five_minute_idle_resource_profile_is_measured_without_shortening_the_NF08_window()
    {
        using var evidence = new ResilienceEvidence("idle-300s", "NF-08");
        await using var f = new ApiFixture(); await f.Start(resilienceMode: "observe"); using var client = f.Client(); var id = await f.Submit(client, ApiFixture.Request()); await f.Until(client, id, r => r.State == OperationState.SUCCEEDED);
        var identity = await ResilienceEvidence.Marker(f, "worker.json"); using var worker = Process.GetProcessById(identity.GetProperty("pid").GetInt32()); Assert.Equal(identity.GetProperty("start_ticks").GetInt64(), worker.StartTime.ToUniversalTime().Ticks);
        _ = f.HostProcess.TotalProcessorTime; await Task.Delay(2000); // explicit warmup, excluded from the five-minute idle window.
        TimeSpan before = f.HostProcess.TotalProcessorTime; var timer = Stopwatch.StartNew(); var ram = new List<long>(); var handles = new List<int>(); var workerRam = new List<long>();
        for (int i = 0; i < 300; i++)
        {
            await Task.Delay(1000); f.HostProcess.Refresh(); worker.Refresh(); ram.Add(f.HostProcess.WorkingSet64); handles.Add(f.HostProcess.HandleCount); workerRam.Add(worker.WorkingSet64);
            Assert.False(f.HostProcess.HasExited); Assert.False(worker.HasExited);
        }
        double cpu = (f.HostProcess.TotalProcessorTime - before).TotalMilliseconds / timer.Elapsed.TotalMilliseconds / Environment.ProcessorCount * 100;
        evidence.Data["duration_s"] = timer.Elapsed.TotalSeconds; evidence.Data["cpu_percent_normalized_all_logical_processors"] = cpu;
        evidence.Data["host_max_ram_mib"] = ram.Max() / 1048576d; evidence.Data["worker_max_ram_mib"] = workerRam.Max() / 1048576d;
        evidence.Data["handles_first"] = handles[0]; evidence.Data["handles_last"] = handles[^1]; evidence.Data["handles_max"] = handles.Max(); evidence.Data["owned_process_count"] = 2;
        Assert.True(timer.Elapsed >= TimeSpan.FromMinutes(5)); Assert.True(cpu < 1, "NF-08 idle CPU: " + cpu); Assert.True(ram.Max() <= 200L * 1048576); Assert.True(workerRam.Max() <= 250L * 1048576);
        evidence.Pass();
    }
    [Fact]
    public async Task One_thousand_and_one_operations_preserve_history_and_recycle_only_a_quiescent_owned_worker()
    {
        using var evidence = new ResilienceEvidence("operations-1001", "NF-11", "NF-12", "RT-15");
        await using var f = new ApiFixture(); await f.Start(); using var client = f.Client();
        var warm = new List<object>(); var view = f.Files.Open(); var accepted = new HashSet<Guid>(); var duration = Stopwatch.StartNew();
        long warmRam = 0; int warmHandles = 0;
        for (int i = 0; i < 1001; i++)
        {
            var id = await f.Submit(client, ApiFixture.Request()); await f.Until(client, id, r => r.State == OperationState.SUCCEEDED, milliseconds: 10000);
            Assert.True(accepted.Add(id.Value)); Assert.Equal(1, view.AttemptCount(id));
            if (i is 49 or 999 or 1000) warm.Add(Resources(f.HostProcess));
            if (i == 49) { f.HostProcess.Refresh(); warmRam = f.HostProcess.WorkingSet64; warmHandles = f.HostProcess.HandleCount; }
        }
        Assert.Equal(1001, f.Files.Count("operations")); Assert.Equal(1001, f.Files.Count("operation_results"));
        var effects = Directory.EnumerateFiles(f.Files.Root, "fake-effects-*.jsonl").SelectMany(File.ReadAllLines).Select(line => JsonSerializer.Deserialize<JsonElement>(line)).GroupBy(row => row.GetProperty("operation_id").GetGuid()).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(1001, effects.Count); Assert.All(accepted, id => Assert.Equal(1, effects[id]));
        using (var c = f.Files.Connection()) { using var command = c.CreateCommand(); command.CommandText = "SELECT count(DISTINCT worker_epoch) FROM worker_processes"; Assert.Equal(2L, command.ExecuteScalar()); }
        f.HostProcess.Refresh(); double ramGrowth = (f.HostProcess.WorkingSet64 - warmRam) / 1048576d, handleGrowth = (double)(f.HostProcess.HandleCount - warmHandles) / warmHandles;
        Assert.Equal(accepted.Count, f.Files.Count("operations")); Assert.Equal(accepted.Count, f.Files.Count("operation_results"));
        evidence.Data["completed_operations"] = accepted.Count; evidence.Data["resource_samples"] = warm; evidence.Data["soak_seconds"] = duration.Elapsed.TotalSeconds;
        evidence.Data["ram_growth_mib"] = ramGrowth; evidence.Data["handle_growth_fraction"] = handleGrowth; evidence.Data["duration_is_not_eight_hour_soak"] = true;
        evidence.Pass();
    }
}
