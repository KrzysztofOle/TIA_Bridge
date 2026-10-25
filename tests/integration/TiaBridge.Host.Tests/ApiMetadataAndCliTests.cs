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
public class ApiMetadataAndCliTests
{
    [Fact]
    public async Task Worker_start_failure_becomes_a_durable_error_while_api_stays_available()
    {
        await using var f = new ApiFixture(); _ = f.Files.Location;
        string folder = Path.Combine(f.Files.Root, "invalid-worker"); Directory.CreateDirectory(folder);
        string invalid = Path.Combine(folder, "TiaBridge.FakeWorker.dll"); File.WriteAllText(invalid, "synthetic invalid assembly, not executable code");
        await f.Start(workerDllOverride: invalid); using var client = f.Client(); var id = await f.Submit(client);
        var failed = await f.Until(client, id, r => r.State == OperationState.FAILED, milliseconds: 20000);
        Assert.Equal(ErrorCode.WORKER_UNAVAILABLE, failed.Error!.Code); Assert.Equal(OperationOutcome.NOT_STARTED, failed.Outcome);
        Assert.Equal(0, f.Files.Open().AttemptCount(id)); Assert.False(f.Files.Open().OwnerBlocked);
        Assert.Equal(200, (await client.SendAsync(HttpMethod.Get, "/v1/diagnostics/health")).StatusCode);
    }
    [Fact]
    public void Committed_fake_configuration_validates_with_only_explicit_test_paths_substituted()
    {
        using var files = new StoreFixture(); _ = files.Location;
        var node = JsonNode.Parse(File.ReadAllText(Path.Combine(files.Repository, "config", "examples", "host.fake.json")))!;
        node["state_path"] = files.Root; node["repository_root"] = files.Repository; node["fake_worker_dll"] = SchedulerFixture.WorkerPath(files);
        string path = Path.Combine(files.Root, "synthetic-host-example.json"); File.WriteAllText(path, node.ToJsonString());
        var config = TiaBridge.Host.Api.HostConfiguration.Read(path);
        Assert.False(config.EnableSyntheticMutations); Assert.Single(config.FakeProfiles!); Assert.Equal("project.inspect", config.FakeProfiles![0].Method);
        Assert.Equal(0, files.Open().ActiveCount); Assert.False(File.Exists(Path.Combine(files.Root, "api", "host.json")));
    }
    [Fact]
    public async Task All_nineteen_valid_requests_have_an_explicit_http_outcome()
    {
        await using var f = new ApiFixture(); await f.Start(FakeScenario.Hang); using var client = f.Client(); var target = await f.Submit(client);
        foreach (var method in Enum.GetValues<OperationMethod>())
        {
            string name = OperationCatalog.Name(method); var node = JsonNode.Parse(ApiFixture.Request(name))!;
            if (name.StartsWith("operation.", StringComparison.Ordinal)) node["params"]!["operation_id"] = target.ToString();
            byte[] body = JsonSerializer.SerializeToUtf8Bytes(node);
            var response = await client.SendAsync(HttpMethod.Post, "/v1/operations", body);
            if (method == OperationMethod.ProjectInspect) Assert.Equal(202, response.StatusCode);
            else if (TiaBridge.Host.Operations.SqliteOperationStore.IsMetadataMethod(method)) Assert.Equal(200, response.StatusCode);
            else { Assert.Equal(422, response.StatusCode); Assert.Equal(ErrorCode.UNSUPPORTED, ContractJson.ReadError(response.Json).Code); }
        }
    }
    [Fact]
    public async Task Metadata_post_uses_the_same_durable_idempotency_and_cancel_journal()
    {
        await using var f = new ApiFixture(); await f.Start(FakeScenario.Hang); using var client = f.Client(); var target = await f.Submit(client); var pending = await f.Submit(client);
        var node = JsonNode.Parse(ApiFixture.Request("operation.cancel", "synthetic-cancel-key"))!; node["params"]!["operation_id"] = pending.ToString();
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(node);
        var first = await client.SendAsync(HttpMethod.Post, "/v1/operations", bytes); var report = ContractJson.ReadReport(first.Json); Assert.Equal(200, first.StatusCode);
        Assert.Equal(OperationCancelResult_Disposition.CANCELLED, Assert.IsType<OperationCancelResult>(report.Result).Disposition);
        var duplicate = await client.SendAsync(HttpMethod.Post, "/v1/operations", bytes); Assert.Equal(first.Json, duplicate.Json);
        node["params"]!["reason"] = "different canonical body";
        Assert.Equal(409, (await client.SendAsync(HttpMethod.Post, "/v1/operations", JsonSerializer.SerializeToUtf8Bytes(node))).StatusCode);
        Assert.Equal(0, f.Files.Open().AttemptCount(pending));
        var store = f.Files.Open(); Assert.Equal(OperationState.SUCCEEDED, store.Read(report.OperationId).Report.State); Assert.Equal(4, store.Events(report.OperationId).Count);
    }
    [Fact]
    public async Task Explicit_wait_is_bounded_and_cli_exit_does_not_cancel_the_operation()
    {
        await using var f = new ApiFixture(); await f.Start(FakeScenario.Hang); string request = f.WriteRequest(ApiFixture.Request());
        var bounded = await f.Cli("submit", "project.inspect", "--request", request, "--wait", "--max-wait", "1"); Assert.Equal(5, bounded.Code);
        var report = ContractJson.ReadReport(Encoding.UTF8.GetBytes(bounded.Json)); Assert.False(report.CancelRequested);
        using var client = f.Client(); var status = ContractJson.ReadReport((await client.SendAsync(HttpMethod.Get, "/v1/operations/" + report.OperationId)).Json);
        Assert.Equal(OperationState.RUNNING, status.State); Assert.False(status.CancelRequested);
        var cancel = await f.Cli("operation", "cancel", report.OperationId.ToString()); Assert.Equal(0, cancel.Code);
    }
    [Fact]
    public async Task Explicit_wait_can_finish_successfully_without_a_second_submit()
    {
        await using var f = new ApiFixture(); await f.Start(FakeScenario.DelayedSuccess, delay: 50);
        var cli = await f.Cli("submit", "project.inspect", "--request", f.WriteRequest(ApiFixture.Request()), "--wait", "--max-wait", "8");
        Assert.Equal(0, cli.Code); Assert.Equal(OperationState.SUCCEEDED, ContractJson.ReadReport(Encoding.UTF8.GetBytes(cli.Json)).State);
        Assert.Equal(1, f.Files.Count("operations"));
    }
    [Fact]
    public async Task Failed_result_and_wait_use_exit_six_without_claiming_engineering_success()
    {
        await using var f = new ApiFixture(); await f.Start(FakeScenario.FailBefore);
        var cli = await f.Cli("submit", "project.inspect", "--request", f.WriteRequest(ApiFixture.Request()), "--wait", "--max-wait", "8"); Assert.Equal(6, cli.Code);
        var report = ContractJson.ReadReport(Encoding.UTF8.GetBytes(cli.Json));
        Assert.Equal(OperationState.FAILED, report.State); Assert.Equal(6, (await f.Cli("operation", "result", report.OperationId.ToString())).Code);
        Assert.Equal(0, (await f.Cli("operation", "status", report.OperationId.ToString())).Code);
    }
    [Fact]
    public async Task Unknown_mutation_stays_quarantined_after_api_cancel_and_host_restart()
    {
        await using var f = new ApiFixture(); await f.Start(FakeScenario.Hang, mutation: true); using var client = f.Client();
        var id = await f.Submit(client, ApiFixture.Request("sync.apply", timeout: 1));
        await f.Until(client, id, r => r.State == OperationState.RECONCILIATION_REQUIRED);
        var cancel = await f.Cli("operation", "cancel", id.ToString()); Assert.Equal(0, cancel.Code);
        Assert.Equal(OperationCancelResult_State.RECONCILIATION_REQUIRED, Assert.IsType<OperationCancelResult>(ContractJson.ReadReport(Encoding.UTF8.GetBytes(cancel.Json)).Result).State);
        await f.Stop(); await f.Start(FakeScenario.Hang, mutation: true); using var fresh = f.Client();
        var next = await f.Submit(fresh, ApiFixture.Request("sync.apply")); await Task.Delay(100);
        Assert.Equal(0, f.Files.Open().AttemptCount(next)); Assert.True(f.Files.Open().OwnerBlocked);
        var health = ContractJson.ReadReport((await fresh.SendAsync(HttpMethod.Get, "/v1/diagnostics/health")).Json);
        Assert.Contains(Assert.IsType<DiagnosticsHealthResult>(health.Result).Components, c => c.Component == "quarantine" && c.Status == ComponentHealth_Status.DEGRADED);
    }
}
