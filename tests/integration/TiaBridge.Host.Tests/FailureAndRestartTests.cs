using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using TiaBridge.Core.Operations;
using TiaBridge.Core.Transport.V1;
using TiaBridge.Host.Operations;
using TiaBridge.Host.Storage;
using Xunit;

namespace TiaBridge.Host.Tests;

public class FailureAndRestartTests
{
    [Theory]
    [InlineData(StorageFaultPoint.OperationInserted)]
    [InlineData(StorageFaultPoint.EventInserted)]
    [InlineData(StorageFaultPoint.OutboxInserted)]
    [InlineData(StorageFaultPoint.BeforeCommit)]
    public void Injected_admission_failure_rolls_back_the_entire_intent(StorageFaultPoint point)
    {
        using var f = new StoreFixture(); var store = f.Open(new ThrowFault(point));
        var request = ContractJson.ReadRequest(StoreFixture.Wire()); store.RegisterProject(request.Identity.Project!);
        var error = Assert.Throws<StorageException>(() => store.Accept("synthetic-principal", StoreFixture.Wire()));
        Assert.False(error.CommitMayHaveSucceeded);
        foreach (string table in new[] { "operations", "events", "outbox", "idempotency_keys" }) Assert.Equal(0, f.Count(table));
        Assert.False(f.Accept(f.Open()).AlreadyAccepted);
    }

    [Fact]
    public void Failure_after_commit_is_not_reported_as_absence_and_repeat_returns_the_same_identity()
    {
        using var f = new StoreFixture(); var store = f.Open(new ThrowFault(StorageFaultPoint.AfterCommit));
        var request = ContractJson.ReadRequest(StoreFixture.Wire()); store.RegisterProject(request.Identity.Project!);
        var error = Assert.Throws<StorageException>(() => store.Accept("synthetic-principal", StoreFixture.Wire()));
        Assert.True(error.CommitMayHaveSucceeded);
        Assert.Equal(1, f.Count("operations")); Assert.Equal(1, f.Count("events"));
        Assert.True(f.Accept(f.Open()).AlreadyAccepted);
    }

    [Theory]
    [InlineData("events")]
    [InlineData("outbox")]
    public void Real_sql_statement_failure_cannot_leave_partial_acceptance(string table)
    {
        using var f = new StoreFixture(); var store = f.Open();
        f.Sql($"CREATE TRIGGER test_failure BEFORE INSERT ON {table} BEGIN SELECT RAISE(ABORT,'synthetic SQL failure'); END;");
        Assert.Throws<StorageException>(() => f.Accept(store));
        Assert.Equal(0, f.Count("operations")); Assert.Equal(0, f.Count("idempotency_keys")); Assert.Equal(0, f.Count("events")); Assert.Equal(0, f.Count("outbox"));
        f.Sql("DROP TRIGGER test_failure;"); Assert.False(f.Accept(f.Open()).AlreadyAccepted);
    }

    [Fact]
    public void Sqlite_full_and_artifact_io_full_are_controlled_without_filling_a_real_disk()
    {
        using var f = new StoreFixture(); var store = f.Open(new ThrowFault(StorageFaultPoint.BeforeCommit, sqliteFull: true));
        Assert.Equal(ErrorCode.STORAGE_FULL, Assert.Throws<StorageException>(() => f.Accept(store)).Code);
        Assert.Equal(0, f.Count("operations"));
        var ordinary = f.Open(); var accepted = f.Accept(ordinary);
        var artifacts = f.Open(new DiskFullFault()).Artifacts;
        Assert.Equal(ErrorCode.STORAGE_FULL, Assert.Throws<StorageException>(() => artifacts.Put(accepted.Operation.Report.OperationId, new MemoryStream("synthetic"u8.ToArray()))).Code);
        Assert.Equal(0, f.Count("artifacts")); Assert.NotEmpty(ordinary.Artifacts.RetainedOrphans());
    }

    [Theory]
    [InlineData(StorageFaultPoint.BeforeCommit, false)]
    [InlineData(StorageFaultPoint.AfterCommit, true)]
    public void Deterministic_process_exit_at_admission_boundary_survives_restart(StorageFaultPoint point, bool committed)
    {
        using var f = new StoreFixture();
        var crashed = f.Probe(checkpoint: point.ToString()); Assert.Equal(73, crashed.Code);
        var reopened = f.Open(); Assert.Equal(committed ? 1 : 0, f.Count("operations"));
        var secondProcess = f.Probe(); Assert.True(secondProcess.Code == 0, secondProcess.Output);
        var json = JsonNode.Parse(secondProcess.Output)!;
        Assert.Equal(committed, json["duplicate"]!.GetValue<bool>());
        Assert.Equal(1, json["events"]!.GetValue<int>()); Assert.Equal(1, json["pending"]!.GetValue<int>());
        var thirdProcess = f.Probe(); Assert.Equal(0, thirdProcess.Code);
        Assert.Equal(json["operation_id"]!.GetValue<string>(), JsonNode.Parse(thirdProcess.Output)!["operation_id"]!.GetValue<string>());
    }

    [Fact]
    public async Task Independent_processes_racing_the_same_key_create_one_acceptance()
    {
        using var f = new StoreFixture(); _ = f.Open();
        var processes = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => f.Probe())));
        Assert.All(processes, p => Assert.True(p.Code == 0, p.Output));
        Assert.Single(processes.Select(p => JsonNode.Parse(p.Output)!["operation_id"]!.GetValue<string>()).Distinct());
        Assert.Equal(1, f.Count("operations")); Assert.Equal(1, f.Count("events")); Assert.Equal(1, f.Count("outbox"));
    }

    [Fact]
    public void Event_failure_rolls_back_a_state_change_and_terminal_result()
    {
        using var f = new StoreFixture(); var store = f.Open(); var accepted = f.Accept(store).Operation;
        var running = StoreFixture.Start(store, accepted, out _);
        var faulted = f.Open(new ThrowFault(StorageFaultPoint.EventInserted));
        Assert.Throws<StorageException>(() => faulted.Record(running.Report.OperationId, running.Revision, running.Report with
        { State = OperationState.RECONCILIATION_REQUIRED, Outcome = OperationOutcome.UNKNOWN }));
        Assert.Equal(OperationState.RUNNING, store.Read(running.Report.OperationId).Report.State);
        Assert.Equal(3, f.Count("events")); Assert.Equal(0, f.Count("operation_results")); Assert.Equal(0, f.Count("quarantines"));
    }

    [Fact]
    public void Read_does_not_wait_for_an_uncommitted_sqlite_writer()
    {
        using var f = new StoreFixture(); var store = f.Open(); var accepted = f.Accept(store);
        using var connection = f.Connection(); using var transaction = connection.BeginTransaction(deferred: false);
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "UPDATE projects SET registered_at=registered_at"; command.ExecuteNonQuery();
        Assert.Equal(accepted.Operation.Report.OperationId, store.Read(accepted.Operation.Report.OperationId).Report.OperationId);
        Assert.Single(store.Events(accepted.Operation.Report.OperationId));
        transaction.Rollback();
    }

    [Fact]
    public async Task Outbox_claim_is_compare_and_swap_across_independent_processes()
    {
        using var f = new StoreFixture(); var store = f.Open(); var id = f.Accept(store).Operation.Report.OperationId;
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => f.Probe("claim"))));
        Assert.All(results, r => Assert.True(r.Code == 0, r.Output));
        Assert.Single(results, r => JsonNode.Parse(r.Output)!["claim_id"] is not null);
        Assert.Null(f.Open().TryClaim(id)); Assert.Empty(f.Open().Pending());
    }
}
