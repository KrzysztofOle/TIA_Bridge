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
        var profile = QualificationProfile.Load(f.Files.Repository);
        var samples = new List<double>();
        performance.Register("nf03-ntfs-save-replace", () => samples, 100, profile.Number("nf03_replace_p95_ms"), null, f.Files.Root);
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
            Assert.True(distribution["p95_ms"] <= profile.Number("nf03_replace_p95_ms"), "NF-03 replace p95: " + distribution["p95_ms"]); evidence.Pass(); performance.Pass();
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); if (File.Exists(target)) File.Delete(target); Directory.Delete(root, recursive: false); }
    }
    [Fact]
    public async Task Repository_volume_replace_is_compared_with_and_without_a_worker_without_unsafe_fallback()
    {
        using var evidence = new ResilienceEvidence("repository-replace-control", "NF-03", "RT-01");
        await using var f = new ApiFixture();
        using var performance = new PerformanceEvidence(f, "repository-replace-control", nameof(Repository_volume_replace_is_compared_with_and_without_a_worker_without_unsafe_fallback));
        var profile = QualificationProfile.Load(f.Files.Repository);
        string parent = Path.GetFullPath(Path.Combine(f.Files.Repository, ".tia-bridge", "resilience", "workspaces")); Directory.CreateDirectory(parent);
        string root = Path.Combine(parent, Guid.NewGuid().ToString("N")); Assert.StartsWith(parent + Path.DirectorySeparatorChar, Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase); Directory.CreateDirectory(root);
        string target = Path.Combine(root, "owned-target.txt"), temporary = Path.Combine(root, "owned-source.tmp");
        var ownedPaths = new List<string>();
        async Task<object> Probe(string label)
        {
            byte[] original = Enumerable.Repeat((byte)0x41, 1024 * 1024).ToArray(), replacement = Enumerable.Repeat((byte)0x42, 1024 * 1024).ToArray();
            var times = new List<double>(); var errors = new List<int>();
            performance.Register("nf03-repository-" + label, () => times, 100, profile.Number("nf03_replace_p95_ms"), null, root, () => errors.Count);
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
            return new { success = errors.Count == 0 && distribution["p95_ms"] <= profile.Number("nf03_replace_p95_ms"), refused_samples = errors.Count, hresults = errors.Distinct().ToArray(), distribution };
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
            if (path.EndsWith("health", StringComparison.Ordinal)) { var healthProfile = QualificationProfile.Load(f.Files.Repository); Assert.True(distribution["p95_ms"] <= healthProfile.Number("nf01_status_p95_ms"), "Health p95: " + distribution["p95_ms"]); Assert.True(distribution["p99_ms"] <= healthProfile.Number("nf01_status_p99_ms"), "Health p99: " + distribution["p99_ms"]); }
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
        var recovered = Distribution(starts); evidence.Data["startup_history"] = recovered; evidence.Data["startup_samples_ms"] = starts; var profile = QualificationProfile.Load(f.Files.Repository); evidence.Data["startup_p95_limit_ms"] = profile.Number("nf05_startup_p95_ms"); Assert.True(recovered["p95_ms"] <= profile.Number("nf05_startup_p95_ms"), "NF-05 startup p95: " + recovered["p95_ms"]);
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
        var profile = QualificationProfile.Load(f.Files.Repository); evidence.Data["qualification_profile"] = profile.Name; evidence.Data["profile_version"] = profile.Version;
        Assert.True(timer.Elapsed >= TimeSpan.FromMinutes(5)); Assert.True(cpu < profile.Number("nf08_host_idle_cpu_mean_percent"), "NF-08 idle CPU: " + cpu); Assert.True(ram.Max() <= profile.Number("nf08_host_ram_mib") * 1048576); Assert.True(workerRam.Max() <= profile.Number("nf08_worker_ram_mib") * 1048576);
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

    [Fact]
    [Trait("Category", "NF11Soak")]
    public async Task NF11_synthetic_soak_is_opt_in_and_persists_full_or_partial_evidence()
    {
        string mode = Environment.GetEnvironmentVariable("TIA_BRIDGE_NF11_MODE") ?? "";
        Assert.Contains(mode, new[] { "FULL", "DIAGNOSTIC" });
        string runId = Environment.GetEnvironmentVariable("TIA_BRIDGE_NF11_RUN_ID") ?? "";
        Assert.Matches("^[0-9a-f]{32}$", runId);
        string repository = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(repository, "TiaBridge.slnx"))) repository = Directory.GetParent(repository)!.FullName;
        string expectedFolder = Path.GetFullPath(Path.Combine(repository, ".tia-bridge", "resilience", runId));
        string outputFolder = Path.GetFullPath(Environment.GetEnvironmentVariable("TIA_BRIDGE_NF11_OUTPUT_DIRECTORY") ?? "");
        Assert.Equal(expectedFolder, outputFolder);
        string stopFile = Path.GetFullPath(Environment.GetEnvironmentVariable("TIA_BRIDGE_NF11_STOP_FILE") ?? "");
        Assert.StartsWith(expectedFolder + Path.DirectorySeparatorChar, stopFile, StringComparison.OrdinalIgnoreCase);

        const int warmupFull = 50;
        const int requiredFullOperations = 1000;
        const int warmupDiagnostic = 3;
        const int diagnosticOperations = 5;
        const int diagnosticSeconds = 15;
        int warmupCount = mode == "FULL" ? warmupFull : warmupDiagnostic;
        int measuredOperations = mode == "FULL" ? requiredFullOperations : diagnosticOperations;
        TimeSpan requiredWindow = mode == "FULL" ? TimeSpan.FromSeconds(28_800) : TimeSpan.FromSeconds(diagnosticSeconds);
        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        DateTimeOffset? measurementStartedAt = null;
        var timer = new Stopwatch();
        using var stop = new CancellationTokenSource();
        await using var fixture = new ApiFixture();
        await using var phaseEvidence = new Nf11SyntheticEvidence(outputFolder);
        var accepted = new HashSet<Guid>();
        var completed = new HashSet<Guid>();
        var workerIdentities = new HashSet<(int Pid, long StartTicks)>();
        var samples = new List<(long Sequence, double ElapsedSeconds, long WorkingSetBytes, int Handles)>();
        long warmupRam = 0;
        int warmupHandles = 0;
        bool interrupted = false;
        bool cleanupConfirmed = false;
        int unmanagedChildProcesses = 0;
        bool hostNeedsStop = false;
        Task? samplerTask = null;
        string executionStatus = "FAIL";
        int hostPid = 0;
        long hostStartTicks = 0;
        double elapsedSeconds = 0;
        try
        {
            await fixture.Start();
            hostNeedsStop = true;
            hostPid = fixture.HostProcess.Id;
            hostStartTicks = fixture.HostProcess.StartTime.ToUniversalTime().Ticks;
            using var client = fixture.Client();

            async Task SubmitAndComplete()
            {
                var operationId = await fixture.Submit(client, ApiFixture.Request());
                Guid id = operationId.Value;
                Assert.True(accepted.Add(id), "Duplicate accepted Operation ID.");
                phaseEvidence.Accepted(id, DateTimeOffset.UtcNow);
                var report = await fixture.Until(client, operationId, r => r.State == OperationState.SUCCEEDED, milliseconds: 10_000);
                Assert.Equal(OperationState.SUCCEEDED, report.State);
                Assert.True(completed.Add(id), "Duplicate completed Operation ID.");
                phaseEvidence.Completed(id, report.State.ToString(), DateTimeOffset.UtcNow);
                TrackWorkers(fixture, workerIdentities);
            }

            for (int i = 0; i < warmupCount; i++)
            {
                if (File.Exists(stopFile)) { interrupted = true; break; }
                await SubmitAndComplete();
            }
            if (!interrupted)
            {
                fixture.HostProcess.Refresh();
                warmupRam = fixture.HostProcess.WorkingSet64;
                warmupHandles = fixture.HostProcess.HandleCount;
                Assert.True(warmupHandles > 0, "Host handle baseline must be positive.");
                startedAt = DateTimeOffset.UtcNow;
                measurementStartedAt = startedAt;
                timer.Start();
                samplerTask = SampleResourcesAsync(fixture, phaseEvidence, samples, workerIdentities, timer, stop, stopFile);
                var hostCpuPrevious = fixture.HostProcess.TotalProcessorTime;
                var cpuTimestampPrevious = timer.Elapsed;
                for (int i = 0; i < measuredOperations && !stop.IsCancellationRequested; i++)
                {
                    TimeSpan target = TimeSpan.FromTicks(requiredWindow.Ticks * (i + 1) / measuredOperations);
                    TimeSpan delay = target - timer.Elapsed;
                    if (delay > TimeSpan.Zero)
                    {
                        try { await Task.Delay(delay, stop.Token); }
                        catch (OperationCanceledException) { break; }
                    }
                    if (File.Exists(stopFile)) { interrupted = true; break; }
                    await SubmitAndComplete();
                    if ((i + 1) % 25 == 0) Console.WriteLine($"NF11 {mode}: operations={completed.Count} elapsed_s={timer.Elapsed.TotalSeconds:F0}");
                }
                if (!stop.IsCancellationRequested && !interrupted && timer.Elapsed < requiredWindow)
                {
                    try { await Task.Delay(requiredWindow - timer.Elapsed, stop.Token); }
                    catch (OperationCanceledException) { }
                }
                if (stop.IsCancellationRequested || File.Exists(stopFile)) interrupted = true;
                stop.Cancel();
                try { await samplerTask; } catch (OperationCanceledException) { } catch { executionStatus = "FAIL"; }
                fixture.HostProcess.Refresh();
                double measuredCpuMs = (fixture.HostProcess.TotalProcessorTime - hostCpuPrevious).TotalMilliseconds;
                double measuredWallMs = Math.Max(1, (timer.Elapsed - cpuTimestampPrevious).TotalMilliseconds);
                _ = measuredCpuMs / measuredWallMs / Environment.ProcessorCount * 100;
                if (!interrupted && executionStatus != "FAIL")
                {
                    Assert.True(accepted.SetEquals(PersistedOperationIds(fixture)), "Accepted Operation IDs differ from durable Host history.");
                    var effects = Directory.EnumerateFiles(fixture.Files.Root, "fake-effects-*.jsonl")
                        .SelectMany(File.ReadAllLines).Select(line => JsonDocument.Parse(line))
                        .GroupBy(document => { using (document) return document.RootElement.GetProperty("operation_id").GetGuid(); })
                        .ToDictionary(group => group.Key, group => group.Count());
                    Assert.True(accepted.SetEquals(effects.Keys), "FakeWorker effects differ from accepted Operation IDs.");
                    Assert.All(effects.Values, count => Assert.Equal(1, count));
                }
            }
            elapsedSeconds = timer.Elapsed.TotalSeconds;
            await fixture.Stop();
            hostNeedsStop = false;
            unmanagedChildProcesses = RemainingOwnedProcesses(hostPid, hostStartTicks, workerIdentities);
            cleanupConfirmed = unmanagedChildProcesses == 0;
            executionStatus = interrupted ? "INTERRUPTED" : mode == "DIAGNOSTIC" ? "DIAGNOSTIC" : "COMPLETE";
        }
        finally
        {
            stop.Cancel();
            if (samplerTask is not null)
            {
                try { await samplerTask; } catch (OperationCanceledException) { } catch { executionStatus = "FAIL"; }
            }
            if (hostNeedsStop)
            {
                try { await fixture.Stop(); } catch { cleanupConfirmed = false; }
            }
            elapsedSeconds = timer.Elapsed.TotalSeconds;
            double ramGrowth = samples.Count == 0 ? 0 : Math.Max(0, (samples.Max(s => s.WorkingSetBytes) - warmupRam) / 1048576d);
            double handleGrowth = samples.Count == 0 || warmupHandles <= 0 ? 0 : Math.Max(0, (double)(samples.Max(s => s.Handles) - warmupHandles) / warmupHandles);
            var result = new
            {
                schema_version = "1.0",
                run_id = runId,
                phase = "NF11_SYNTHETIC",
                mode,
                execution_status = executionStatus,
                started_at = startedAt,
                measurement_started_at = measurementStartedAt,
                ended_at = DateTimeOffset.UtcNow,
                measured_seconds = elapsedSeconds,
                required_seconds = 28_800,
                minimum_operations = 1000,
                warmup_operations = warmupCount,
                accepted_operations = accepted.Count,
                completed_operations = completed.Count,
                lost_operation_ids = accepted.Except(completed).Select(id => id.ToString()).Order().ToArray(),
                operation_ids = completed.Select(id => id.ToString()).Order().ToArray(),
                warmup_host_working_set_bytes = warmupRam,
                warmup_host_handles = warmupHandles,
                ram_growth_mib = ramGrowth,
                handle_growth_fraction = handleGrowth,
                samples_count = samples.Count,
                sampling_interval_seconds = 1,
                cleanup_confirmed = cleanupConfirmed,
                unmanaged_child_processes = unmanagedChildProcesses,
                thresholds = new { maximum_ram_growth_mib = 50, maximum_handle_growth_fraction = .05 },
                diagnostic_must_not_qualify = mode == "DIAGNOSTIC"
            };
            ResilienceEvidence.WriteNew(Path.Combine(outputFolder, "nf11-synthetic-result.json"), JsonSerializer.SerializeToUtf8Bytes(result, new JsonSerializerOptions { WriteIndented = true }));
            phaseEvidence.CompleteManifest(runId);
        }

        if (interrupted) throw new Xunit.Sdk.XunitException("NF-11 synthetic run was controlled-interrupted; partial evidence retained.");
        if (mode == "FULL")
        {
            Assert.True(elapsedSeconds >= 28_800);
            Assert.True(completed.Count >= 1000);
            Assert.Empty(accepted.Except(completed));
            Assert.True(cleanupConfirmed, "An owned Host/FakeWorker process remained after cleanup.");
            Assert.True(samples.Count >= 28_800, "One-second resource samples are incomplete.");
            Assert.True(samples.Max(s => s.WorkingSetBytes) - warmupRam <= 50L * 1024 * 1024);
            Assert.True((double)(samples.Max(s => s.Handles) - warmupHandles) / warmupHandles <= .05);
        }
    }

    private static async Task SampleResourcesAsync(ApiFixture fixture, Nf11SyntheticEvidence evidence,
        List<(long Sequence, double ElapsedSeconds, long WorkingSetBytes, int Handles)> samples,
        HashSet<(int Pid, long StartTicks)> workers, Stopwatch timer, CancellationTokenSource stop, string stopFile)
    {
        using var interval = new PeriodicTimer(TimeSpan.FromSeconds(1));
        double previousElapsed = 0;
        double previousHostCpuMs = fixture.HostProcess.TotalProcessorTime.TotalMilliseconds;
        while (await interval.WaitForNextTickAsync(stop.Token))
        {
            if (File.Exists(stopFile)) { stop.Cancel(); break; }
            fixture.HostProcess.Refresh();
            TrackWorkers(fixture, workers);
            long ws = fixture.HostProcess.WorkingSet64;
            int handles = fixture.HostProcess.HandleCount;
            int threads = fixture.HostProcess.Threads.Count;
            double cpuMs = fixture.HostProcess.TotalProcessorTime.TotalMilliseconds;
            double elapsed = timer.Elapsed.TotalSeconds;
            double elapsedDelta = elapsed - previousElapsed;
            double hostCpuPercent = elapsedDelta <= 0 ? 0 : Math.Max(0, cpuMs - previousHostCpuMs) / (elapsedDelta * 1000 * Environment.ProcessorCount) * 100;
            previousElapsed = elapsed;
            previousHostCpuMs = cpuMs;
            int liveWorkers = 0;
            long workerWs = 0;
            int workerHandles = 0;
            int workerThreads = 0;
            double workerCpuMs = 0;
            (int Pid, long StartTicks)[] workerSnapshot;
            lock (workers) workerSnapshot = workers.ToArray();
            foreach (var identity in workerSnapshot)
            {
                if (!TryProcess(identity.Pid, identity.StartTicks, out var process)) continue;
                using (process)
                {
                    process.Refresh(); liveWorkers++; workerWs += process.WorkingSet64;
                    workerHandles += process.HandleCount; workerThreads += process.Threads.Count;
                    workerCpuMs += process.TotalProcessorTime.TotalMilliseconds;
                }
            }
            long sequence = samples.Count;
            samples.Add((sequence, elapsed, ws, handles));
            evidence.Sample(new
            {
                observed_at_utc = DateTimeOffset.UtcNow,
                elapsed_seconds = elapsed,
                host = new { pid = fixture.HostProcess.Id, start_ticks = fixture.HostProcess.StartTime.ToUniversalTime().Ticks, working_set_bytes = ws, handles, threads, cpu_ms = cpuMs },
                fake_workers = new { tracked_processes = workers.Count, live_processes = liveWorkers, working_set_bytes = workerWs, handles = workerHandles, threads = workerThreads, cpu_ms = workerCpuMs },
                process_counts = new { machine = MachineProcessCount(), host = 1, fake_worker = liveWorkers, tracked_owned_total = 1 + liveWorkers },
                cpu_percent_normalized_all_logical_processors = hostCpuPercent,
                diagnostic = "Raw cumulative CPU milliseconds are preserved; no NF-11 CPU threshold is defined."
            });
        }
    }

    private static void TrackWorkers(ApiFixture fixture, HashSet<(int Pid, long StartTicks)> workers)
    {
        using var connection = fixture.Files.Connection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT pid,start_ticks FROM worker_processes";
        using var reader = command.ExecuteReader();
        lock (workers)
            while (reader.Read()) workers.Add((reader.GetInt32(0), reader.GetInt64(1)));
    }

    private static HashSet<Guid> PersistedOperationIds(ApiFixture fixture)
    {
        var ids = new HashSet<Guid>();
        using var connection = fixture.Files.Connection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT operation_id FROM operations";
        using var reader = command.ExecuteReader();
        while (reader.Read()) ids.Add(Guid.Parse(reader.GetString(0)));
        return ids;
    }

    private static int RemainingOwnedProcesses(int hostPid, long hostStartTicks, HashSet<(int Pid, long StartTicks)> workers)
    {
        int remaining = 0;
        if (TryProcess(hostPid, hostStartTicks, out var host)) { host.Dispose(); remaining++; }
        lock (workers)
        {
            foreach (var identity in workers)
            {
                if (!TryProcess(identity.Pid, identity.StartTicks, out var process)) continue;
                process.Dispose();
                remaining++;
            }
        }
        return remaining;
    }

    private static int MachineProcessCount()
    {
        Process[] processes = Process.GetProcesses();
        try { return processes.Length; }
        finally { foreach (Process process in processes) process.Dispose(); }
    }

    private static bool TryProcess(int pid, long startTicks, out Process process)
    {
        try
        {
            process = Process.GetProcessById(pid);
            if (process.StartTime.ToUniversalTime().Ticks == startTicks) return true;
            process.Dispose(); process = null!; return false;
        }
        catch (ArgumentException) { process = null!; return false; }
        catch (InvalidOperationException) { process = null!; return false; }
    }
}
