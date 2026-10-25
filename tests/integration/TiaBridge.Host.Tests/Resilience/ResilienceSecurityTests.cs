using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text.Json;
using TiaBridge.Ipc;
using Xunit;

namespace TiaBridge.Host.Tests;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
[Collection("HTTP API process")]
public class ResilienceSecurityTests
{
    [Theory]
    [InlineData("hmac")]
    [InlineData("epoch")]
    [InlineData("sequence")]
    [InlineData("oversize")]
    [InlineData("json")]
    [InlineData("truncated")]
    [InlineData("version")]
    [InlineData("peer")]
    public async Task Real_process_pipe_attack_is_rejected_without_weakening_acl_or_authentication(string attack)
    {
        using var evidence = new ResilienceEvidence("ipc-" + attack, "RT-15");
        await using var f = new ApiFixture(); _ = f.Files.Location;
        string pipeName = "tia-bridge2-resilience-" + Guid.NewGuid().ToString("N"); Guid epoch = Guid.NewGuid(); byte[] key = RandomNumberGenerator.GetBytes(32);
        using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly | PipeOptions.FirstPipeInstance);
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string arg in new[] { f.ProbeBinary, f.Files.Root, f.Files.Repository, "ipc-adversary", attack }) start.ArgumentList.Add(arg);
        using var child = Process.Start(start)!; using var own = Process.GetCurrentProcess();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            await child.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new Bootstrap(pipeName, Convert.ToBase64String(key), epoch, Guid.NewGuid(), own.Id, own.StartTime.ToUniversalTime().Ticks, f.Files.Root), Protocol.Json)); child.StandardInput.Close();
            await server.WaitForConnectionAsync(deadline.Token);
            if (attack == "peer") Assert.Throws<ProtocolException>(() => PipePeer.Verify(server, own.Id, server: true));
            else
            {
                PipePeer.Verify(server, child.Id, server: true);
                await using var framed = new FramedConnection(server, key, epoch);
                await Assert.ThrowsAnyAsync<IOException>(() => framed.ReadAsync(deadline.Token));
            }
            server.Dispose(); await child.WaitForExitAsync(deadline.Token); Assert.Equal(0, child.ExitCode);
            Assert.Empty(await child.StandardOutput.ReadToEndAsync()); Assert.Empty(await child.StandardError.ReadToEndAsync());
            evidence.Data["attack"] = attack; evidence.Data["peer_pid"] = child.Id; evidence.Data["effect_count"] = 0; evidence.Pass();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            if (!child.HasExited) { child.Kill(entireProcessTree: false); await child.WaitForExitAsync(); }
        }
    }
    [Fact]
    public async Task Owned_host_shutdown_does_not_terminate_an_unrelated_same_name_process()
    {
        using var evidence = new ResilienceEvidence("unrelated-process-survives", "RT-15");
        await using var f = new ApiFixture(); using var other = new StoreFixture(); _ = other.Location;
        var info = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string arg in new[] { f.ProbeBinary, other.Root, other.Repository, "lock-child", "none" }) info.ArgumentList.Add(arg);
        using var sentinel = Process.Start(info)!;
        try
        {
            var timer = Stopwatch.StartNew(); while (!File.Exists(Path.Combine(other.Root, "child.json"))) { Assert.True(timer.Elapsed < TimeSpan.FromSeconds(10)); await Task.Delay(10); }
            long ticks = sentinel.StartTime.ToUniversalTime().Ticks;
            await f.Start(FakeScenario.Hang, mutation: true); using var client = f.Client(); var id = await f.Submit(client, ApiFixture.Request("sync.apply", timeout: 1));
            await f.Until(client, id, r => r.State == TiaBridge.Core.Operations.OperationState.RECONCILIATION_REQUIRED); await f.Stop();
            Assert.False(sentinel.HasExited); Assert.Equal(ticks, sentinel.StartTime.ToUniversalTime().Ticks);
            evidence.Data["unrelated_pid"] = sentinel.Id; evidence.Data["unrelated_start_ticks"] = ticks; evidence.Data["host_did_not_own_or_stop_sentinel"] = true;
        }
        finally { File.WriteAllText(Path.Combine(other.Root, "child.release"), "release"); using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10)); await sentinel.WaitForExitAsync(deadline.Token); }
        evidence.Pass();
    }
    [Fact]
    public async Task Cleanup_after_expected_startup_failure_disposes_owned_process_and_unique_test_directory()
    {
        using var evidence = new ResilienceEvidence("startup-failure-cleanup", "RT-15");
        var f = new ApiFixture(); string root = f.Files.Root;
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Start(workerDllOverride: Path.Combine(root, "missing", "TiaBridge.FakeWorker.dll")));
        await Assert.ThrowsAnyAsync<Exception>(() => f.DisposeAsync().AsTask());
        Assert.False(Directory.Exists(root)); evidence.Data["unique_directory_removed"] = true; evidence.Pass();
    }
}
