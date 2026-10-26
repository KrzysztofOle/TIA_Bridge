using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace TiaBridge.Host.Tests;

/// <summary>Evidence only. Never performs I/O inside a measured interval; partial samples survive assertions.</summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal sealed class PerformanceEvidence : IDisposable
{
    private readonly ApiFixture fixture;
    private readonly string scenario;
    private readonly string testName;
    private readonly DateTimeOffset started = DateTimeOffset.UtcNow;
    private readonly string gitSha;
    private readonly bool dirty;
    private readonly Dictionary<string, string> binaries = new();
    private readonly Dictionary<string, string> sources = new();
    private readonly List<Func<object>> metrics = new();
    private bool passed;
    internal object? Summary { get; set; }

    internal PerformanceEvidence(ApiFixture fixture, string scenario, string testName)
    {
        this.fixture = fixture; this.scenario = scenario; this.testName = testName;
        string Git(params string[] args)
        {
            var info = new ProcessStartInfo("git") { WorkingDirectory = fixture.Files.Repository, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string arg in args) info.ArgumentList.Add(arg);
            using var process = Process.Start(info)!;
            var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(10000)) throw new TimeoutException("Evidence Git metadata timeout.");
            if (process.ExitCode != 0) throw new InvalidOperationException("Evidence Git metadata unavailable.");
            _ = error.GetAwaiter().GetResult(); return output.GetAwaiter().GetResult().Trim();
        }
        gitSha = Git("rev-parse", "HEAD"); dirty = Git("status", "--porcelain").Length != 0;
        foreach (string path in new[] { typeof(PerformanceEvidence).Assembly.Location, fixture.Binary("TiaBridge.Core"), fixture.Binary("TiaBridge.Host"), fixture.Binary("TiaBridge.Ipc"), fixture.Binary("TiaBridge.FakeWorker") })
            binaries.Add(Path.GetRelativePath(fixture.Files.Repository, path).Replace('\\', '/'), Hash(path));
        foreach (string relative in new[] { "tests/integration/TiaBridge.Host.Tests/ApiPerformanceTests.cs", "tests/integration/TiaBridge.Host.Tests/Resilience/ResiliencePerformanceTests.cs", "tests/integration/TiaBridge.Host.Tests/Resilience/PerformanceEvidence.cs" })
            sources.Add(relative, Hash(Path.Combine(fixture.Files.Repository, relative)));
    }

    internal void Register(string id, Func<IEnumerable<double>> samples, int expected, double p95, double? p99, string workspace, Func<int>? refused = null)
    {
        // Snapshot once on disposal: the original measured values, including every outlier and partial result.
        metrics.Add(() =>
        {
            double[] values = samples().ToArray(); int errors = refused?.Invoke() ?? 0;
            var distribution = values.Length == 0 ? null : ResiliencePerformanceTests.Distribution(values);
            string status = values.Length != expected ? "INCOMPLETE" : errors != 0 || distribution!["p95_ms"] > p95 || p99 is not null && distribution["p99_ms"] > p99 ? "FAIL" : "PASS";
            return new { id, unit = "ms", time_kind = "wall_clock", order = "collection_snapshot_not_concurrent_completion_order", expected_samples = expected, sample_count = values.Length, samples = values.Select((value, index) => new { id = index, value }).ToArray(), distribution, p95_limit_ms = p95, p99_limit_ms = p99, refused_samples = errors, status, workspace = Path.GetFullPath(workspace), filesystem = new DriveInfo(Path.GetPathRoot(workspace)!).DriveFormat };
        });
    }
    internal void Pass() => passed = true;
    internal static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    internal static void WriteNew(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(bytes); stream.Flush(true);
    }
    public void Dispose()
    {
        string folder = Path.Combine(fixture.Files.Repository, ".tia-bridge", "resilience", ResilienceEvidence.CurrentRunId, "performance");
        string name = scenario + ".json";
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schema_version = "1.1",
            kind = "nf-performance",
            run_id = ResilienceEvidence.CurrentRunId,
            scenario,
            test_name = testName,
            started_at = started,
            ended_at = DateTimeOffset.UtcNow,
            git_sha = gitSha,
            git_dirty = dirty,
            binaries_sha256 = binaries,
            sources_sha256 = sources,
            runtime = RuntimeInformation.FrameworkDescription,
            os = RuntimeInformation.OSDescription,
            processors = Environment.ProcessorCount,
            state_path = fixture.Files.Root,
            state_filesystem = new DriveInfo(Path.GetPathRoot(fixture.Files.Root)!).DriveFormat,
            fake_scenario = "Hang",
            config_sha256 = File.Exists(fixture.ConfigPath) ? Hash(fixture.ConfigPath) : null,
            test_status = passed ? "PASS" : "FAIL",
            metrics = metrics.Select(capture => capture()).ToArray(),
            summary = Summary
        }, new JsonSerializerOptions { WriteIndented = true });
        WriteNew(Path.Combine(folder, name), bytes);
        WriteNew(Path.Combine(folder, scenario + ".manifest.json"), JsonSerializer.SerializeToUtf8Bytes(new { schema_version = "1.1", kind = "nf-performance-manifest", run_id = ResilienceEvidence.CurrentRunId, artifact = name, bytes = bytes.Length, sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)) }));
    }
}
