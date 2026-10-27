using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using TiaBridge.Core.Operations;
using TiaBridge.Core.Transport.V1;
using TiaBridge.Host.Operations;
using TiaBridge.Host.Scheduling;
using TiaBridge.Host.Storage;
using TiaBridge.Host.Supervision;
using TiaBridge.Ipc;
using Xunit;

namespace TiaBridge.Host.Tests;

[Collection("HTTP API process")]
public class SchedulerSafetyTests
{
    [Fact]
    public async Task Restarted_host_classifies_the_old_read_and_dispatches_only_after_old_process_exit()
    {
        using var files = new StoreFixture(); var crash = files.Probe("scheduler-crash-read"); Assert.True(crash.Code == 76, crash.Output);
        var store = files.Open(); var old = files.Accept(store, "project.inspect").Operation.Report.OperationId;
        var node = JsonNode.Parse(StoreFixture.Wire("project.inspect"))!; node["idempotency_key"] = "new-read-after-restart";
        var next = store.Accept("synthetic-principal", JsonSerializer.SerializeToUtf8Bytes(node)).Operation.Report.OperationId;
        var asset = store.Artifacts.Put(next, new MemoryStream(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "response.project.inspect.json"))));
        var profile = new FakeProfile(); profile.Specs[next] = new("synthetic", "fake.1.inspect", true, asset);
        await using var scheduler = new OperationScheduler(store, new FakeWorkerSupervisor(SchedulerFixture.WorkerPath(files), files.Root), profile);
        Assert.Equal(OperationStatusResult_State.FAILED, store.ReadStatus(old).State);
        Assert.False(store.OwnerBlocked); Assert.Equal(1, store.AttemptCount(old));
        var timer = Stopwatch.StartNew();
        while (store.ReadStatus(next).State != OperationStatusResult_State.SUCCEEDED)
        { Assert.True(timer.Elapsed < TimeSpan.FromSeconds(10)); await scheduler.PumpOnceAsync(); await Task.Delay(10); }
        Assert.Equal(1, store.AttemptCount(old)); Assert.Equal(1, store.AttemptCount(next));
    }
    [Fact]
    public async Task One_owner_serializes_two_different_projects_on_the_same_portal()
    {
        await using var f = new SchedulerFixture(); var first = f.Admit(FakeScenario.Hang);
        await f.Running(first); var second = f.Admit(mutation: true, differentProject: true);
        for (int i = 0; i < 5; i++) await f.Scheduler.PumpOnceAsync();
        Assert.Equal(0, f.Store.AttemptCount(second)); Assert.Equal(OperationStatusResult_State.ACCEPTED, f.Store.ReadStatus(second).State);
        Assert.Equal(1, f.Store.AttemptCount(first));
    }
    [Fact]
    public async Task Cancellation_race_has_either_zero_dispatch_or_a_recorded_running_cancel()
    {
        await using var f = new SchedulerFixture(); var id = f.Admit(FakeScenario.Cancellable);
        await Task.WhenAll(f.Scheduler.PumpOnceAsync(), Task.Run(() => f.Store.Cancel(id)));
        await f.Until(() => f.Store.ReadStatus(id).State == OperationStatusResult_State.CANCELLED);
        Assert.InRange(f.Store.AttemptCount(id), 0, 1); Assert.True(f.Store.Read(id).Report.CancelRequested);
        Assert.Equal(OperationOutcome.NOT_STARTED, f.Store.Read(id).Report.Outcome);
    }
    [Fact]
    public async Task Cancelling_a_hung_mutation_never_claims_the_call_has_stopped()
    {
        await using var f = new SchedulerFixture(); var id = f.Admit(FakeScenario.Hang, mutation: true, timeout: 3);
        await f.Running(id); f.Store.Cancel(id); await f.Scheduler.PumpOnceAsync();
        Assert.Equal(OperationStatusResult_State.RUNNING, f.Store.ReadStatus(id).State);
        await f.Advance(4); Assert.Equal(OperationStatusResult_State.RECONCILIATION_REQUIRED, f.Store.ReadStatus(id).State);
        f.Store.Cancel(id); Assert.True(f.Store.OwnerBlocked); Assert.Equal(OperationOutcome.UNKNOWN, f.Store.Read(id).Report.Outcome);
    }
    [Fact]
    public async Task Cancelling_during_proven_no_effect_backoff_does_not_start_a_second_attempt()
    {
        await using var f = new SchedulerFixture(); var id = f.Admit(FakeScenario.FailBefore);
        await f.Until(() => f.Store.ReceiptCount(id) == 1); f.Store.Cancel(id); await f.Scheduler.PumpOnceAsync();
        Assert.Equal(OperationStatusResult_State.CANCELLED, f.Store.ReadStatus(id).State); Assert.Equal(1, f.Store.AttemptCount(id));
    }
    [Fact]
    public async Task A_missing_execution_profile_is_denied_before_starting_any_worker()
    {
        await using var f = new SchedulerFixture(); var id = f.Admit(); f.Profile.Specs.Remove(id);
        await f.Scheduler.PumpOnceAsync(); Assert.Equal(ErrorCode.UNSUPPORTED, f.Store.Read(id).Report.Error!.Code);
        Assert.Null(f.Scheduler.Observation.Pid); Assert.Equal(0, f.Store.AttemptCount(id));
    }
    [Fact]
    public async Task Queue_deadline_fails_without_dispatch_or_worker_start()
    {
        await using var f = new SchedulerFixture(); var id = f.Admit();
        // The accepted timestamp is immutable. Advance while maintaining the owner's heartbeat/lease.
        for (int i = 0; i < 151; i++) { f.Clock.Advance(TimeSpan.FromSeconds(2)); Assert.True(f.Store.RenewOwner(f.Scheduler.Owner)); }
        await f.Scheduler.PumpOnceAsync();
        Assert.Equal(ErrorCode.WAIT_TIMEOUT, f.Store.Read(id).Report.Error!.Code); Assert.Equal(0, f.Store.AttemptCount(id));
    }
    [Fact]
    public async Task Queue_deadline_is_enforced_even_while_another_call_is_hung()
    {
        await using var f = new SchedulerFixture(); var first = f.Admit(FakeScenario.Hang, timeout: 30); await f.Running(first);
        var node = JsonNode.Parse(StoreFixture.Wire("project.inspect"))!;
        node["idempotency_key"] = "synthetic-short-queue"; node["params"]!["queue_timeout_s"] = 1;
        var second = f.Store.Accept("synthetic-principal", JsonSerializer.SerializeToUtf8Bytes(node)).Operation.Report.OperationId;
        await f.Advance(3); Assert.Equal(ErrorCode.WAIT_TIMEOUT, f.Store.Read(second).Report.Error!.Code);
        Assert.Equal(0, f.Store.AttemptCount(second)); Assert.Equal(OperationStatusResult_State.RUNNING, f.Store.ReadStatus(first).State);
    }
    [Fact]
    public async Task Safe_pending_work_survives_host_reopen_and_can_then_execute()
    {
        using var files = new StoreFixture(); var store = files.Open(); var operation = files.Accept(store, "project.inspect").Operation;
        var asset = store.Artifacts.Put(operation.Report.OperationId, new MemoryStream(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "response.project.inspect.json"))));
        var profile = new FakeProfile(); profile.Specs[operation.Report.OperationId] = new("synthetic", "fake.1.inspect", true, asset);
        var reopened = files.Open();
        await using var scheduler = new OperationScheduler(reopened, new FakeWorkerSupervisor(SchedulerFixture.WorkerPath(files), files.Root), profile);
        var timer = Stopwatch.StartNew();
        while (reopened.ReadStatus(operation.Report.OperationId).State != OperationStatusResult_State.SUCCEEDED)
        { Assert.True(timer.Elapsed < TimeSpan.FromSeconds(10)); await scheduler.PumpOnceAsync(); await Task.Delay(10); }
        Assert.Equal(1, reopened.AttemptCount(operation.Report.OperationId));
    }
    [Fact]
    public async Task Expired_owner_is_fenced_even_if_its_worker_is_still_alive()
    {
        await using var f = new SchedulerFixture(); var id = f.Admit(FakeScenario.Hang, mutation: true);
        await f.Running(id); f.Clock.Advance(TimeSpan.FromSeconds(16));
        Assert.False(f.Store.RenewOwner(f.Scheduler.Owner));
        await Assert.ThrowsAsync<StorageException>(() => f.Scheduler.PumpOnceAsync());
        Assert.Equal(OperationStatusResult_State.RUNNING, f.Store.ReadStatus(id).State);
        Assert.Equal(1, f.Store.AttemptCount(id));
    }
    [Fact]
    public async Task Completed_quarantined_operation_is_not_replayed_on_a_second_host_epoch()
    {
        using var files = new StoreFixture(); var clock = new ManualClock(); var store = new SqliteOperationStore(files.Location, clock: clock);
        var operation = files.Accept(store).Operation;
        var asset = store.Artifacts.Put(operation.Report.OperationId, new MemoryStream(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "response.sync.apply.json"))));
        var profile = new FakeProfile(); profile.Specs[operation.Report.OperationId] = new("synthetic", "fake.1.apply", false, asset, FakeScenario.Hang);
        var qualificationProfile = QualificationProfile.Load(files.Repository);
        long fence;
        await using (var scheduler = new OperationScheduler(store, new FakeWorkerSupervisor(SchedulerFixture.WorkerPath(files), files.Root, clock), profile, clock))
        {
            await scheduler.PumpOnceAsync(); fence = scheduler.Owner.Fence;
            var timer = Stopwatch.StartNew();
            while (store.ReadStatus(operation.Report.OperationId).Phase != "EXECUTING") { Assert.True(timer.Elapsed < TimeSpan.FromSeconds(qualificationProfile.HarnessNumber("fake_worker_phase_observation_s"))); await scheduler.PumpOnceAsync(); await Task.Delay(10); }
        }
        await using var second = new OperationScheduler(store, new FakeWorkerSupervisor(SchedulerFixture.WorkerPath(files), files.Root, clock), profile, clock);
        Assert.True(second.Owner.Fence > fence); Assert.True(store.OwnerBlocked);
        await second.PumpOnceAsync(); Assert.Equal(1, store.AttemptCount(operation.Report.OperationId));
        Assert.Equal(OperationState.RECONCILIATION_REQUIRED, store.Read(operation.Report.OperationId).Report.State);
    }
    [Theory]
    [InlineData(FakeScenario.WrongFence)]
    [InlineData(FakeScenario.WrongPlanHash)]
    public async Task Wrong_fence_or_plan_from_a_real_worker_is_evidence_only(FakeScenario scenario)
    {
        await using var f = new SchedulerFixture(); var id = f.Admit(scenario, mutation: true, timeout: 3);
        await f.Until(() => f.Store.ReceiptCount(id) == 1);
        Assert.Equal(1, f.Store.ReceiptCount(id)); Assert.Equal(OperationStatusResult_State.RUNNING, f.Store.ReadStatus(id).State);
        await f.Advance(4); Assert.Equal(OperationStatusResult_State.RECONCILIATION_REQUIRED, f.Store.ReadStatus(id).State);
        Assert.True(f.Store.OwnerBlocked); Assert.Equal(1, f.Store.AttemptCount(id));
    }
}
