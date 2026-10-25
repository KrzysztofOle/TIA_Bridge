using System.Diagnostics;
using System.Net.Http.Headers;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Nodes;
using TiaBridge.Client;
using TiaBridge.Core.Identity;
using TiaBridge.Core.Operations;
using TiaBridge.Core.Transport.V1;
using TiaBridge.Host.Api;
using TiaBridge.Host.Operations;
using TiaBridge.Ipc;
using Xunit;

namespace TiaBridge.Host.Tests;

[SupportedOSPlatform("windows")]
internal sealed class ApiFixture : IAsyncDisposable
{
    internal StoreFixture Files { get; } = new();
    internal string DiscoveryPath => Path.Combine(Files.Root, "api", "host.json");
    internal string ConfigPath => Path.Combine(Files.Root, "host-config.json");
    internal HostDiscovery Discovery { get; private set; } = null!;
    private Process? host;
    internal Process HostProcess => host ?? throw new InvalidOperationException("No owned Host.");
    internal string ProbeBinary => Path.Combine(Files.Repository, "tests", "integration", "TiaBridge.StorageProbe", "bin", new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name, "net10.0", "TiaBridge.StorageProbe.dll");
    private Task<string>? stdout, stderr;
    internal string Binary(string project) => Path.Combine(Files.Repository, "src", project, "bin", new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name, "net10.0", project + ".dll");
    internal async Task Start(FakeScenario scenario = FakeScenario.Success, bool mutation = false, int delay = 0, bool fakeEnabled = true, int rate = 2048, string? workerDllOverride = null, string? resilienceMode = null)
    {
        _ = Files.Location;
        string method = mutation ? "sync.apply" : "project.inspect";
        using var report = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "response." + method + ".json")));
        var config = new HostConfiguration("1.0", Files.Root, Files.Repository, ["11111111-1111-4111-8111-111111111111"], fakeEnabled ? workerDllOverride ?? Binary("TiaBridge.FakeWorker") : null,
            fakeEnabled ? [new FakeApiProfile(method, scenario, delay, !mutation, report.RootElement.Clone())] : [], mutation, RequestsPerSecond: rate);
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(config, HostConfiguration.Json), new System.Text.UTF8Encoding(false));
        var info = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in resilienceMode is null ? new[] { Binary("TiaBridge.Host"), "--config", ConfigPath, "--stdio" } : new[] { ProbeBinary, Files.Root, Files.Repository, "http-resilience", resilienceMode }) info.ArgumentList.Add(argument);
        info.Environment["ASPNETCORE_URLS"] = "http://0.0.0.0:6553"; // explicit loopback binding must ignore environment URLs.
        info.Environment["ASPNETCORE_ENVIRONMENT"] = "Development"; // operator environment must not enable developer error pages.
        host = Process.Start(info)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        string? ready = await host.StandardOutput.ReadLineAsync(timeout.Token);
        if (ready != "{\"status\":\"READY\"}")
        {
            await host.WaitForExitAsync(timeout.Token);
            throw new InvalidOperationException("Owned test Host failed to start: " + ready + "; " + await host.StandardError.ReadToEndAsync(timeout.Token));
        }
        stdout = host.StandardOutput.ReadToEndAsync(); stderr = host.StandardError.ReadToEndAsync(); Discovery = DiscoveryFile.Read(DiscoveryPath);
    }
    internal BridgeClient Client() => new(DiscoveryPath);
    internal HttpClient Raw(bool authenticated = true)
    {
        var client = new HttpClient(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false, MaxConnectionsPerServer = 8 }) { BaseAddress = new Uri(Discovery.BaseUrl), Timeout = TimeSpan.FromSeconds(10) };
        if (authenticated) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Discovery.Token);
        return client;
    }
    internal static byte[] Request(string method = "project.inspect", string? key = null, int? timeout = null)
    {
        var node = JsonNode.Parse(StoreFixture.Wire(method))!;
        node["idempotency_key"] = key ?? Guid.NewGuid().ToString(); if (timeout is not null) node["params"]!["timeout_s"] = timeout;
        return JsonSerializer.SerializeToUtf8Bytes(node);
    }
    internal async Task<OperationId> Submit(BridgeClient client, byte[]? request = null)
    {
        var response = await client.SendAsync(HttpMethod.Post, "/v1/operations", request ?? Request());
        Assert.Equal(202, response.StatusCode); return ContractJson.ReadAcceptance(response.Json).OperationId;
    }
    internal async Task<OperationReport> Until(BridgeClient client, OperationId id, Func<OperationReport, bool> predicate, int milliseconds = 15000)
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.ElapsedMilliseconds <= milliseconds)
        {
            var response = await client.SendAsync(HttpMethod.Get, "/v1/operations/" + id); Assert.Equal(200, response.StatusCode);
            var report = ContractJson.ReadReport(response.Json); if (predicate(report)) return report; await Task.Delay(20);
        }
        throw new TimeoutException("HTTP operation predicate did not become true.");
    }
    internal async Task<(int Code, string Json)> Cli(params string[] arguments)
    {
        var info = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add(Binary("TiaBridge.Cli")); foreach (string argument in arguments.Concat(["--discovery", DiscoveryPath, "--json"])) info.ArgumentList.Add(argument);
        using var process = Process.Start(info)!; var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try { await process.WaitForExitAsync(deadline.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); throw new TimeoutException("Own test CLI exceeded deadline."); }
        string json = await output; Assert.True(string.IsNullOrWhiteSpace(await error), "CLI must use redacted JSON, not stderr diagnostics.");
        return (process.ExitCode, json);
    }
    internal string WriteRequest(byte[] request)
    { string path = Path.Combine(Files.Root, "request-" + Guid.NewGuid().ToString("N") + ".json"); File.WriteAllBytes(path, request); return path; }
    internal async Task Stop()
    {
        if (host is null) return;
        if (!host.HasExited) { await host.StandardInput.WriteLineAsync("stop"); await host.StandardInput.FlushAsync(); }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try { await host.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { throw new TimeoutException("Owned Host shutdown was not confirmed."); }
        try { Assert.Equal(0, host.ExitCode); if (stdout is not null) _ = await stdout; if (stderr is not null) Assert.True(string.IsNullOrWhiteSpace(await stderr)); }
        finally { host.Dispose(); host = null; }
    }
    internal async Task Crash(bool externally = false)
    {
        if (host is null) throw new InvalidOperationException("No owned process.");
        if (externally && !host.HasExited) host.Kill(entireProcessTree: false); // Exact Process returned by this fixture, no PID/name search.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15)); await host.WaitForExitAsync(timeout.Token);
        try { if (!externally) Assert.Equal(86, host.ExitCode); if (stdout is not null) _ = await stdout; if (stderr is not null) Assert.True(string.IsNullOrWhiteSpace(await stderr)); }
        finally { host.Dispose(); host = null; }
    }
    public async ValueTask DisposeAsync()
    {
        try { await Stop(); }
        finally { if (host is null || host.HasExited) Files.Dispose(); }
    }
}
