using System.Net;
using System.Net.Http.Headers;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using TiaBridge.Client;
using TiaBridge.Core.Operations;
using TiaBridge.Core.Transport.V1;
using Xunit;

namespace TiaBridge.Host.Tests;

[CollectionDefinition("HTTP API process", DisableParallelization = true)]
public class ApiProcessCollection { }

[SupportedOSPlatform("windows")]
[Collection("HTTP API process")]
public class ApiSecurityTests
{
    [Fact]
    public void Admission_concurrency_keeps_reserved_capacity_for_metadata_reads()
    {
        var gate = new TiaBridge.Host.Api.RequestGate(2048, 256, 4);
        using var first = gate.Enter(true); using var second = gate.Enter(true);
        Assert.NotNull(first); Assert.NotNull(second); Assert.Null(gate.Enter(true));
        using var read = gate.Enter(false); using var health = gate.Enter(false); Assert.NotNull(read); Assert.NotNull(health); Assert.Null(gate.Enter(false));
    }
    [Theory]
    [InlineData("missing")]
    [InlineData("incorrect")]
    [InlineData("query")]
    public async Task Missing_or_wrong_token_is_denied_without_disclosure(string kind)
    {
        await using var f = new ApiFixture(); await f.Start(); using var client = f.Raw(false);
        if (kind == "incorrect") client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new string('A', 44));
        var response = await client.GetAsync("/v1/diagnostics/health" + (kind == "query" ? "?token=forbidden" : ""));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode); byte[] bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(ErrorCode.UNAUTHENTICATED, ContractJson.ReadError(bytes).Code); Assert.False(Encoding.UTF8.GetString(bytes).Contains(f.Discovery.Token, StringComparison.Ordinal), "Response must not expose a transport credential.");
        Assert.Equal(0, f.Files.Count("operations"));
    }
    [Theory]
    [InlineData("Origin", "https://untrusted.invalid")]
    [InlineData("Origin", "null")]
    [InlineData("Sec-Fetch-Site", "same-origin")]
    [InlineData("Host", "rebinding.invalid")]
    [InlineData("X-Forwarded-Host", "untrusted.invalid")]
    public async Task Browser_rebinding_and_proxy_requests_are_denied(string header, string value)
    {
        await using var f = new ApiFixture(); await f.Start(); using var client = f.Raw();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/diagnostics/health"); request.Headers.TryAddWithoutValidation(header, value);
        var response = await client.SendAsync(request); Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(ErrorCode.ACCESS_DENIED, ContractJson.ReadError(await response.Content.ReadAsByteArrayAsync()).Code);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }
    [Fact]
    public async Task Discovery_is_user_only_and_a_widened_acl_is_rejected_by_cli()
    {
        await using var f = new ApiFixture(); await f.Start();
        var file = new FileInfo(f.DiscoveryPath); DiscoveryFile.VerifyAcl(file.GetAccessControl());
        Assert.Equal("127.0.0.1", new Uri(f.Discovery.BaseUrl).Host);
        var acl = file.GetAccessControl();
        acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null), FileSystemRights.Read, AccessControlType.Allow));
        file.SetAccessControl(acl);
        try
        {
            Assert.Throws<IOException>(() => DiscoveryFile.Read(f.DiscoveryPath));
            var cli = await f.Cli("health"); Assert.Equal(4, cli.Code); Assert.False(cli.Json.Contains(f.Discovery.Token, StringComparison.Ordinal), "CLI must not expose a transport credential.");
        }
        finally
        {
            // Restore only the explicit rule added by this test, never a foreign ACL.
            acl.RemoveAccessRuleSpecific(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null), FileSystemRights.Read, AccessControlType.Allow)); file.SetAccessControl(acl);
        }
    }
    [Fact]
    public async Task Operation_ownership_is_checked_instead_of_trusting_a_json_principal()
    {
        await using var f = new ApiFixture(); await f.Start(); var store = f.Files.Open();
        var foreign = store.Accept("synthetic-different-principal", ApiFixture.Request());
        using var client = f.Client();
        foreach (string suffix in new[] { "", "/result", "/events" })
            Assert.Equal(403, (await client.SendAsync(HttpMethod.Get, "/v1/operations/" + foreign.Operation.Report.OperationId + suffix)).StatusCode);
        Assert.Equal(403, (await client.SendAsync(HttpMethod.Post, "/v1/operations/" + foreign.Operation.Report.OperationId + "/cancel")).StatusCode);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Oversize_bodies_are_rejected_for_declared_and_streamed_lengths(bool chunked)
    {
        await using var f = new ApiFixture(); await f.Start(); using var client = f.Raw();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/operations");
        request.Content = chunked ? new StreamContent(new MemoryStream(new byte[ContractJson.MaxRequestBytes + 1])) : new ByteArrayContent(new byte[ContractJson.MaxRequestBytes + 1]);
        request.Content.Headers.ContentType = new("application/json"); if (chunked) request.Headers.TransferEncodingChunked = true;
        var response = await client.SendAsync(request); Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal(ErrorCode.LIMIT_EXCEEDED, ContractJson.ReadError(await response.Content.ReadAsByteArrayAsync()).Code); Assert.Equal(0, f.Files.Count("operations"));
    }
    [Fact]
    public async Task Request_rate_is_bounded_and_recovers_in_a_new_window()
    {
        await using var f = new ApiFixture(); await f.Start(rate: 8); using var client = f.Raw();
        var codes = new List<HttpStatusCode>();
        for (int i = 0; i < 40; i++) { using var response = await client.GetAsync("/v1/diagnostics/health"); codes.Add(response.StatusCode); }
        Assert.Contains(HttpStatusCode.TooManyRequests, codes); await Task.Delay(1100);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/v1/diagnostics/health")).StatusCode);
    }
    [Fact]
    public async Task Cli_has_no_token_argument_and_discovery_errors_are_redacted()
    {
        await using var f = new ApiFixture(); await f.Start();
        var bad = await f.Cli("health", "--token", "synthetic-forbidden-value"); Assert.Equal(2, bad.Code); Assert.DoesNotContain("synthetic-forbidden-value", bad.Json);
        await f.Stop(); var offline = await f.Cli("health"); Assert.Equal(4, offline.Code);
        Assert.Equal(ErrorCode.WORKER_UNAVAILABLE, ContractJson.ReadError(Encoding.UTF8.GetBytes(offline.Json)).Code);
    }
}
