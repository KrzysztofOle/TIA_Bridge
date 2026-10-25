using System.Diagnostics;
using System.Globalization;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TiaBridge.Core.Identity;
using TiaBridge.Core.Operations;
using TiaBridge.Core.Transport.V1;
using TiaBridge.Host.Scheduling;
using TiaBridge.Host.Storage;
using TiaBridge.Ipc;
using static TiaBridge.Host.Storage.SQLite.SqliteDatabase;

namespace TiaBridge.Host.Operations;

public sealed partial class SqliteOperationStore
{
    internal IDisposable LockScheduler()
    {
        try { return new FileStream(database.Location.ManagedPath("scheduler.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { throw new StorageException(ErrorCode.NOT_OWNER); }
    }
    internal OwnerToken BeginOwnership() => database.Write((c, t) =>
    {
        using var host = Process.GetCurrentProcess(); var epoch = Guid.NewGuid();
        long fence = ScalarLong(c, t, "SELECT coalesce(max(fence),0)+1 FROM scheduler_ownership");
        string sid = OperatingSystem.IsWindows() ? WindowsIdentity.GetCurrent().User!.Value : throw new StorageException(ErrorCode.NOT_OWNER);
        Exec(c, t, "INSERT INTO scheduler_ownership(slot,host_epoch,fence,host_pid,host_start_ticks,sid,lease_until) VALUES(1,$e,$f,$p,$s,$sid,$l) ON CONFLICT(slot) DO UPDATE SET host_epoch=$e,fence=$f,host_pid=$p,host_start_ticks=$s,sid=$sid,lease_until=$l", ("$e", epoch.ToString()), ("$f", fence), ("$p", host.Id), ("$s", host.StartTime.ToUniversalTime().Ticks), ("$sid", sid), ("$l", At(clock.GetUtcNow() + Watchdog.Lease)));
        return new OwnerToken(epoch, fence);
    });
    internal bool RenewOwner(OwnerToken owner) => database.Write((c, t) =>
    {
        using var cmd = Command(c, t, "UPDATE scheduler_ownership SET lease_until=$l WHERE host_epoch=$e AND fence=$f AND lease_until>$now", ("$e", owner.Epoch.ToString()), ("$f", owner.Fence), ("$l", At(clock.GetUtcNow() + Watchdog.Lease)), ("$now", At(clock.GetUtcNow())));
        return cmd.ExecuteNonQuery() == 1;
    });
    private void CheckOwner(SqliteConnection c, SqliteTransaction t, OwnerToken owner, bool dispatch = false)
    {
        if (ScalarLong(c, t, "SELECT count(*) FROM scheduler_ownership WHERE host_epoch=$e AND fence=$f AND lease_until>$now" + (dispatch ? " AND blocked=0" : ""), ("$e", owner.Epoch.ToString()), ("$f", owner.Fence), ("$now", At(clock.GetUtcNow()))) != 1) throw new StorageException(ErrorCode.NOT_OWNER);
    }
    public bool OwnerBlocked => database.Read(c => ScalarLong(c, null, "SELECT count(*) FROM scheduler_ownership WHERE blocked=1") != 0);
    internal IReadOnlyList<OperationId> WaitingOperations() => database.Read(c =>
    {
        using var command = Command(c, null, "SELECT operation_id FROM operations WHERE state IN ('ACCEPTED','QUEUED') ORDER BY accepted_at,operation_id LIMIT 1000");
        using var reader = command.ExecuteReader(); var ids = new List<OperationId>();
        while (reader.Read()) ids.Add(new(Guid.Parse(reader.GetString(0)))); return (IReadOnlyList<OperationId>)ids;
    });
    internal IReadOnlyList<OperationId> ExpiredWaitingOperations(DateTimeOffset now) => database.Read(c =>
    {
        using var command = Command(c, null, "SELECT operation_id,accepted_at,request_json FROM operations WHERE state IN ('ACCEPTED','QUEUED') ORDER BY accepted_at,operation_id LIMIT 1000");
        using var reader = command.ExecuteReader(); var ids = new List<OperationId>();
        while (reader.Read())
        {
            var accepted = DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture);
            if (now - accepted < TimeSpan.FromSeconds(1)) continue;
            // Requests were validated and frozen at admission. Full validation is repeated only if an expired item is about to change state.
            using var request = JsonDocument.Parse((byte[])reader[2], new JsonDocumentOptions { MaxDepth = ContractJson.MaxDepth });
            int seconds = request.RootElement.GetProperty("params").TryGetProperty("queue_timeout_s", out var timeout) ? timeout.GetInt32() : 300;
            if (seconds is < 1 or > 3600) throw new StorageException(ErrorCode.VERIFY_FAILED);
            if (now - accepted >= TimeSpan.FromSeconds(seconds)) ids.Add(new(Guid.Parse(reader.GetString(0))));
        }
        return (IReadOnlyList<OperationId>)ids;
    });
    internal void RegisterWorker(OwnerToken owner, Guid epoch, int pid, long startTicks, string workerId) => database.Write((c, t) =>
    {
        CheckOwner(c, t, owner, true);
        Exec(c, t, "INSERT INTO worker_processes VALUES($e,$h,$p,$s,$w,$at)", ("$e", epoch.ToString()), ("$h", owner.Epoch.ToString()), ("$p", pid), ("$s", startTicks), ("$w", workerId), ("$at", At(clock.GetUtcNow()))); return 0;
    });
    public StoredOperation Cancel(OperationId id) => database.Write((c, t) => CancelCore(c, t, id), checkpoints: true);
    private StoredOperation CancelCore(SqliteConnection c, SqliteTransaction t, OperationId id)
    {
        var previous = ReadStored(c, t, id); if (OperationLifecycle.IsTerminal(previous.Report.State) || previous.Report.CancelRequested) return previous;
        var report = previous.Report with { CancelRequested = true };
        if (report.State is OperationState.ACCEPTED or OperationState.QUEUED) report = report with { State = OperationState.CANCELLED, Phase = "CANCELLED_BEFORE_DISPATCH", Outcome = OperationOutcome.NOT_STARTED, CompletedAt = clock.GetUtcNow(), PollAfterMs = 0 };
        return Save(c, t, previous, report);
    }
    private StoredOperation Save(SqliteConnection c, SqliteTransaction t, StoredOperation previous, OperationReport report, Guid? claim = null)
    {
        byte[] bytes = ContractJson.WriteReport(report);
        using var proof = new ArtifactProof();
        return RecordCore(c, t, report.OperationId, previous.Revision, report, bytes, proof, claim);
    }
    internal AttemptBinding? PrepareDispatch(OwnerToken owner, OperationId id, Guid workerEpoch, ExecutionSpec spec, DateTimeOffset deadline, int attempt = 1) => database.Write<AttemptBinding?>((c, t) =>
    {
        CheckOwner(c, t, owner, true); var previous = ReadStored(c, t, id);
        if (OperationLifecycle.IsTerminal(previous.Report.State) || previous.Report.CancelRequested) return null;
        var request = ContractJson.ReadRequest(previous.RequestJson); bool mutation = RetryPolicy.IsMutation(request.Identity.Method);
        if (request.Identity.Project is { } project && ScalarLong(c, t, "SELECT count(*) FROM quarantines WHERE project_key=$p", ("$p", project.ToString())) != 0) return null;
        if (ScalarLong(c, t, "SELECT count(*) FROM dispatch_attempts WHERE disposition='POSSIBLY_SENT'") != 0) throw new StorageException(ErrorCode.CONFLICT);
        if (ScalarLong(c, t, "SELECT count(*) FROM worker_processes WHERE worker_epoch=$e AND host_epoch=$h", ("$e", workerEpoch.ToString()), ("$h", owner.Epoch.ToString())) != 1) throw new StorageException(ErrorCode.NOT_OWNER);
        if (attempt == 1)
        {
            if (previous.Report.State == OperationState.ACCEPTED) previous = Save(c, t, previous, previous.Report with { State = OperationState.QUEUED, Phase = "QUEUED" });
            if (previous.Report.State != OperationState.QUEUED || (string?)Scalar(c, t, "SELECT state FROM outbox WHERE operation_id=$id", ("$id", id.ToString())) != "PENDING") return null;
            Guid claim = Guid.NewGuid();
            Exec(c, t, "UPDATE outbox SET state='CLAIMED',claim_id=$claim WHERE operation_id=$id", ("$id", id.ToString()), ("$claim", claim.ToString("D")));
            Exec(c, t, "UPDATE outbox SET state='DISPATCH_RECORDED' WHERE operation_id=$id", ("$id", id.ToString()));
            previous = Save(c, t, previous, previous.Report with { State = OperationState.RUNNING, Phase = "DISPATCHING", StartedAt = clock.GetUtcNow(), DeadlineAt = deadline, SessionEpoch = new SessionEpoch(workerEpoch) }, claim);
        }
        else
        {
            if (attempt is < 2 or > 3 || mutation || !spec.QualifiedEffectFreeRead || previous.Report.State != OperationState.RUNNING
                || ScalarLong(c, t, "SELECT count(*) FROM dispatch_attempts WHERE operation_id=$id AND attempt=$a AND disposition='REJECTED_NO_EFFECT'", ("$id", id.ToString()), ("$a", attempt - 1)) != 1) throw new StorageException(ErrorCode.CONFLICT);
            previous = Save(c, t, previous, previous.Report with { Phase = "RETRY_DISPATCH" });
        }
        string? plan = request.Parameters is SyncApplyParameters apply ? apply.ExpectedPlanSha256 : null;
        Exec(c, t, "INSERT INTO dispatch_attempts(operation_id,attempt,host_epoch,worker_epoch,fence,portal_key,capability,mutation,input_hash,plan_hash,deadline_at) VALUES($id,$a,$h,$w,$f,$p,$cap,$m,$hash,$plan,$d)", ("$id", id.ToString()), ("$a", attempt), ("$h", owner.Epoch.ToString()), ("$w", workerEpoch.ToString()), ("$f", owner.Fence), ("$p", spec.PortalKey), ("$cap", spec.Capability), ("$m", mutation ? 1 : 0), ("$hash", previous.BodySha256), ("$plan", plan), ("$d", At(deadline)));
        Exec(c, t, "INSERT INTO steps(step_id,operation_id,ordinal,intent_json) VALUES($step,$id,$a,$intent)", ("$step", id + ":attempt:" + attempt), ("$id", id.ToString()), ("$a", attempt), ("$intent", JsonSerializer.SerializeToUtf8Bytes(new { workerEpoch, owner.Fence, previous.BodySha256, plan })));
        if (request.Identity.Project is { } key)
            Exec(c, t, "INSERT INTO leases VALUES($l,$p,NULL,$meta)", ("$l", id + ":" + attempt), ("$p", key.ToString()), ("$meta", JsonSerializer.SerializeToUtf8Bytes(new { owner.Epoch, owner.Fence, workerEpoch, deadline })));
        return new(id, attempt, workerEpoch, owner.Fence, previous.BodySha256, plan, mutation, deadline);
    }, checkpoints: true);

    internal void RecordReceipt(OwnerToken owner, AttemptBinding binding, WireMessage message, string disposition) => database.Write((c, t) =>
    {
        CheckOwner(c, t, owner); byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(message, Protocol.Json);
        Exec(c, t, "INSERT INTO worker_receipts(operation_id,worker_epoch,fence,attempt,received_at,message_json,sha256,disposition) VALUES($id,$e,$f,$a,$at,$b,$sha,$d)", ("$id", binding.OperationId.ToString()), ("$e", message.WorkerEpoch.ToString()), ("$f", message.Fence), ("$a", binding.Attempt), ("$at", At(clock.GetUtcNow())), ("$b", bytes), ("$sha", Hash(bytes)), ("$d", disposition));
        if (disposition == "REJECTED_NO_EFFECT" || disposition == "QUIESCENT")
            Exec(c, t, "UPDATE dispatch_attempts SET disposition=$d WHERE operation_id=$id AND attempt=$a", ("$d", disposition), ("$id", binding.OperationId.ToString()), ("$a", binding.Attempt));
        return 0;
    });
    internal void Complete(OwnerToken owner, AttemptBinding binding, OperationReport report)
    {
        byte[] bytes = ContractJson.WriteReport(report); using var proof = VerifyArtifacts(bytes);
        database.Write((c, t) =>
        {
            CheckOwner(c, t, owner); var previous = ReadStored(c, t, binding.OperationId);
            if (ScalarLong(c, t, "SELECT count(*) FROM dispatch_attempts WHERE operation_id=$id AND attempt=$a AND worker_epoch=$e AND fence=$f AND host_epoch=$h", ("$id", binding.OperationId.ToString()), ("$a", binding.Attempt), ("$e", binding.WorkerEpoch.ToString()), ("$f", owner.Fence), ("$h", owner.Epoch.ToString())) != 1) throw new StorageException(ErrorCode.NOT_OWNER);
            var result = RecordCore(c, t, binding.OperationId, previous.Revision, report with { CancelRequested = previous.Report.CancelRequested }, bytes: ContractJson.WriteReport(report with { CancelRequested = previous.Report.CancelRequested }), proof);
            if (report.State == OperationState.RECONCILIATION_REQUIRED)
            {
                Exec(c, t, "UPDATE quarantines SET worker_epoch=$e,fence=$f WHERE operation_id=$id", ("$e", binding.WorkerEpoch.ToString()), ("$f", binding.Fence), ("$id", binding.OperationId.ToString()));
                Exec(c, t, "UPDATE scheduler_ownership SET blocked=1 WHERE slot=1");
            }
            return result;
        }, checkpoints: true);
    }
    public int ReceiptCount(OperationId id) => database.Read(c => checked((int)ScalarLong(c, null, "SELECT count(*) FROM worker_receipts WHERE operation_id=$id", ("$id", id.ToString()))));
    public int AttemptCount(OperationId id) => database.Read(c => checked((int)ScalarLong(c, null, "SELECT count(*) FROM dispatch_attempts WHERE operation_id=$id", ("$id", id.ToString()))));
    internal void ConfirmReadProcessExit(OwnerToken owner, AttemptBinding binding, int pid, long startTicks) => database.Write((c, t) =>
    {
        CheckOwner(c, t, owner);
        if (binding.Mutation || ScalarLong(c, t, "SELECT count(*) FROM worker_processes WHERE worker_epoch=$e AND pid=$p AND start_ticks=$s", ("$e", binding.WorkerEpoch.ToString()), ("$p", pid), ("$s", startTicks)) != 1) throw new StorageException(ErrorCode.IDENTITY_MISMATCH);
        try { using var process = Process.GetProcessById(pid); if (process.StartTime.ToUniversalTime().Ticks == startTicks && !process.HasExited) throw new StorageException(ErrorCode.CONFLICT); }
        catch (ArgumentException) { }
        catch (InvalidOperationException) { }
        Exec(c, t, "UPDATE dispatch_attempts SET disposition='QUIESCENT_PROCESS_EXIT' WHERE operation_id=$id AND attempt=$a AND mutation=0", ("$id", binding.OperationId.ToString()), ("$a", binding.Attempt)); return 0;
    });
    internal void Recover(OwnerToken owner) => database.Write((c, t) =>
    {
        CheckOwner(c, t, owner); var ids = new List<OperationId>();
        using (var command = Command(c, t, "SELECT operation_id FROM operations WHERE state='RUNNING' OR operation_id IN (SELECT operation_id FROM outbox WHERE state IN ('CLAIMED','DISPATCH_RECORDED'))"))
        using (var reader = command.ExecuteReader()) while (reader.Read()) ids.Add(new(Guid.Parse(reader.GetString(0))));
        foreach (var id in ids)
        {
            var previous = ReadStored(c, t, id); if (OperationLifecycle.IsTerminal(previous.Report.State)) continue;
            bool mutation = previous.Report.State == OperationState.RUNNING && RetryPolicy.IsMutation(previous.Report.Method);
            var report = Failure(previous.Report, mutation, ErrorCode.WORKER_UNAVAILABLE, "HOST_RESTART");
            Save(c, t, previous, report);
            if (mutation)
            {
                Exec(c, t, "UPDATE quarantines SET worker_epoch=(SELECT worker_epoch FROM dispatch_attempts WHERE operation_id=$id ORDER BY attempt DESC LIMIT 1),fence=(SELECT fence FROM dispatch_attempts WHERE operation_id=$id ORDER BY attempt DESC LIMIT 1) WHERE operation_id=$id", ("$id", id.ToString()));
                Exec(c, t, "UPDATE scheduler_ownership SET blocked=1 WHERE slot=1");
            }
        }
        // An old own process may still be calling. Observe identity; never adopt, kill or infer quiescence from a lease.
        using var workers = Command(c, t, "SELECT pid,start_ticks FROM worker_processes WHERE host_epoch!=$h", ("$h", owner.Epoch.ToString()));
        using var rows = workers.ExecuteReader(); bool alive = false;
        while (rows.Read())
        {
            try { using var process = Process.GetProcessById(rows.GetInt32(0)); alive |= process.StartTime.ToUniversalTime().Ticks == rows.GetInt64(1) && !process.HasExited; }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { alive = true; }
        }
        rows.Dispose();
        if (alive) Exec(c, t, "UPDATE scheduler_ownership SET blocked=1 WHERE slot=1");
        else if (ScalarLong(c, t, "SELECT count(*) FROM quarantines") == 0 && ScalarLong(c, t, "SELECT count(*) FROM operations WHERE state='RECONCILIATION_REQUIRED'") == 0)
        {
            // Only effect-free historical attempts whose processes are independently absent can release this barrier.
            Exec(c, t, "UPDATE dispatch_attempts SET disposition='QUIESCENT_PROCESS_EXIT' WHERE mutation=0 AND disposition='POSSIBLY_SENT' AND host_epoch!=$h", ("$h", owner.Epoch.ToString()));
            Exec(c, t, "UPDATE scheduler_ownership SET blocked=0 WHERE slot=1");
        }
        return 0;
    }, checkpoints: true);
    internal OperationReport Failure(OperationReport previous, bool unknown, ErrorCode code, string phase)
    {
        var report = previous with
        {
            State = unknown ? OperationState.RECONCILIATION_REQUIRED : OperationState.FAILED,
            Phase = phase,
            Outcome = unknown ? OperationOutcome.UNKNOWN : OperationOutcome.NOT_STARTED,
            PollAfterMs = 0,
            CompletedAt = clock.GetUtcNow(),
            Result = null,
            Error = new(code, ErrorCategory.EXECUTION, "Executor did not confirm completion.", false, unknown ? OperationOutcome.UNKNOWN : OperationOutcome.NOT_STARTED)
        };
        // A worker-specific diagnostic is retained in the receipt. The public method envelope has a narrower code set.
        try { _ = ContractJson.WriteReport(report); }
        catch (ContractValidationException) { report = report with { Error = report.Error! with { Code = ErrorCode.WORKER_UNAVAILABLE } }; }
        return report;
    }
    public string? ReadBreaker(string portal, string capability) => database.Read(c => Scalar(c, null, "SELECT state_json FROM circuit_breakers WHERE portal_key=$p AND capability=$c", ("$p", portal), ("$c", capability)) as string);
    internal void WriteBreaker(OwnerToken owner, string portal, string capability, string json) => database.Write((c, t) =>
    { CheckOwner(c, t, owner); Exec(c, t, "INSERT INTO circuit_breakers VALUES($p,$c,$j) ON CONFLICT(portal_key,capability) DO UPDATE SET state_json=$j", ("$p", portal), ("$c", capability), ("$j", json)); return 0; });
    private static string At(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}
