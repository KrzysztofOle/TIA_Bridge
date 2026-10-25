using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;
using TiaBridge.Core.Transport.V1;
using TiaBridge.Ipc;
using Xunit;

namespace TiaBridge.Host.Tests;

[CollectionDefinition("API performance", DisableParallelization = true)]
public class ApiPerformanceCollection { }

[SupportedOSPlatform("windows")]
[Collection("API performance")]
public class ApiPerformanceTests
{
    private static object Distribution(IEnumerable<double> values)
    {
        double[] sorted = values.Order().ToArray();
        return new { samples = sorted.Length, p50_ms = sorted[(int)Math.Ceiling(sorted.Length * .50) - 1], p95_ms = sorted[(int)Math.Ceiling(sorted.Length * .95) - 1], p99_ms = sorted[(int)Math.Ceiling(sorted.Length * .99) - 1], maximum_ms = sorted[^1] };
    }
    private static double Percentile(IEnumerable<double> values, double p) { double[] sorted = values.Order().ToArray(); return sorted[(int)Math.Ceiling(sorted.Length * p) - 1]; }
    [Fact]
    public async Task One_and_eight_clients_keep_status_and_workspace_responsive_during_real_worker_hang()
    {
        await using var f = new ApiFixture(); await f.Start(FakeScenario.Hang); using var client = f.Client(); var id = await f.Submit(client, ApiFixture.Request(timeout: 120));
        await f.Until(client, id, r => r.Phase == "EXECUTING");
        async Task<double[]> StatusSamples(int parallel)
        {
            using var http = f.Raw(); var samples = new ConcurrentBag<double>();
            await Task.WhenAll(Enumerable.Range(0, parallel).Select(async clientIndex =>
            {
                for (int i = 0; i < 1000 / parallel; i++)
                {
                    var watch = Stopwatch.StartNew(); using var response = await http.GetAsync("/v1/operations/" + id); _ = await response.Content.ReadAsByteArrayAsync();
                    samples.Add(watch.Elapsed.TotalMilliseconds); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                }
            })); return samples.ToArray();
        }
        // Warm schema/HTTP pools before the measured profile, retaining every measured sample thereafter.
        using (var warm = f.Raw()) for (int i = 0; i < 8; i++) { using var response = await warm.GetAsync("/v1/operations/" + id); _ = await response.Content.ReadAsByteArrayAsync(); }
        double[] single = await StatusSamples(1); await Task.Delay(1100); double[] eight = await StatusSamples(8);
        var admissions = new ConcurrentBag<double>();
        using (var http = f.Raw())
            await Task.WhenAll(Enumerable.Range(0, 8).Select(async clientIndex =>
            {
                for (int i = 0; i < 8; i++)
                {
                    using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/operations"); request.Content = new ByteArrayContent(ApiFixture.Request(timeout: 120)); request.Content.Headers.ContentType = new("application/json");
                    var watch = Stopwatch.StartNew(); using var response = await http.SendAsync(request); byte[] bytes = await response.Content.ReadAsByteArrayAsync(); admissions.Add(watch.Elapsed.TotalMilliseconds);
                    Assert.Equal(HttpStatusCode.Accepted, response.StatusCode); _ = ContractJson.ReadAcceptance(bytes);
                }
            }));
        string parent = Path.GetFullPath(Path.Combine(f.Files.Repository, ".tia-bridge", "b02c", "workspaces")); Directory.CreateDirectory(parent);
        string workspace = Path.Combine(parent, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(workspace);
        if (!Path.GetFullPath(workspace).StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unsafe synthetic workspace.");
        string editable = Path.Combine(workspace, "editable.scl"), temporary = Path.Combine(workspace, "editable.tmp");
        byte[] payload = Enumerable.Repeat((byte)0x41, 1024 * 1024).ToArray(); var edits = new List<double>();
        try
        {
            for (int i = 0; i < 100; i++)
            {
                payload[0] = (byte)i; var watch = Stopwatch.StartNew(); File.WriteAllBytes(editable, payload); edits.Add(watch.Elapsed.TotalMilliseconds);
                Assert.Equal(32, SHA256.HashData(File.ReadAllBytes(editable)).Length); // independent offline work during the same hang.
                await Task.Delay(100); // explicit editor workload: at most ten 1 MiB saves/s, no samples discarded.
            }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); if (File.Exists(editable)) File.Delete(editable); Directory.Delete(workspace, recursive: false); }
        string reportPath = Path.Combine(f.Files.Repository, ".tia-bridge", "b02c", "performance.json");
        File.WriteAllText(reportPath, JsonSerializer.Serialize(new
        {
            schema_version = "1.0",
            observed_at = DateTimeOffset.UtcNow,
            os = RuntimeInformation.OSDescription,
            runtime = RuntimeInformation.FrameworkDescription,
            processors = Environment.ProcessorCount,
            architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            state_filesystem = new DriveInfo(Path.GetPathRoot(f.Files.Root)!).DriveFormat,
            workspace_filesystem = new DriveInfo(Path.GetPathRoot(f.Files.Repository)!).DriveFormat,
            edit_interval_ms = 100,
            fake_scenario = "Hang",
            status_one_client = Distribution(single),
            status_eight_clients = Distribution(eight),
            submit_eight_clients = Distribution(admissions),
            edit_one_mib = Distribution(edits)
        }, new JsonSerializerOptions { WriteIndented = true }));
        Assert.True(Percentile(single, .95) <= 100, "NF-01 single p95: " + Percentile(single, .95));
        Assert.True(Percentile(eight, .95) <= 100, "NF-01 eight p95: " + Percentile(eight, .95));
        Assert.True(Percentile(single, .99) <= 250); Assert.True(Percentile(eight, .99) <= 250);
        Assert.True(Percentile(admissions, .95) <= 250, "NF-02 submit p95: " + Percentile(admissions, .95)); Assert.True(Percentile(admissions, .99) <= 500);
        Assert.True(Percentile(edits, .95) <= 100, "NF-03 edit p95: " + Percentile(edits, .95));
    }
    [Fact]
    public async Task Queue_is_bounded_at_one_thousand_and_duplicate_health_and_status_still_work()
    {
        using var evidence = new ResilienceEvidence("queue-capacity", "NF-15", "NF-08");
        await using var f = new ApiFixture(); await f.Start(FakeScenario.Hang); using var client = f.Client(); byte[] first = ApiFixture.Request(timeout: 120); var id = await f.Submit(client, first);
        await f.Until(client, id, r => r.Phase == "EXECUTING");
        for (int i = 1; i < 999; i++) { _ = await f.Submit(client, ApiFixture.Request(timeout: 120)); if (i % 128 == 0) await Task.Delay(1000); }
        await Task.Delay(1100);
        var raced = await Task.WhenAll(Enumerable.Range(0, 4).Select(i => client.SendAsync(HttpMethod.Post, "/v1/operations", ApiFixture.Request(timeout: 120))));
        Assert.Equal(1, raced.Count(r => r.StatusCode == 202)); Assert.Equal(3, raced.Count(r => r.StatusCode == 429));
        var full = await client.SendAsync(HttpMethod.Post, "/v1/operations", ApiFixture.Request()); Assert.Equal(429, full.StatusCode);
        Assert.Equal(TiaBridge.Core.Operations.ErrorCode.QUEUE_FULL, ContractJson.ReadError(full.Json).Code);
        Assert.Equal(1000, f.Files.Open().ActiveCount); Assert.Equal(1000, f.Files.Count("operations"));
        Assert.Equal(200, (await client.SendAsync(HttpMethod.Post, "/v1/operations", first)).StatusCode);
        var health = await client.SendAsync(HttpMethod.Get, "/v1/diagnostics/health"); Assert.Equal(200, health.StatusCode);
        Assert.Contains(Assert.IsType<DiagnosticsHealthResult>(ContractJson.ReadReport(health.Json).Result).Components, c => c.Component == "scheduler" && c.Status == ComponentHealth_Status.HEALTHY);
        Assert.Equal(200, (await client.SendAsync(HttpMethod.Get, "/v1/operations/" + id)).StatusCode);
        f.HostProcess.Refresh(); evidence.Data["active_operations"] = f.Files.Open().ActiveCount; evidence.Data["host_ram_mib"] = f.HostProcess.WorkingSet64 / 1048576d;
        evidence.Data["host_handles"] = f.HostProcess.HandleCount; evidence.Data["host_threads"] = f.HostProcess.Threads.Count; evidence.Data["owned_processes"] = 2; evidence.Pass();
    }
}
