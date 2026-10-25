using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TiaBridge.Core.Identity;
using TiaBridge.Core.Operations;
using TiaBridge.Core.Transport.V1;
using TiaBridge.Host.Storage;
using static TiaBridge.Host.Storage.SQLite.SqliteDatabase;

namespace TiaBridge.Host.Operations;

public sealed record OperationMetadata(byte[] ReportJson, DateTimeOffset UpdatedAt, bool Quarantined);

public sealed partial class SqliteOperationStore
{
    /// <summary>Host lifetime handle prevents a last-connection WAL checkpoint after every short request. No shared command/transaction.</summary>
    public IDisposable KeepDatabaseOpen() => database.KeepOpen();
    public IDisposable OpenMetadataReaders() => database.OpenMetadataReaders();
    private static void Authorize(SqliteConnection c, SqliteTransaction? t, OperationId id, string principal)
    {
        string? owner = Scalar(c, t, "SELECT principal FROM operations WHERE operation_id=$id", ("$id", id.ToString())) as string;
        if (owner is null) throw new StorageException(ErrorCode.NOT_FOUND);
        if (owner != principal) throw new StorageException(ErrorCode.ACCESS_DENIED);
    }
    public void AuthorizeOperation(OperationId id, string principal) => database.Read(c => { Authorize(c, null, id, principal); return 0; });
    /// <summary>Only authenticated journal metadata: no request revalidation, artifact IO or worker lock.</summary>
    public OperationMetadata Metadata(OperationId id, string principal) => database.ReadMetadata(c =>
    {
        using var command = Command(c, null, "SELECT r.principal,r.report_json,r.report_sha256,e.occurred_at,EXISTS(SELECT 1 FROM quarantines q WHERE q.project_key=r.project_key) FROM operations r JOIN events e ON e.operation_id=r.operation_id AND e.event_sequence=r.last_sequence AND e.report_sha256=r.report_sha256 WHERE r.operation_id=$id", ("$id", id.ToString()));
        using var reader = command.ExecuteReader(); if (!reader.Read()) throw new StorageException(ErrorCode.NOT_FOUND);
        if (reader.GetString(0) != principal) throw new StorageException(ErrorCode.ACCESS_DENIED);
        byte[] bytes = (byte[])reader[1]; if (Hash(bytes) != reader.GetString(2)) throw new StorageException(ErrorCode.VERIFY_FAILED);
        return new OperationMetadata(bytes, DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture), reader.GetInt64(4) != 0);
    });
    public StoredOperation CancelAuthorized(OperationId id, string principal) => database.Write((c, t) =>
    { Authorize(c, t, id, principal); return CancelCore(c, t, id); }, checkpoints: true);
    public OperationCancelResult CancelAuthorizedProjection(OperationId id, string principal) => database.Write((c, t) => CancelResult(c, t, id, principal), checkpoints: true);
    public int ActiveCount => database.ReadMetadata(c => checked((int)ScalarLong(c, null, "SELECT count(*) FROM operations WHERE state IN ('ACCEPTED','QUEUED','RUNNING')")));
    public bool IsQuarantined(ProjectKey? project) => database.ReadMetadata(c => project is null
        ? ScalarLong(c, null, "SELECT count(*) FROM quarantines") > 0
        : ScalarLong(c, null, "SELECT count(*) FROM quarantines WHERE project_key=$p", ("$p", project.ToString())) > 0);
    public static bool IsMetadataMethod(OperationMethod method) => method is OperationMethod.OperationStatus or OperationMethod.OperationResult or OperationMethod.OperationEvents or OperationMethod.OperationCancel or OperationMethod.DiagnosticsHealth;

    /// <summary>Fast POST methods still use the same idempotency ledger, journal and atomic outbox. No worker is called.</summary>
    public Admission AcceptMetadata(string principal, ReadOnlyMemory<byte> json, DiagnosticsHealthResult health) => AcceptInternal(principal, json, (c, t, stored, request) =>
    {
        if (!IsMetadataMethod(request.Identity.Method)) throw new StorageException(ErrorCode.UNSUPPORTED);
        OperationResult result = request.Parameters switch
        {
            DiagnosticsHealthParameters => health,
            OperationStatusParameters status => StatusCore(c, t, status.OperationId, principal),
            OperationResultParameters target => ResultCore(c, t, target.OperationId, principal),
            OperationEventsParameters events => EventsCore(c, t, events.OperationId, principal, events.AfterSequence ?? 0, checked((int)(events.Limit ?? 100))),
            OperationCancelParameters cancel => CancelResult(c, t, cancel.OperationId, principal),
            _ => throw new StorageException(ErrorCode.UNSUPPORTED)
        };
        stored = Save(c, t, stored, stored.Report with { State = OperationState.QUEUED, Phase = "METADATA" });
        Guid claim = Guid.NewGuid();
        Exec(c, t, "UPDATE outbox SET state='CLAIMED',claim_id=$c WHERE operation_id=$id", ("$c", claim.ToString("D")), ("$id", stored.Report.OperationId.ToString()));
        Exec(c, t, "UPDATE outbox SET state='DISPATCH_RECORDED' WHERE operation_id=$id", ("$id", stored.Report.OperationId.ToString()));
        stored = Save(c, t, stored, stored.Report with { State = OperationState.RUNNING, StartedAt = clock.GetUtcNow(), Phase = "METADATA" }, claim);
        return Save(c, t, stored, stored.Report with { State = OperationState.SUCCEEDED, Outcome = OperationOutcome.CONFIRMED, Result = result, Phase = "COMPLETED", CompletedAt = clock.GetUtcNow(), PollAfterMs = 0 });
    });
    private OperationStatusResult StatusCore(SqliteConnection c, SqliteTransaction t, OperationId id, string principal)
    {
        Authorize(c, t, id, principal); var r = ReadStored(c, t, id).Report;
        return new(id, Enum.Parse<OperationStatusResult_State>(r.State.ToString()), r.Phase, Enum.Parse<OperationStatusResult_Outcome>(r.Outcome.ToString()), r.CancelRequested);
    }
    private OperationResultResult ResultCore(SqliteConnection c, SqliteTransaction t, OperationId id, string principal)
    {
        Authorize(c, t, id, principal); var r = ReadStored(c, t, id).Report;
        return new(id, Enum.Parse<OperationResultResult_State>(r.State.ToString()), Enum.Parse<OperationResultResult_Outcome>(r.Outcome.ToString()), r.ArtifactRefs);
    }
    private OperationCancelResult CancelResult(SqliteConnection c, SqliteTransaction t, OperationId id, string principal)
    {
        Authorize(c, t, id, principal); var before = ReadStored(c, t, id).Report; var after = CancelCore(c, t, id).Report;
        return CancelProjection(before.State, after);
    }
    public static OperationCancelResult CancelProjection(OperationState before, OperationReport after) => new(after.OperationId,
        OperationLifecycle.IsTerminal(before) ? OperationCancelResult_Disposition.ALREADY_TERMINAL : after.State == OperationState.CANCELLED ? OperationCancelResult_Disposition.CANCELLED : OperationCancelResult_Disposition.CANCEL_REQUESTED,
        Enum.Parse<OperationCancelResult_State>(after.State.ToString()));
    public OperationEventsResult JournalProjection(OperationId id, string principal, long after = 0, int limit = 100) => database.Read(c => EventsCore(c, null, id, principal, after, limit));
    private OperationEventsResult EventsCore(SqliteConnection c, SqliteTransaction? t, OperationId id, string principal, long after, int limit)
    {
        if (after < 0 || limit is < 1 or > 1000) throw new StorageException(ErrorCode.CURSOR_INVALID);
        Authorize(c, t, id, principal);
        using var command = Command(c, t, "SELECT event_sequence,occurred_at,report_json,report_sha256 FROM events WHERE operation_id=$id AND event_sequence>$a ORDER BY event_sequence LIMIT $l", ("$id", id.ToString()), ("$a", after), ("$l", limit));
        using var reader = command.ExecuteReader(); var events = ImmutableArray.CreateBuilder<OperationEvent>(); long next = after;
        while (reader.Read())
        {
            byte[] bytes = (byte[])reader[2]; if (Hash(bytes) != reader.GetString(3)) throw new StorageException(ErrorCode.VERIFY_FAILED);
            using var report = JsonDocument.Parse(bytes); var root = report.RootElement; next = reader.GetInt64(0);
            var evidence = root.GetProperty("artifact_refs").EnumerateArray().Select(a => a.GetProperty("artifact_id").GetString()!).ToList();
            if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object && error.TryGetProperty("evidence_refs", out var refs))
                evidence.AddRange(refs.EnumerateArray().Select(e => e.GetString()!));
            events.Add(new(next, id, Enum.Parse<OperationEvent_State>(root.GetProperty("state").GetString()!), root.GetProperty("phase").GetString()!, DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture), evidence.Distinct(StringComparer.Ordinal).ToImmutableArray()));
        }
        return new(id, events.ToImmutable(), next);
    }
}
