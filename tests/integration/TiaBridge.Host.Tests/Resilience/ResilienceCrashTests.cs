using System.Text;
using System.Text.Json;
using TiaBridge.Client;
using TiaBridge.Core.Identity;
using TiaBridge.Core.Operations;
using TiaBridge.Core.Transport.V1;
using TiaBridge.Ipc;
using Xunit;

namespace TiaBridge.Host.Tests;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
[Collection("HTTP API process")]
public class ResilienceCrashTests
{
    [Fact]
    public async Task RT03_explicit_QUEUED_state_survives_restart_and_executes_once_after_old_read_exits()
    {
        using var evidence = new ResilienceEvidence("queued-restart", "RT-03", "NF-12");
        await using var f = new ApiFixture(); await f.Start(FakeScenario.Hang, resilienceMode: "observe"); using var client = f.Client();
        var first = await f.Submit(client, ApiFixture.Request(timeout: 120)); await f.Until(client, first, r => r.Phase == "EXECUTING");
        byte[] request = ApiFixture.Request(); var queued = await f.Submit(client, request); var store = f.Files.Open(); var before = store.Read(queued);
        store.Record(queued, before.Revision, before.Report with { State = OperationState.QUEUED, Phase = "QUEUED" });
        Assert.Equal(0, store.AttemptCount(queued)); await ResilienceEvidence.IndependentWork(f); await f.Crash(externally: true);
        await ResilienceEvidence.WorkerAbsent(await ResilienceEvidence.Marker(f, "worker.json")); await f.Start(); using var fresh = f.Client();
        await f.Until(fresh, queued, r => r.State == OperationState.SUCCEEDED); Assert.Equal(1, store.AttemptCount(queued)); Assert.Equal(1, ResilienceEvidence.DurableEffects(f, queued));
        Assert.Equal(OperationState.FAILED, store.Read(first).Report.State);
        var duplicate = await fresh.SendAsync(HttpMethod.Post, "/v1/operations", request); Assert.Equal(200, duplicate.StatusCode); Assert.Equal(queued, ContractJson.ReadReport(duplicate.Json).OperationId);
        evidence.Data["operation"] = ResilienceEvidence.Journal(f, queued); evidence.Pass();
    }
    [Theory]
    [InlineData("OperationInserted", false)]
    [InlineData("EventInserted", false)]
    [InlineData("OutboxInserted", false)]
    [InlineData("BeforeCommit", false)]
    [InlineData("AfterCommit", true)]
    public async Task RT03_admission_crash_has_no_false_acceptance_and_durable_idempotency(string point, bool committed)
    {
        using var evidence = new ResilienceEvidence("admission-" + point, "RT-03", "RT-16", "NF-12");
        await using var f = new ApiFixture(); await f.Start(mutation: true, resilienceMode: "crash." + point);
        byte[] request = ApiFixture.Request("sync.apply"); string path = f.WriteRequest(request);
        var cli = await f.Cli("submit", "sync.apply", "--request", path); Assert.Equal(4, cli.Code);
        await f.Crash(); Assert.True(File.Exists(Path.Combine(f.Files.Root, point + ".reached")));
        foreach (string table in new[] { "operations", "events", "outbox", "idempotency_keys" }) Assert.Equal(committed ? 1 : 0, f.Files.Count(table));
        OperationId? previous = null;
        using (var c = f.Files.Connection()) { using var command = c.CreateCommand(); command.CommandText = "SELECT operation_id FROM operations"; if (command.ExecuteScalar() is string id) previous = new(Guid.Parse(id)); }
        await f.Start(mutation: true); using var client = f.Client(); var retry = await client.SendAsync(HttpMethod.Post, "/v1/operations", request);
        Assert.Equal(committed ? 200 : 202, retry.StatusCode);
        var operation = retry.StatusCode == 202 ? ContractJson.ReadAcceptance(retry.Json).OperationId : ContractJson.ReadReport(retry.Json).OperationId;
        if (previous is not null) Assert.Equal(previous, operation);
        await f.Until(client, operation, r => r.State == OperationState.SUCCEEDED);
        Assert.Equal(1, ResilienceEvidence.DurableEffects(f, operation)); Assert.Equal(1, f.Files.Open().AttemptCount(operation));
        Assert.Equal(0, (await f.Cli("operation", "status", operation.ToString())).Code);
        await ResilienceEvidence.IndependentWork(f); evidence.Data["recovered"] = ResilienceEvidence.Journal(f, operation); evidence.Data["effects"] = 1; evidence.Pass();
    }
    [Theory]
    [InlineData("dispatch-intent", false)]
    [InlineData("dispatch-sent", false)]
    [InlineData("receipt-received", false)]
    [InlineData("WorkerReceiptCommitted", false)]
    [InlineData("result-before", false)]
    [InlineData("result-after", true)]
    public async Task RT03_running_crash_never_replays_unknown_and_terminal_commit_is_atomic(string point, bool succeeded)
    {
        using var evidence = new ResilienceEvidence("running-" + point, "RT-03", "RT-16", "NF-12");
        await using var f = new ApiFixture(); await f.Start(mutation: true, resilienceMode: "crash." + point);
        using var client = f.Client(); byte[] request = ApiFixture.Request("sync.apply", timeout: 30); var id = await f.Submit(client, request);
        ResilienceEvidence.Release(f, "profile");
        await f.Crash(); var worker = await ResilienceEvidence.Marker(f, "worker.json"); await ResilienceEvidence.WorkerAbsent(worker);
        int effects = ResilienceEvidence.DurableEffects(f, id); Assert.InRange(effects, 0, 1);
        if (point is "receipt-received" or "WorkerReceiptCommitted" or "result-before" or "result-after") Assert.Equal(1, effects);
        var watch = System.Diagnostics.Stopwatch.StartNew(); await f.Start(mutation: true); using var fresh = f.Client();
        var report = ContractJson.ReadReport((await fresh.SendAsync(HttpMethod.Get, "/v1/operations/" + id)).Json);
        Assert.Equal(succeeded ? OperationState.SUCCEEDED : OperationState.RECONCILIATION_REQUIRED, report.State);
        evidence.Data["recovery_ms"] = watch.Elapsed.TotalMilliseconds;
        Assert.Equal(succeeded ? OperationOutcome.CONFIRMED : OperationOutcome.UNKNOWN, report.Outcome);
        var duplicate = await fresh.SendAsync(HttpMethod.Post, "/v1/operations", request); Assert.Equal(200, duplicate.StatusCode); Assert.Equal(id, ContractJson.ReadReport(duplicate.Json).OperationId);
        Assert.Equal(1, f.Files.Open().AttemptCount(id)); Assert.Equal(effects, ResilienceEvidence.DurableEffects(f, id));
        if (!succeeded)
        {
            var blocked = await f.Submit(fresh, ApiFixture.Request("sync.apply")); Assert.Equal(0, f.Files.Open().AttemptCount(blocked)); Assert.True(f.Files.Open().OwnerBlocked);
            await f.Stop(); await f.Start(mutation: true); Assert.True(f.Files.Open().OwnerBlocked); Assert.Equal(OperationOutcome.UNKNOWN, f.Files.Open().Read(id).Report.Outcome);
        }
        await ResilienceEvidence.IndependentWork(f); evidence.Data["worker_before_crash"] = worker; evidence.Data["operation"] = ResilienceEvidence.Journal(f, id); evidence.Data["effects"] = effects; evidence.Pass();
    }
    [Theory]
    [InlineData("EventInserted")]
    [InlineData("OutboxInserted")]
    [InlineData("BeforeCommit")]
    public async Task Storage_faults_over_http_cannot_partially_accept_or_claim_disk_full_success(string point)
    {
        using var evidence = new ResilienceEvidence("storage-fault-" + point, "NF-12");
        await using var f = new ApiFixture(); await f.Start(resilienceMode: "fail." + point); using var client = f.Client(); byte[] request = ApiFixture.Request();
        var failure = await client.SendAsync(HttpMethod.Post, "/v1/operations", request); Assert.Equal(503, failure.StatusCode);
        Assert.Equal(point == "BeforeCommit" ? ErrorCode.STORAGE_FULL : ErrorCode.STORE_UNAVAILABLE, ContractJson.ReadError(failure.Json).Code);
        foreach (string table in new[] { "operations", "events", "outbox", "idempotency_keys" }) Assert.Equal(0, f.Files.Count(table));
        var id = await f.Submit(client, request); await f.Until(client, id, r => r.State == OperationState.SUCCEEDED);
        evidence.Data["operation"] = ResilienceEvidence.Journal(f, id); evidence.Pass();
    }
    [Fact]
    public async Task RT16_one_hundred_identical_http_submits_and_lost_cli_response_have_one_effect()
    {
        using var evidence = new ResilienceEvidence("idempotency-100", "RT-16", "NF-12");
        await using var f = new ApiFixture(); await f.Start(FakeScenario.DuplicateDispatch, mutation: true); using var client = f.Client(); byte[] request = ApiFixture.Request("sync.apply");
        using var slots = new SemaphoreSlim(8);
        var ids = await Task.WhenAll(Enumerable.Range(0, 100).Select(async _ =>
        {
            await slots.WaitAsync(); try { var r = await client.SendAsync(HttpMethod.Post, "/v1/operations", request); Assert.Contains(r.StatusCode, new[] { 200, 202 }); return r.StatusCode == 202 ? ContractJson.ReadAcceptance(r.Json).OperationId : ContractJson.ReadReport(r.Json).OperationId; } finally { slots.Release(); }
        }));
        var id = Assert.Single(ids.Distinct()); await f.Until(client, id, r => r.State == OperationState.SUCCEEDED);
        Assert.Equal(1, ResilienceEvidence.DurableEffects(f, id)); Assert.Equal(1, f.Files.Open().AttemptCount(id));
        var changed = System.Text.Json.Nodes.JsonNode.Parse(request)!; changed["params"]!["timeout_s"] = 1;
        Assert.Equal(409, (await client.SendAsync(HttpMethod.Post, "/v1/operations", JsonSerializer.SerializeToUtf8Bytes(changed))).StatusCode);
        await f.Crash(externally: true); await f.Start(FakeScenario.DuplicateDispatch, mutation: true);
        var cli = await f.Cli("submit", "sync.apply", "--request", f.WriteRequest(request)); Assert.Equal(0, cli.Code);
        Assert.Equal(id, ContractJson.ReadReport(Encoding.UTF8.GetBytes(cli.Json)).OperationId); Assert.Equal(1, ResilienceEvidence.DurableEffects(f, id));
        evidence.Data["identical_requests"] = 100; evidence.Data["operation"] = ResilienceEvidence.Journal(f, id); evidence.Data["effect_count"] = 1; evidence.Pass();
    }
}
