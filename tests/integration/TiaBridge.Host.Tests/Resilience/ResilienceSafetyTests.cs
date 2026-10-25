using System.Diagnostics;
using System.Text.Json;
using TiaBridge.Core.Operations;
using TiaBridge.Core.Transport.V1;
using TiaBridge.Ipc;
using Xunit;

namespace TiaBridge.Host.Tests;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
[Collection("HTTP API process")]
public class ResilienceSafetyTests
{
    [Fact]
    public async Task RT05_two_synthetic_writes_are_serial_and_changed_P_B_rejects_the_second_without_effect()
    {
        using var evidence = new ResilienceEvidence("two-writes-stale-P-B", "RT-05");
        await using var f = new ApiFixture(); await f.Start(mutation: true, resilienceMode: "stale"); using var client = f.Client();
        var first = await f.Submit(client, ApiFixture.Request("sync.apply")); await ResilienceEvidence.Marker(f, "dispatch-sent.reached");
        var second = await f.Submit(client, ApiFixture.Request("sync.apply")); Assert.Equal(0, f.Files.Open().AttemptCount(second));
        Assert.Equal(1, f.Files.Open().AttemptCount(first)); await ResilienceEvidence.IndependentWork(f);
        Assert.Equal(200, (await client.SendAsync(HttpMethod.Get, "/v1/diagnostics/health")).StatusCode);
        ResilienceEvidence.Release(f, "dispatch-sent"); await f.Until(client, first, r => r.State == OperationState.SUCCEEDED);
        var rejected = await f.Until(client, second, r => r.State == OperationState.FAILED); Assert.Equal(ErrorCode.STALE_PLAN, rejected.Error!.Code);
        Assert.Equal(1, ResilienceEvidence.DurableEffects(f, first)); Assert.Equal(0, ResilienceEvidence.DurableEffects(f, second));
        using (var c = f.Files.Connection())
        {
            using var command = c.CreateCommand(); command.CommandText = "SELECT count(*) FROM worker_receipts WHERE operation_id=$id AND disposition='REJECTED_NO_EFFECT'"; command.Parameters.AddWithValue("$id", second.ToString()); Assert.Equal(1L, command.ExecuteScalar());
        }
        evidence.Data["synthetic_preconditions"] = await ResilienceEvidence.Marker(f, "synthetic-preconditions.json");
        evidence.Data["first"] = ResilienceEvidence.Journal(f, first); evidence.Data["second"] = ResilienceEvidence.Journal(f, second); evidence.Pass();
    }
    [Fact]
    public async Task RT06_receipt_already_in_transit_after_deadline_is_evidence_not_success_even_after_restart()
    {
        using var evidence = new ResilienceEvidence("deadline-late-receipt", "RT-06", "RT-03");
        await using var f = new ApiFixture(); await f.Start(mutation: true, resilienceMode: "barrier.receipt-received"); using var client = f.Client();
        var id = await f.Submit(client, ApiFixture.Request("sync.apply", timeout: 1)); await ResilienceEvidence.Marker(f, "receipt-received.reached");
        var before = ContractJson.ReadReport((await client.SendAsync(HttpMethod.Get, "/v1/operations/" + id)).Json);
        Assert.Equal(OperationState.RUNNING, before.State); Assert.Equal(1, ResilienceEvidence.DurableEffects(f, id));
        while (DateTimeOffset.UtcNow <= before.DeadlineAt) await Task.Delay(10);
        await ResilienceEvidence.IndependentWork(f); ResilienceEvidence.Release(f, "receipt-received");
        var after = await f.Until(client, id, r => r.State == OperationState.RECONCILIATION_REQUIRED); Assert.Equal(ErrorCode.TIMEOUT, after.Error!.Code);
        Assert.Equal(1, f.Files.Open().ReceiptCount(id)); Assert.True(f.Files.Open().OwnerBlocked);
        await f.Stop(); await f.Start(mutation: true); Assert.Equal(OperationOutcome.UNKNOWN, f.Files.Open().Read(id).Report.Outcome);
        using var fresh = f.Client(); var next = await f.Submit(fresh, ApiFixture.Request("sync.apply")); Assert.Equal(0, f.Files.Open().AttemptCount(next));
        evidence.Data["operation"] = ResilienceEvidence.Journal(f, id); evidence.Data["effects"] = ResilienceEvidence.DurableEffects(f, id); evidence.Pass();
    }
    [Fact]
    public async Task Old_epoch_cannot_promote_a_real_receipt_and_quarantine_survives_cancel()
    {
        using var evidence = new ResilienceEvidence("old-epoch-receipt", "RT-06");
        await using var f = new ApiFixture(); await f.Start(mutation: true, resilienceMode: "old-epoch"); using var client = f.Client();
        var id = await f.Submit(client, ApiFixture.Request("sync.apply", timeout: 1)); await f.Until(client, id, r => r.State == OperationState.RECONCILIATION_REQUIRED);
        Assert.Equal(1, ResilienceEvidence.DurableEffects(f, id)); Assert.Equal(1, f.Files.Open().AttemptCount(id));
        using (var c = f.Files.Connection())
        { using var command = c.CreateCommand(); command.CommandText = "SELECT disposition FROM worker_receipts WHERE operation_id=$id"; command.Parameters.AddWithValue("$id", id.ToString()); Assert.Equal("FENCED_REJECTED", command.ExecuteScalar()); }
        Assert.Equal(0, (await f.Cli("operation", "cancel", id.ToString())).Code); Assert.True(f.Files.Open().OwnerBlocked);
        evidence.Data["operation"] = ResilienceEvidence.Journal(f, id); evidence.Pass();
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancel_dispatch_race_has_exactly_one_safe_ordering(bool dispatchWins)
    {
        using var evidence = new ResilienceEvidence("cancel-race-" + dispatchWins, "RT-05");
        string barrier = dispatchWins ? "dispatch-sent" : "before-dispatch";
        await using var f = new ApiFixture(); await f.Start(FakeScenario.Cancellable, mutation: true, resilienceMode: "barrier." + barrier); using var client = f.Client();
        var id = await f.Submit(client, ApiFixture.Request("sync.apply")); await ResilienceEvidence.Marker(f, barrier + ".reached");
        var cancel = await client.SendAsync(HttpMethod.Post, "/v1/operations/" + id + "/cancel");
        var disposition = Assert.IsType<OperationCancelResult>(ContractJson.ReadReport(cancel.Json).Result);
        Assert.Equal(dispatchWins ? OperationCancelResult_Disposition.CANCEL_REQUESTED : OperationCancelResult_Disposition.CANCELLED, disposition.Disposition);
        ResilienceEvidence.Release(f, barrier); await f.Until(client, id, r => r.State == OperationState.CANCELLED);
        Assert.Equal(dispatchWins ? 1 : 0, f.Files.Open().AttemptCount(id)); Assert.Equal(0, ResilienceEvidence.DurableEffects(f, id));
        await ResilienceEvidence.IndependentWork(f); evidence.Data["operation"] = ResilienceEvidence.Journal(f, id); evidence.Pass();
    }
    [Theory]
    [InlineData(FakeScenario.Disconnect)]
    [InlineData(FakeScenario.Exit)]
    [InlineData(FakeScenario.NoHeartbeat)]
    [InlineData(FakeScenario.HeartbeatWithoutProgress)]
    public async Task Loss_or_live_heartbeat_without_progress_never_proves_mutation_success(FakeScenario scenario)
    {
        using var evidence = new ResilienceEvidence("worker-loss-" + scenario, "RT-01", "RT-06", "NF-04");
        await using var f = new ApiFixture(); await f.Start(scenario, mutation: true, resilienceMode: "observe"); using var client = f.Client();
        var watch = Stopwatch.StartNew(); var id = await f.Submit(client, ApiFixture.Request("sync.apply", timeout: scenario == FakeScenario.HeartbeatWithoutProgress ? 5 : 20));
        if (scenario == FakeScenario.HeartbeatWithoutProgress)
        {
            var running = await f.Until(client, id, r => r.State == OperationState.RUNNING && r.WorkerHeartbeatAgeMs is < 1000 && DateTimeOffset.UtcNow - r.LastProgressAt > TimeSpan.FromSeconds(2));
            evidence.Data["last_progress_at"] = running.LastProgressAt; evidence.Data["worker_heartbeat_age_ms"] = running.WorkerHeartbeatAgeMs;
        }
        var report = await f.Until(client, id, r => r.State == OperationState.RECONCILIATION_REQUIRED);
        evidence.Data["classification_ms"] = watch.Elapsed.TotalMilliseconds;
        if (scenario == FakeScenario.NoHeartbeat) Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10));
        Assert.Equal(OperationOutcome.UNKNOWN, report.Outcome); Assert.Equal(1, f.Files.Open().AttemptCount(id));
        Assert.Equal(200, (await client.SendAsync(HttpMethod.Get, "/v1/diagnostics/health")).StatusCode); await ResilienceEvidence.IndependentWork(f);
        evidence.Data["operation"] = ResilienceEvidence.Journal(f, id); evidence.Pass();
    }
    [Fact]
    public async Task Expired_lease_with_live_call_does_not_admit_second_owner_or_release_mutation_after_restart()
    {
        using var evidence = new ResilienceEvidence("lease-expiry-live-call", "RT-03", "RT-05");
        await using var f = new ApiFixture(); await f.Start(FakeScenario.Hang, mutation: true, resilienceMode: "barrier.dispatch-sent"); using var client = f.Client();
        var id = await f.Submit(client, ApiFixture.Request("sync.apply")); await ResilienceEvidence.Marker(f, "dispatch-sent.reached");
        f.Files.Sql("UPDATE scheduler_ownership SET lease_until='2000-01-01T00:00:00.0000000+00:00'");
        var next = await f.Submit(client, ApiFixture.Request("sync.apply")); ResilienceEvidence.Release(f, "dispatch-sent");
        Assert.Equal(OperationState.RUNNING, ContractJson.ReadReport((await client.SendAsync(HttpMethod.Get, "/v1/operations/" + id)).Json).State); Assert.Equal(0, f.Files.Open().AttemptCount(next));
        Assert.Throws<TiaBridge.Host.Storage.StorageException>(() => new TiaBridge.Host.Scheduling.OperationScheduler(f.Files.Open(), new TiaBridge.Host.Supervision.FakeWorkerSupervisor(f.Binary("TiaBridge.FakeWorker"), f.Files.Root), new FakeProfile()));
        await f.Crash(externally: true); var worker = await ResilienceEvidence.Marker(f, "worker.json"); await ResilienceEvidence.WorkerAbsent(worker);
        await f.Start(mutation: true); Assert.True(f.Files.Open().OwnerBlocked); Assert.Equal(OperationOutcome.UNKNOWN, f.Files.Open().Read(id).Report.Outcome); Assert.Equal(0, f.Files.Open().AttemptCount(next));
        evidence.Data["operation"] = ResilienceEvidence.Journal(f, id); evidence.Data["old_worker"] = worker; evidence.Pass();
    }
    [Fact]
    public async Task RT15_owned_child_lock_is_detected_quarantined_and_only_owned_resources_are_cleaned()
    {
        using var evidence = new ResilienceEvidence("owned-child-lock", "RT-15", "RT-03");
        await using var f = new ApiFixture(); await f.Start(FakeScenario.Hang, mutation: true, resilienceMode: "child"); using var client = f.Client();
        var id = await f.Submit(client, ApiFixture.Request("sync.apply")); await f.Until(client, id, r => r.Phase == "EXECUTING");
        var worker = await ResilienceEvidence.Marker(f, "worker.json"); var child = await ResilienceEvidence.Marker(f, "child.json");
        try
        {
            using (var process = Process.GetProcessById(worker.GetProperty("pid").GetInt32()))
            { Assert.Equal(worker.GetProperty("start_ticks").GetInt64(), process.StartTime.ToUniversalTime().Ticks); process.Kill(entireProcessTree: false); await process.WaitForExitAsync(); }
            await f.Until(client, id, r => r.State == OperationState.RECONCILIATION_REQUIRED);
            Assert.Throws<IOException>(() => { using var file = new FileStream(Path.Combine(f.Files.Root, "resilience-resource.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None); });
            await Assert.ThrowsAnyAsync<Exception>(() => f.Stop()); await ResilienceEvidence.Marker(f, "cleanup-anomaly.json");
            await f.Start(mutation: true); using var fresh = f.Client(); var next = await f.Submit(fresh, ApiFixture.Request("sync.apply")); Assert.Equal(0, f.Files.Open().AttemptCount(next));
            evidence.Data["worker"] = worker; evidence.Data["child"] = child; evidence.Data["operation"] = ResilienceEvidence.Journal(f, id);
        }
        finally { File.WriteAllText(Path.Combine(f.Files.Root, "child.release"), "release"); await ResilienceEvidence.WorkerAbsent(child); }
        using (var file = new FileStream(Path.Combine(f.Files.Root, "resilience-resource.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None)) Assert.True(file.CanRead);
        evidence.Data["cleanup_confirmed"] = true; evidence.Pass();
    }
}
