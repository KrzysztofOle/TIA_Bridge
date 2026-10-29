using System.Text.Json;
using TiaBridge.Core.Identity;
using TiaBridge.Core.Operations;
using TiaBridge.Core.Transport.V1;
using TiaBridge.Host.Qualification;
using TiaBridge.Host.Scheduling;
using TiaBridge.Ipc;
using static TiaBridge.Host.Storage.SQLite.SqliteDatabase;

namespace TiaBridge.Host.Operations;

internal sealed record BootstrapState(BootstrapAuthorization Authorization, string Sha256, string State, string? HostEpoch, int UsedCycles, string? StopReason);
internal sealed record BootstrapCycleRecord(string OperationId, int Cycle, Guid WorkerEpoch, int WorkerPid, long WorkerStartTicks,
    string ReservedAt, string State, byte[]? Telemetry, byte[]? TerminalReport, byte[]? InitialTelemetry);

public sealed partial class SqliteOperationStore
{
    internal byte[] BootstrapDocument(string id) => database.Read(c => (byte[])(Scalar(c, null, "SELECT document FROM bootstrap_authorizations WHERE authorization_id=$id", ("$id", id)) ?? throw new IOException("BOOTSTRAP_NOT_FOUND")));
    internal void BootstrapRelease(string id, bool confirmed) => database.Write((c, t) =>
    {
        Exec(c, t, "INSERT INTO bootstrap_release VALUES($id,$at,$ok) ON CONFLICT(authorization_id) DO NOTHING", ("$id", id), ("$at", At(clock.GetUtcNow())), ("$ok", confirmed ? 1 : 0)); return 0;
    });
    internal bool BootstrapReleased(string id) => database.Read(c => ScalarLong(c, null, "SELECT count(*) FROM bootstrap_release WHERE authorization_id=$id AND confirmed=1", ("$id", id)) == 1);
    internal void AuthorizeBootstrap(byte[] document, string operatorSid)
    {
        var auth = BootstrapAuthorization.Read(document);
        database.Write((c, t) =>
        {
            Exec(c, t, "INSERT INTO bootstrap_authorizations(authorization_id,document,sha256,operator_sid,approved_at,state) VALUES($id,$doc,$sha,$sid,$at,'READY')",
                ("$id", auth.AuthorizationId), ("$doc", document), ("$sha", Hash(document)), ("$sid", operatorSid), ("$at", At(clock.GetUtcNow())));
            return 0;
        }, checkpoints: true);
    }
    internal BootstrapState? ReadBootstrap(string id) => database.Read(c =>
    {
        using var command = Command(c, null, "SELECT document,sha256,state,host_epoch,used_cycles,stop_reason FROM bootstrap_authorizations WHERE authorization_id=$id", ("$id", id));
        using var row = command.ExecuteReader();
        if (!row.Read()) return null;
        byte[] bytes = (byte[])row[0]; if (Hash(bytes) != row.GetString(1)) throw new IOException("BOOTSTRAP_DOCUMENT_CORRUPT");
        var auth = BootstrapAuthorization.Read(bytes); if (auth.AuthorizationId != id) throw new IOException("BOOTSTRAP_DOCUMENT_MISMATCH");
        return new BootstrapState(auth, row.GetString(1), row.GetString(2), row.IsDBNull(3) ? null : row.GetString(3), row.GetInt32(4), row.IsDBNull(5) ? null : row.GetString(5));
    });
    internal void BindBootstrap(string id, OwnerToken owner) => database.Write((c, t) =>
    {
        CheckOwner(c, t, owner);
        Exec(c, t, "UPDATE bootstrap_authorizations SET state='ACTIVE',host_epoch=$h WHERE authorization_id=$id AND state='READY' AND host_epoch IS NULL AND used_cycles=0", ("$id", id), ("$h", owner.Epoch.ToString()));
        return 0;
    }, checkpoints: true);
    internal void StopBootstrap(string id, string reason, bool revoke = false) => database.Write((c, t) =>
    {
        Exec(c, t, "UPDATE bootstrap_authorizations SET state=$s,stop_reason=$r WHERE authorization_id=$id AND state IN ('READY','ACTIVE')", ("$id", id), ("$s", revoke ? "REVOKED" : "STOPPED"), ("$r", reason));
        return 0;
    }, checkpoints: true);
    internal bool BootstrapCanDispatch(BootstrapPermit permit, OwnerToken owner, OperationId id)
    {
        var state = ReadBootstrap(permit.AuthorizationId);
        return state is { State: "ACTIVE" } && state.Sha256 == permit.AuthorizationSha256 && state.HostEpoch == owner.Epoch.ToString()
            && (state.UsedCycles < state.Authorization.CycleBudget || BootstrapCycles(permit.AuthorizationId).Any(c => c.OperationId == id.ToString() && c.State == "RESERVED"));
    }
    private void ReserveBootstrap(Microsoft.Data.Sqlite.SqliteConnection c, Microsoft.Data.Sqlite.SqliteTransaction t,
        OwnerToken owner, OperationId id, Guid workerEpoch, ExecutionSpec spec, int attempt)
    {
        if (spec.Bootstrap is not { } permit) return;
        if (attempt != 1 || spec.QualifiedEffectFreeRead || spec.V19Target is null) throw new IOException("BOOTSTRAP_REPLAY_DENIED");
        int budget = BootstrapBudget(c, t, permit.AuthorizationId, permit.AuthorizationSha256);
        using var update = Command(c, t, "UPDATE bootstrap_authorizations SET used_cycles=used_cycles+1 WHERE authorization_id=$id AND sha256=$sha AND state='ACTIVE' AND host_epoch=$h AND used_cycles<$budget",
            ("$id", permit.AuthorizationId), ("$sha", permit.AuthorizationSha256), ("$h", owner.Epoch.ToString()), ("$budget", budget));
        if (update.ExecuteNonQuery() != 1) throw new IOException("BOOTSTRAP_BUDGET_OR_SESSION_DENIED");
        int cycle = checked((int)ScalarLong(c, t, "SELECT used_cycles FROM bootstrap_authorizations WHERE authorization_id=$id", ("$id", permit.AuthorizationId)));
        Exec(c, t, "INSERT INTO bootstrap_cycles(operation_id,authorization_id,cycle,host_epoch,worker_epoch,worker_pid,worker_start_ticks,reserved_at) SELECT $op,$id,$n,$h,worker_epoch,pid,start_ticks,$at FROM worker_processes WHERE worker_epoch=$w AND host_epoch=$h",
            ("$op", id.ToString()), ("$id", permit.AuthorizationId), ("$n", cycle), ("$h", owner.Epoch.ToString()), ("$w", workerEpoch.ToString()), ("$at", At(clock.GetUtcNow())));
    }
    internal void BootstrapTelemetry(OperationId id, object telemetry) => database.Write((c, t) =>
    {
        Exec(c, t, "UPDATE bootstrap_cycles SET telemetry=$b WHERE operation_id=$id AND telemetry IS NULL", ("$id", id.ToString()), ("$b", JsonSerializer.SerializeToUtf8Bytes(telemetry, Protocol.Json)));
        return 0;
    });
    internal void BootstrapInitialTelemetry(OperationId id, object telemetry) => database.Write((c, t) =>
    {
        Exec(c, t, "UPDATE bootstrap_cycles SET initial_telemetry=$b WHERE operation_id=$id AND initial_telemetry IS NULL", ("$id", id.ToString()), ("$b", JsonSerializer.SerializeToUtf8Bytes(telemetry, Protocol.Json))); return 0;
    });
    private void CompleteBootstrap(Microsoft.Data.Sqlite.SqliteConnection c, Microsoft.Data.Sqlite.SqliteTransaction t, OperationReport report)
    {
        string? auth = Scalar(c, t, "SELECT authorization_id FROM bootstrap_cycles WHERE operation_id=$id", ("$id", report.OperationId.ToString())) as string;
        if (auth is null) return;
        Exec(c, t, "UPDATE bootstrap_cycles SET state=$s,terminal_report=$b WHERE operation_id=$id AND state='RESERVED'", ("$id", report.OperationId.ToString()), ("$s", report.State == OperationState.SUCCEEDED ? "COMPLETED" : "UNCERTAIN_OR_FAILED"), ("$b", ContractJson.WriteReport(report)));
        int budget = BootstrapBudget(c, t, auth);
        Exec(c, t, "UPDATE bootstrap_authorizations SET state=$s,stop_reason=$r WHERE authorization_id=$id AND state='ACTIVE' AND (used_cycles=$budget OR $failed=1)",
            ("$id", auth), ("$s", report.State == OperationState.SUCCEEDED ? "FINISHED" : "STOPPED"), ("$r", report.State == OperationState.SUCCEEDED ? "BUDGET_COMPLETED" : report.Phase), ("$failed", report.State == OperationState.SUCCEEDED ? 0 : 1), ("$budget", budget));
    }
    private static int BootstrapBudget(Microsoft.Data.Sqlite.SqliteConnection c, Microsoft.Data.Sqlite.SqliteTransaction t, string id, string? expectedSha = null)
    {
        using var command = Command(c, t, "SELECT document,sha256 FROM bootstrap_authorizations WHERE authorization_id=$id", ("$id", id));
        using var row = command.ExecuteReader();
        if (!row.Read()) throw new IOException("BOOTSTRAP_NOT_FOUND");
        byte[] bytes = (byte[])row[0]; string sha = Hash(bytes);
        if (sha != row.GetString(1) || expectedSha is not null && sha != expectedSha) throw new IOException("BOOTSTRAP_DOCUMENT_CORRUPT");
        var auth = BootstrapAuthorization.Read(bytes);
        if (auth.AuthorizationId != id) throw new IOException("BOOTSTRAP_DOCUMENT_MISMATCH");
        return auth.CycleBudget;
    }
    internal IReadOnlyList<BootstrapCycleRecord> BootstrapCycles(string id) => database.Read(c =>
    {
        using var command = Command(c, null, "SELECT operation_id,cycle,worker_epoch,worker_pid,worker_start_ticks,reserved_at,state,telemetry,terminal_report,initial_telemetry FROM bootstrap_cycles WHERE authorization_id=$id ORDER BY cycle", ("$id", id));
        using var rows = command.ExecuteReader(); var result = new List<BootstrapCycleRecord>();
        while (rows.Read()) result.Add(new(rows.GetString(0), rows.GetInt32(1), Guid.Parse(rows.GetString(2)), rows.GetInt32(3), rows.GetInt64(4), rows.GetString(5), rows.GetString(6), rows.IsDBNull(7) ? null : (byte[])rows[7], rows.IsDBNull(8) ? null : (byte[])rows[8], rows.IsDBNull(9) ? null : (byte[])rows[9]));
        return (IReadOnlyList<BootstrapCycleRecord>)result;
    });
    internal byte[][] BootstrapReceipts(OperationId id) => database.Read(c =>
    {
        using var command = Command(c, null, "SELECT message_json,sha256 FROM worker_receipts WHERE operation_id=$id ORDER BY receipt_id", ("$id", id.ToString()));
        using var rows = command.ExecuteReader(); var result = new List<byte[]>();
        while (rows.Read()) { byte[] bytes = (byte[])rows[0]; if (Hash(bytes) != rows.GetString(1)) throw new IOException("BOOTSTRAP_RECEIPT_CORRUPT"); result.Add(bytes); }
        return result.ToArray();
    });
}
