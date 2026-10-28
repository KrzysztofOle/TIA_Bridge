using System.Text.Json;
using Xunit;

namespace TiaBridge.Host.Tests;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
[Collection("HTTP API process")]
public sealed class SenderLifetimeTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    public async Task Net48_sender_preserves_IPC11_and_cleanup_evidence(bool heartbeat, bool gc, bool disconnect)
    {
        using var files = new StoreFixture(); _ = files.Location;
        string path = Path.Combine(files.Root, "spec.json");
        InfrastructureInvestigation.WriteNew(path, new { id = "regression", git_sha = "NOT_AVAILABLE", build_sha = "NOT_AVAILABLE", operations = 2, delay_ms = 0, idle_seconds = 3, heartbeat, controlled_gc = gc, observe_flush = gc, observe_apm = gc, inline_flush = false, recovery = true, disconnect });
        Assert.Equal(0, await SenderLifetimeInvestigation.Run(files.Root, files.Repository, path));
        using var ended = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(files.Root, "ended.json")));
        Assert.Equal("DISPOSED", ended.RootElement.GetProperty("cleanup").GetString());
        Assert.Equal(disconnect ? "PIPE_LOSS" : "STOP", ended.RootElement.GetProperty("termination").GetString());
        using var checks = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(files.Root, "cleanup-checks.json")));
        Assert.Equal(5, checks.RootElement.GetArrayLength());
        Assert.All(checks.RootElement.EnumerateArray(), c => Assert.True(c.GetProperty("disposed").GetBoolean()));
        using var exclusive = new FileStream(Path.Combine(files.Root, "metrics.jsonl"), FileMode.Open, FileAccess.Read, FileShare.None);
        using var runtime = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(files.Root, "runtime.json")));
        Assert.False(runtime.RootElement.GetProperty("vendor_loaded").GetBoolean());
        if (gc)
        {
            var snapshots = Directory.GetFiles(files.Root, "types-*.json").Select(p => JsonDocument.Parse(File.ReadAllBytes(p))).ToArray();
            try
            {
                var last = snapshots.OrderBy(d => d.RootElement.GetProperty("sequence").GetInt32()).Last().RootElement.GetProperty("ownership");
                Assert.True(last.GetProperty("apm_ended").GetInt64() > 0);
                Assert.Equal(last.GetProperty("apm_ended").GetInt64(), last.GetProperty("apm_closed").GetInt64());
                // A scalable runtime pool is allowed. No per-send dedicated thread is expected.
                Assert.All(last.GetProperty("threads").EnumerateArray().Where(t => t.GetProperty("role").GetString() != "Hello"), t => Assert.True(t.GetProperty("thread_pool").GetBoolean()));
            }
            finally { foreach (var snapshot in snapshots) snapshot.Dispose(); }
        }
    }
}
