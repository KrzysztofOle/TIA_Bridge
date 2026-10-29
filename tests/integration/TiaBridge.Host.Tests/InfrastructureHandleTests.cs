using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using TiaBridge.Ipc;
using Xunit;

namespace TiaBridge.Host.Tests;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
[Collection("HTTP API process")]
public sealed class InfrastructureHandleTests
{
    [Fact]
    public async Task Repeated_net48_connections_keep_fences_epochs_and_release_owned_children()
    {
        using var files = new StoreFixture(); _ = files.Location;
        var epochs = new HashSet<Guid>();
        for (int n = 0; n < 5; n++)
        {
            string root = Path.Combine(files.Root, "connection-" + n); Directory.CreateDirectory(root);
            await using var session = await InfrastructureInvestigation.ProbeSession.Start(root, files.Repository, "worker", true);
            Assert.True(epochs.Add(session.Epoch));
            Guid operation = Guid.NewGuid(); var response = await session.Dispatch(operation, n + 1);
            Assert.Equal(operation, response.OperationId); Assert.Equal(n + 1, response.Fence);
            Assert.Equal("Succeeded", Protocol.Read<WorkerReply>(response.Body).Receipt.Disposition);
            await session.Stop(); Assert.True(session.Child.HasExited);
            using var ended = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "ended.json")));
            Assert.False(ended.RootElement.GetProperty("vendor_loaded").GetBoolean());
            using var audit = new FileStream(Directory.GetFiles(root, "v19-audit-*.jsonl").Single(), FileMode.Open, FileAccess.Read, FileShare.None);
            using var samples = new FileStream(Path.Combine(root, "internal.jsonl"), FileMode.Open, FileAccess.Read, FileShare.None);
        }
    }

    [Theory]
    [InlineData("heartbeat", true)]
    [InlineData("no-heartbeat", false)]
    [InlineData("send-only-no-flush", true)]
    public async Task Isolated_heartbeat_switch_does_not_change_IPC11_and_cleanup(string mode, bool expected)
    {
        using var files = new StoreFixture(); _ = files.Location;
        await using var session = await InfrastructureInvestigation.ProbeSession.Start(files.Root, files.Repository, mode, false);
        await Task.Delay(2300);
        var receipt = await session.Dispatch(Guid.NewGuid(), 3);
        Assert.Equal("1.1", receipt.ProtocolVersion); Assert.Equal(3, receipt.Fence);
        Assert.Equal(expected, session.Heartbeats > 0);
        await session.Stop(); Assert.Equal(0, session.Child.ExitCode);
    }

    [Fact]
    public async Task Synthetic_getter_exception_preserves_receipt_and_releases_audit()
    {
        using var files = new StoreFixture(); _ = files.Location; File.WriteAllText(Path.Combine(files.Root, "fail-read"), "synthetic");
        await using var session = await InfrastructureInvestigation.ProbeSession.Start(files.Root, files.Repository, "worker", true);
        var message = await session.Dispatch(Guid.NewGuid(), 1); var reply = Protocol.Read<WorkerReply>(message.Body);
        Assert.Equal("Failed", reply.Receipt.Disposition); Assert.True(reply.Receipt.Quiescent);
        await session.Stop(); Assert.Equal(0, session.Child.ExitCode);
    }

    [Fact]
    public async Task Observer_timeout_does_not_restart_or_abort_the_owned_STA_action()
    {
        using var files = new StoreFixture(); _ = files.Location; File.WriteAllText(Path.Combine(files.Root, "hang-read"), "synthetic");
        await using var session = await InfrastructureInvestigation.ProbeSession.Start(files.Root, files.Repository, "worker", true);
        int pid = session.Child.Id; Guid epoch = session.Epoch;
        var pending = session.Dispatch(Guid.NewGuid(), 1);
        try { await Assert.ThrowsAsync<TimeoutException>(() => pending.WaitAsync(TimeSpan.FromMilliseconds(30))); }
        finally { File.WriteAllText(Path.Combine(files.Root, "release-read"), "explicit synthetic release"); }
        Assert.Equal("Succeeded", Protocol.Read<WorkerReply>((await pending).Body).Receipt.Disposition);
        Assert.Equal(pid, session.Child.Id); Assert.Equal(epoch, session.Epoch); await session.Stop();
    }

    [Theory]
    [InlineData("sampler")]
    [InlineData("audit")]
    public async Task Sampler_and_audit_wrappers_close_streams_after_repeated_observation(string mode)
    {
        using var files = new StoreFixture(); _ = files.Location;
        File.WriteAllText(Path.Combine(files.Root, "phase-activity"), "activity");
        await using var session = await InfrastructureInvestigation.ProbeSession.Start(files.Root, files.Repository, mode, false);
        await Task.Delay(2300); await session.Stop();
        using var stream = new FileStream(Path.Combine(files.Root, "internal.jsonl"), FileMode.Open, FileAccess.Read, FileShare.None);
        if (mode == "audit")
        { using var audit = new FileStream(Path.Combine(files.Root, "audit.jsonl"), FileMode.Open, FileAccess.Read, FileShare.None); Assert.True(audit.Length > 0); }
    }

    [Fact]
    public void Infrastructure_binary_has_no_vendor_assembly_reference()
    {
        using var files = new StoreFixture();
        string exe = Path.Combine(files.Repository, "tests/integration/TiaBridge.Worker.InfrastructureProbe/bin/Release/net48/TiaBridge.Worker.InfrastructureProbe.exe");
        using var file = File.OpenRead(exe); using var pe = new PEReader(file); var reader = pe.GetMetadataReader();
        Assert.DoesNotContain(reader.AssemblyReferences.Select(h => reader.GetString(reader.GetAssemblyReference(h).Name)), name => name.StartsWith("Siemens.", StringComparison.Ordinal));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(exe)!, "Siemens.*"));
    }

    [Fact]
    public async Task Fake_comparison_uses_the_real_health_route_and_returns_a_valid_report()
    {
        using var files = new StoreFixture(); _ = files.Location;
        string spec = Path.Combine(files.Root, "spec.json");
        InfrastructureInvestigation.WriteNew(spec, new InfrastructureInvestigation.Specification("C", 1, 1, 3));
        Assert.Equal(0, await InfrastructureInvestigation.Run(files.Root, files.Repository, spec));
        using var health = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(files.Root, "health-final.json")));
        Assert.Equal("diagnostics.health", health.RootElement.GetProperty("method").GetString());
        Assert.Equal("SUCCEEDED", health.RootElement.GetProperty("state").GetString());
        using var exclusive = new FileStream(Path.Combine(files.Root, "state/operations.sqlite3"), FileMode.Open, FileAccess.Read, FileShare.None);
    }
}
