using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using TiaBridge.Core.Identity;
using TiaBridge.Core.Operations;
using TiaBridge.Core.Transport.V1;
using TiaBridge.Host.Operations;
using TiaBridge.Host.Storage;
using Xunit;

namespace TiaBridge.Host.Tests;

public class OperationStorageTests
{
    [Fact]
    public void Admission_persists_request_event_outbox_and_dedupe_across_reopen()
    {
        using var f = new StoreFixture(); var store = f.Open();
        var first = f.Accept(store);
        Assert.False(first.AlreadyAccepted); Assert.Equal(OperationState.ACCEPTED, first.Operation.Report.State);
        Assert.Equal("NTFS", new DriveInfo(Path.GetPathRoot(f.Root)!).DriveFormat);
        Assert.Equal(1, f.Count("operations")); Assert.Equal(1, f.Count("events")); Assert.Equal(1, f.Count("outbox")); Assert.Equal(1, f.Count("idempotency_keys"));
        var second = f.Accept(f.Open());
        Assert.True(second.AlreadyAccepted); Assert.Equal(first.Operation.Report.OperationId, second.Operation.Report.OperationId);
        Assert.Equal(first.Operation.BodySha256, second.Operation.BodySha256);
        Assert.Equal(first.Operation.RequestJson, f.Open().Read(first.Operation.Report.OperationId).RequestJson);
        Assert.Null(f.Open().ReadResult(first.Operation.Report.OperationId));
    }

    [Fact]
    public async Task One_hundred_concurrent_identical_submits_across_store_instances_have_one_identity()
    {
        using var f = new StoreFixture(); var first = f.Open(); first.RegisterProject(ContractJson.ReadRequest(StoreFixture.Wire()).Identity.Project!);
        var stores = Enumerable.Range(0, 4).Select(_ => f.Open()).ToArray();
        var results = await Task.WhenAll(Enumerable.Range(0, 100).Select(i => Task.Run(() => stores[i % stores.Length].Accept("synthetic-principal", StoreFixture.Wire()))));
        Assert.Single(results.Select(r => r.Operation.Report.OperationId).Distinct());
        Assert.Equal(1, results.Count(r => !r.AlreadyAccepted)); Assert.Equal(99, results.Count(r => r.AlreadyAccepted));
        Assert.Equal(1, f.Count("operations")); Assert.Equal(1, f.Count("events")); Assert.Equal(1, f.Count("outbox"));
    }

    [Fact]
    public void New_key_scope_and_conflicting_body_obey_core_idempotency()
    {
        using var f = new StoreFixture(); var store = f.Open(); var first = f.Accept(store);
        var node = JsonNode.Parse(StoreFixture.Wire())!; node["params"]!["plan_id"] = "synthetic.different";
        Assert.Equal(ErrorCode.IDEMPOTENCY_MISMATCH, Assert.Throws<StorageException>(() => store.Accept("synthetic-principal", JsonSerializer.SerializeToUtf8Bytes(node))).Code);
        var differentPrincipal = f.Accept(store, principal: "synthetic-other");
        Assert.NotEqual(first.Operation.Report.OperationId, differentPrincipal.Operation.Report.OperationId);
        node["idempotency_key"] = "another-key";
        Assert.NotEqual(first.Operation.Report.OperationId, store.Accept("synthetic-principal", JsonSerializer.SerializeToUtf8Bytes(node)).Operation.Report.OperationId);
        var global = f.Accept(store, "diagnostics.health");
        Assert.Equal(global.Operation.Report.OperationId, f.Accept(f.Open(), "diagnostics.health").Operation.Report.OperationId);
        Assert.Equal(4, f.Count("operations"));
    }

    [Fact]
    public void Unregistered_project_and_invalid_request_never_create_acceptance()
    {
        using var f = new StoreFixture(); var store = f.Open();
        Assert.Equal(ErrorCode.IDENTITY_MISMATCH, Assert.Throws<StorageException>(() => store.Accept("principal", StoreFixture.Wire())).Code);
        Assert.Throws<ContractValidationException>(() => store.Accept("principal", "{}"u8.ToArray()));
        Assert.Throws<ContractValidationException>(() => store.Accept("principal", new byte[ContractJson.MaxRequestBytes + 1]));
        Assert.Throws<ArgumentException>(() => store.Accept("", StoreFixture.Wire()));
        Assert.Equal(0, f.Count("operations")); Assert.Equal(0, f.Count("outbox"));
    }

