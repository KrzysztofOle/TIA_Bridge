using System.Net;
using System.Net.Http.Headers;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TiaBridge.Core.Identity;
using TiaBridge.Core.Operations;
using TiaBridge.Core.Transport.V1;
using TiaBridge.Ipc;
using Xunit;

namespace TiaBridge.Host.Tests;

[SupportedOSPlatform("windows")]
[Collection("HTTP API process")]
public class HttpApiTests
{
    [Fact]
    public async Task Real_host_cli_and_fake_complete_the_submit_status_events_result_cycle()
    {
        await using var f = new ApiFixture(); await f.Start(FakeScenario.DelayedSuccess, delay: 150);
        string request = f.WriteRequest(ApiFixture.Request());
        var submit = await f.Cli("submit", "project.inspect", "--request", request); Assert.Equal(0, submit.Code);
        var acceptance = ContractJson.ReadAcceptance(Encoding.UTF8.GetBytes(submit.Json));
        var status = await f.Cli("operation", "status", acceptance.OperationId.ToString()); Assert.Equal(0, status.Code);
        using var client = f.Client(); await f.Until(client, acceptance.OperationId, r => r.State == OperationState.SUCCEEDED);
        var events = await f.Cli("operation", "events", acceptance.OperationId.ToString()); Assert.Equal(0, events.Code);
        var eventReport = ContractJson.ReadReport(Encoding.UTF8.GetBytes(events.Json)); var list = Assert.IsType<OperationEventsResult>(eventReport.Result).Events;
        Assert.Equal(Enumerable.Range(1, list.Length).Select(n => (long)n), list.Select(e => e.Sequence));
        var result = await f.Cli("operation", "result", acceptance.OperationId.ToString()); Assert.Equal(0, result.Code);
        Assert.Equal(OperationState.SUCCEEDED, ContractJson.ReadReport(Encoding.UTF8.GetBytes(result.Json)).State);
        Assert.Equal(0, (await f.Cli("health")).Code);
    }
    [Fact]
    public async Task Duplicate_submit_returns_the_same_id_and_conflicting_body_is_409()
    {
        await using var f = new ApiFixture(); await f.Start(FakeScenario.Hang); using var client = f.Client();
        byte[] request = ApiFixture.Request(key: "synthetic-idempotency"); var id = await f.Submit(client, request);
        var duplicate = await client.SendAsync(HttpMethod.Post, "/v1/operations", request); Assert.Equal(200, duplicate.StatusCode);
        Assert.Equal(id, ContractJson.ReadReport(duplicate.Json).OperationId);
        var node = JsonNode.Parse(request)!; node["params"]!["fresh"] = false;
        var conflict = await client.SendAsync(HttpMethod.Post, "/v1/operations", JsonSerializer.SerializeToUtf8Bytes(node));
        Assert.Equal(409, conflict.StatusCode); Assert.Equal(ErrorCode.IDEMPOTENCY_MISMATCH, ContractJson.ReadError(conflict.Json).Code);
    }
    [Fact]
    public async Task Result_202_events_cursor_and_metadata_do_not_wait_for_hung_worker()
    {
        await using var f = new ApiFixture(); await f.Start(FakeScenario.Hang); using var client = f.Client(); var id = await f.Submit(client);
        await f.Until(client, id, r => r.Phase == "EXECUTING");
        var result = await client.SendAsync(HttpMethod.Get, "/v1/operations/" + id + "/result"); Assert.Equal(202, result.StatusCode);
        Assert.Equal(OperationState.RUNNING, ContractJson.ReadReport(result.Json).State);
        var events = await client.SendAsync(HttpMethod.Get, "/v1/operations/" + id + "/events?after_sequence=1&limit=1");
        var projection = Assert.IsType<OperationEventsResult>(ContractJson.ReadReport(events.Json).Result); Assert.Single(projection.Events); Assert.Equal(2, projection.NextSequence);
        using var raw = f.Raw(); var status = await raw.GetAsync("/v1/operations/" + id); Assert.NotNull(status.Content.Headers.LastModified);
        Assert.Equal("no-store", status.Headers.CacheControl!.ToString());
    }
    [Fact]
    public async Task Queued_cancel_never_dispatches_and_running_cancel_waits_for_confirmation()
    {
        await using var f = new ApiFixture(); await f.Start(FakeScenario.Cancellable); using var client = f.Client(); var running = await f.Submit(client);
        await f.Until(client, running, r => r.Phase == "EXECUTING"); var queued = await f.Submit(client);
        var cancel = await client.SendAsync(HttpMethod.Post, "/v1/operations/" + queued + "/cancel");
        Assert.Equal(OperationCancelResult_Disposition.CANCELLED, Assert.IsType<OperationCancelResult>(ContractJson.ReadReport(cancel.Json).Result).Disposition);
        var replay = await client.SendAsync(HttpMethod.Post, "/v1/operations/" + queued + "/cancel");
        Assert.Equal(OperationCancelResult_Disposition.ALREADY_TERMINAL, Assert.IsType<OperationCancelResult>(ContractJson.ReadReport(replay.Json).Result).Disposition);
        Assert.Equal(0, f.Files.Open().AttemptCount(queued));
        var request = await f.Cli("operation", "cancel", running.ToString(), "--reason", "synthetic cancellation"); Assert.Equal(0, request.Code);
        Assert.Equal(OperationCancelResult_Disposition.CANCEL_REQUESTED, Assert.IsType<OperationCancelResult>(ContractJson.ReadReport(Encoding.UTF8.GetBytes(request.Json)).Result).Disposition);
        await f.Until(client, running, r => r.State == OperationState.CANCELLED);
    }
    [Fact]
    public async Task Host_restart_keeps_results_identity_and_history_and_rotates_transport_token()
    {
        await using var f = new ApiFixture(); await f.Start(); using var client = f.Client(); byte[] request = ApiFixture.Request(key: "synthetic-restart"); var id = await f.Submit(client, request);
        await f.Until(client, id, r => r.State == OperationState.SUCCEEDED); var events = await client.SendAsync(HttpMethod.Get, "/v1/operations/" + id + "/events");
        string token = f.Discovery.Token; await f.Stop(); await f.Start(fakeEnabled: false); using var next = f.Client();
        Assert.True(token != f.Discovery.Token, "Transport token must rotate after restart.");
        var duplicate = await next.SendAsync(HttpMethod.Post, "/v1/operations", request); Assert.Equal(200, duplicate.StatusCode); Assert.Equal(id, ContractJson.ReadReport(duplicate.Json).OperationId);
        Assert.Equal(OperationState.SUCCEEDED, ContractJson.ReadReport((await next.SendAsync(HttpMethod.Get, "/v1/operations/" + id + "/result")).Json).State);
        Assert.Equal(events.Json, (await next.SendAsync(HttpMethod.Get, "/v1/operations/" + id + "/events")).Json);
        using var stale = f.Raw(false); stale.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token); Assert.Equal(HttpStatusCode.Unauthorized, (await stale.GetAsync("/v1/diagnostics/health")).StatusCode);
    }
    [Theory]
    [InlineData("compile.plc")]
    [InlineData("compile.hmi")]
    [InlineData("simulation.start")]
    [InlineData("project.export")]
    public async Task Unimplemented_operations_are_denied_without_false_acceptance(string method)
    {
        await using var f = new ApiFixture(); await f.Start(fakeEnabled: false); using var client = f.Client();
        var response = await client.SendAsync(HttpMethod.Post, "/v1/operations", ApiFixture.Request(method));
        Assert.Equal(422, response.StatusCode); Assert.Equal(ErrorCode.UNSUPPORTED, ContractJson.ReadError(response.Json).Code); Assert.Equal(0, f.Files.Count("operations"));
    }
    [Theory]
    [InlineData("{")]
    [InlineData("{}")]
    [InlineData("{\"schema_version\":\"2.0\"}")]
    public async Task Invalid_json_or_version_has_a_typed_error_and_no_acceptance(string json)
    {
        await using var f = new ApiFixture(); await f.Start(); using var client = f.Client();
        var response = await client.SendAsync(HttpMethod.Post, "/v1/operations", Encoding.UTF8.GetBytes(json));
        Assert.Contains(response.StatusCode, new[] { 400, 422 }); Assert.NotNull(ContractJson.ReadError(response.Json)); Assert.Equal(0, f.Files.Count("operations"));
    }
    [Fact]
    public async Task Unknown_routes_and_bad_cursor_cancel_body_are_rejected()
    {
        await using var f = new ApiFixture(); await f.Start(FakeScenario.Hang); using var client = f.Client(); var id = await f.Submit(client);
        foreach (string path in new[] { "/v1/operations/" + id + "?token=forbidden", "/v1/operations/" + id + "/events?limit=0", "/v1/operations/" + id + "/events?after_sequence=-1" })
            Assert.Equal(400, (await client.SendAsync(HttpMethod.Get, path)).StatusCode);
        Assert.Equal(400, (await client.SendAsync(HttpMethod.Post, "/v1/operations/" + id + "/cancel", "{\"command\":\"forbidden\"}"u8.ToArray())).StatusCode);
        Assert.Equal(404, (await client.SendAsync(HttpMethod.Get, "/v1/execute-shell")).StatusCode);
    }
}