    [Fact]
    public void Journal_state_and_terminal_proof_survive_restart_and_cannot_be_overwritten()
    {
        using var f = new StoreFixture(); var store = f.Open();
        var admitted = f.Accept(store, "diagnostics.health");
        var running = StoreFixture.Start(store, admitted.Operation, out _);
        var example = ContractJson.ReadReport(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "response.diagnostics.health.json")));
        var completed = store.Record(running.Report.OperationId, running.Revision, example with { OperationId = running.Report.OperationId, AcceptedAt = running.Report.AcceptedAt });
        Assert.Equal(OperationState.SUCCEEDED, f.Open().ReadResult(completed.Report.OperationId)!.State);
        Assert.Equal(new long[] { 1, 2, 3, 4 }, f.Open().Events(completed.Report.OperationId).Select(e => e.Sequence));
        Assert.Equal(2, store.Events(completed.Report.OperationId, 2).Count);
        Assert.Equal(1, f.Count("operation_results")); Assert.Empty(store.Pending());
        Assert.Throws<StorageException>(() => store.Record(completed.Report.OperationId, completed.Revision, completed.Report));
        Assert.Equal(4, store.Events(completed.Report.OperationId).Count);
        Assert.Equal(ContractJson.WriteReport(completed.Report), ContractJson.WriteReport(f.Open().ReadResult(completed.Report.OperationId)!));
        var process = f.Probe("inspect", completed.Report.OperationId.ToString()); Assert.True(process.Code == 0, process.Output);
        Assert.Equal("SUCCEEDED", JsonNode.Parse(process.Output)!["state"]!.GetValue<string>());
        Assert.True(JsonNode.Parse(process.Output)!["terminal"]!.GetValue<bool>());
    }

    [Fact]
    public void Invalid_lifecycle_stale_revision_wrong_identity_and_unclaimed_running_are_rejected()
    {
        using var f = new StoreFixture(); var store = f.Open(); var first = f.Accept(store).Operation; var id = first.Report.OperationId;
        Assert.Throws<InvalidOperationException>(() => store.Record(id, 0, first.Report with { State = OperationState.RUNNING }));
        Assert.Throws<StorageException>(() => store.Record(id, 1, first.Report with { State = OperationState.QUEUED }));
        Assert.Throws<StorageException>(() => store.Record(id, 0, first.Report with { OperationId = new OperationId(Guid.NewGuid()) }));
        var queued = store.Record(id, 0, first.Report with { State = OperationState.QUEUED });
        Assert.Throws<StorageException>(() => store.Record(id, queued.Revision, queued.Report with { State = OperationState.RUNNING }));
        Assert.Equal(OperationState.QUEUED, store.Read(id).Report.State);
        Assert.Equal(2, store.Events(id).Count);
    }

    [Fact]
    public void Unknown_mutation_is_terminal_retained_and_quarantines_pending_project_work()
    {
        using var f = new StoreFixture(); var store = f.Open(); var first = f.Accept(store);
        var running = StoreFixture.Start(store, first.Operation, out _);
        var unknown = store.Record(running.Report.OperationId, running.Revision, running.Report with
        { State = OperationState.RECONCILIATION_REQUIRED, Outcome = OperationOutcome.UNKNOWN, Phase = "TIMEOUT", Error = new OperationError(ErrorCode.TIMEOUT, ErrorCategory.EXECUTION, "Synthetic unknown", false, OperationOutcome.UNKNOWN) });
        var replay = f.Accept(f.Open());
        Assert.Equal(unknown.Report.OperationId, replay.Operation.Report.OperationId); Assert.Equal(OperationState.RECONCILIATION_REQUIRED, replay.Operation.Report.State);
        Assert.Null(store.TryClaim(unknown.Report.OperationId)); Assert.Empty(f.Open().Pending()); Assert.Equal(1, f.Count("quarantines"));
        var node = JsonNode.Parse(StoreFixture.Wire())!; node["idempotency_key"] = "next-request";
        var next = store.Accept("synthetic-principal", JsonSerializer.SerializeToUtf8Bytes(node));
        Assert.Null(store.TryClaim(next.Operation.Report.OperationId)); Assert.Empty(store.Pending());
        Assert.Throws<StorageException>(() => store.Record(unknown.Report.OperationId, unknown.Revision, unknown.Report with { State = OperationState.QUEUED, Outcome = OperationOutcome.NOT_STARTED, Error = null }));
        var process = f.Probe("inspect", unknown.Report.OperationId.ToString()); Assert.True(process.Code == 0, process.Output);
        Assert.Equal("RECONCILIATION_REQUIRED", JsonNode.Parse(process.Output)!["state"]!.GetValue<string>());
        Assert.Equal(0, JsonNode.Parse(process.Output)!["pending"]!.GetValue<int>());
    }

    [Fact]
    public async Task Outbox_claim_is_single_use_across_concurrency_and_restart()
    {
        using var f = new StoreFixture(); var store = f.Open(); var id = f.Accept(store).Operation.Report.OperationId;
        var claims = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => store.TryClaim(id))));
        Guid claim = Assert.Single(claims, c => c.HasValue)!.Value;
        Assert.Null(f.Open().TryClaim(id)); Assert.Empty(f.Open().Pending());
        Assert.False(store.RecordDispatch(id, Guid.NewGuid())); Assert.True(store.RecordDispatch(id, claim));
        Assert.False(f.Open().RecordDispatch(id, claim));
        Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() => f.Sql("UPDATE outbox SET state='PENDING',claim_id=NULL"));
    }

    [Fact]
    public void Quarantine_blocks_dispatch_record_for_work_claimed_before_the_unknown_outcome()
    {
        using var f = new StoreFixture(); var store = f.Open(); var first = f.Accept(store).Operation;
        var running = StoreFixture.Start(store, first, out _);
        var node = JsonNode.Parse(StoreFixture.Wire())!; node["idempotency_key"] = "pending-other";
        var second = store.Accept("synthetic-principal", JsonSerializer.SerializeToUtf8Bytes(node)).Operation;
        var claimed = store.TryClaim(second.Report.OperationId)!.Value;
        store.Record(running.Report.OperationId, running.Revision, running.Report with { State = OperationState.RECONCILIATION_REQUIRED, Outcome = OperationOutcome.UNKNOWN });
        Assert.False(f.Open().RecordDispatch(second.Report.OperationId, claimed));
        Assert.Null(store.TryClaim(second.Report.OperationId));
    }
}
