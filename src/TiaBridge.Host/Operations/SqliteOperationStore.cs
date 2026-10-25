using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TiaBridge.Core.Identity;
using TiaBridge.Core.Operations;
using TiaBridge.Core.Transport.V1;
using TiaBridge.Host.Storage;
using TiaBridge.Host.Storage.SQLite;
using static TiaBridge.Host.Storage.SQLite.SqliteDatabase;

namespace TiaBridge.Host.Operations;

/// <summary>Durable metadata only: no scheduler, API server, grant evaluation or executor.</summary>
public sealed partial class SqliteOperationStore : IOperationRepository, IOutboxRepository
{
    private readonly SqliteDatabase database;
    private readonly TimeProvider clock;
    public IArtifactStore Artifacts { get; }
    public SqliteOperationStore(StorageLocation location, IStorageFaults? faults = null, TimeProvider? clock = null)
        : this(location, faults ?? NoStorageFaults.Instance, clock ?? TimeProvider.System, SchemaVersion) { }
    internal SqliteOperationStore(StorageLocation location, IStorageFaults faults, TimeProvider clock, int targetVersion)
    {
        database = new SqliteDatabase(location, faults, targetVersion); this.clock = clock;
        Artifacts = new Storage.Artifacts.FileArtifactStore(database, clock);
    }
    public void RegisterProject(ProjectKey project)
    {
        ArgumentNullException.ThrowIfNull(project);
        database.Write((c, t) => { Exec(c, t, "INSERT INTO projects VALUES($id,$at) ON CONFLICT(project_key) DO NOTHING", ("$id", project.ToString()), ("$at", clock.GetUtcNow().ToString("O"))); return 0; });
    }

    public const int QueueCapacity = 1000;
    public Admission Accept(string authenticatedPrincipal, ReadOnlyMemory<byte> requestJson) => AcceptInternal(authenticatedPrincipal, requestJson, null);
    public Admission AcceptSupported(string authenticatedPrincipal, ReadOnlyMemory<byte> requestJson, Func<OperationMethod, bool> supports)
        => AcceptInternal(authenticatedPrincipal, requestJson, null, supports);
    private Admission AcceptInternal(string authenticatedPrincipal, ReadOnlyMemory<byte> requestJson,
        Func<SqliteConnection, SqliteTransaction, StoredOperation, ValidatedRequest, StoredOperation>? complete, Func<OperationMethod, bool>? supports = null)
    {
        ValidatePrincipal(authenticatedPrincipal);
        // Snapshot and validate before entering the short SQLite transaction. No caller-owned DTO is trusted.
        if (requestJson.Length > ContractJson.MaxRequestBytes) _ = ContractJson.ReadRequest(requestJson);
        var request = ContractJson.ReadRequest(requestJson.ToArray());
        byte[] wire = ContractJson.WriteRequest(request.Identity.Method, request.Identity.Project, request.Identity.IdempotencyKey, request.Parameters);
        string? project = request.Identity.Project?.ToString();
        string key = request.Identity.IdempotencyKey.Value;
        var admission = database.Write((c, t) =>
        {
            string? existing = Scalar(c, t, "SELECT operation_id FROM idempotency_keys WHERE principal=$p AND project_scope=$s AND idempotency_key=$k", ("$p", authenticatedPrincipal), ("$s", project ?? ""), ("$k", key)) as string;
            if (existing is not null)
            {
                var stored = ReadStored(c, t, new OperationId(Guid.Parse(existing)));
                var previous = ContractJson.ReadRequest(stored.RequestJson);
                if (new RequestIdentity(authenticatedPrincipal, request.Identity).Compare(new RequestIdentity(authenticatedPrincipal, previous.Identity)) != IdempotencyComparison.SameRequest
                    || !request.Identity.GetCanonicalBody().AsSpan().SequenceEqual(previous.Identity.GetCanonicalBody())) throw new StorageException(ErrorCode.IDEMPOTENCY_MISMATCH);
                return new Admission(stored, true);
            }
            if (supports is not null && !supports(request.Identity.Method)) throw new StorageException(ErrorCode.UNSUPPORTED);
            if (complete is null && ScalarLong(c, t, "SELECT count(*) FROM operations WHERE state IN ('ACCEPTED','QUEUED','RUNNING')") >= QueueCapacity) throw new StorageException(ErrorCode.QUEUE_FULL);
            if (project is not null && ScalarLong(c, t, "SELECT count(*) FROM projects WHERE project_key=$p", ("$p", project)) != 1) throw new StorageException(ErrorCode.IDENTITY_MISMATCH);
            var id = new OperationId(Guid.NewGuid()); var acceptedAt = clock.GetUtcNow();
            var report = new OperationReport("1.0", id, request.Identity.Method, OperationState.ACCEPTED, "ACCEPTED", OperationOutcome.NOT_STARTED, false, 1000, ImmutableArray<ArtifactRef>.Empty, AcceptedAt: acceptedAt);
            byte[] bytes = ContractJson.WriteReport(report); string hash = Hash(bytes);
            Exec(c, t, "INSERT INTO operations(operation_id,principal,project_key,method,request_json,canonical_body,body_sha256,accepted_at,state,outcome,revision,report_json,report_sha256,last_sequence) VALUES($id,$p,$project,$m,$wire,$body,$sha,$at,'ACCEPTED','NOT_STARTED',0,$report,$rh,1)",
                ("$id", id.ToString()), ("$p", authenticatedPrincipal), ("$project", project), ("$m", OperationCatalog.Name(request.Identity.Method)), ("$wire", wire), ("$body", request.Identity.GetCanonicalBody()), ("$sha", request.Identity.BodySha256), ("$at", acceptedAt.ToString("O")), ("$report", bytes), ("$rh", hash));
            database.Faults.Checkpoint(StorageFaultPoint.OperationInserted);
            Exec(c, t, "INSERT INTO idempotency_keys(principal,project_key,idempotency_key,operation_id) VALUES($p,$project,$k,$id)", ("$p", authenticatedPrincipal), ("$project", project), ("$k", key), ("$id", id.ToString()));
            InsertEvent(c, t, report, 1, bytes);
            Exec(c, t, "INSERT INTO outbox(operation_id,state) VALUES($id,'PENDING')", ("$id", id.ToString()));
            database.Faults.Checkpoint(StorageFaultPoint.OutboxInserted);
            var accepted = new StoredOperation(report, 0, wire, request.Identity.BodySha256);
            if (complete is not null) accepted = complete(c, t, accepted, request);
            return new Admission(accepted, false);
        }, checkpoints: true);
        if (admission.AlreadyAccepted)
        {
            using var proof = VerifyArtifacts(ContractJson.WriteReport(admission.Operation.Report));
        }
        return admission;
    }

    public StoredOperation Read(OperationId operationId)
    {
        var stored = database.Read(c => ReadStored(c, null, operationId));
        using var proof = VerifyArtifacts(ContractJson.WriteReport(stored.Report));
        return stored;
    }
    public OperationReport? ReadResult(OperationId operationId)
    {
        var stored = Read(operationId);
        return OperationLifecycle.IsTerminal(stored.Report.State) ? stored.Report : null;
    }

    /// <summary>Journal status only; never hashes artifacts or probes an executor/TIA inline.</summary>
    public OperationStatusResult ReadStatus(OperationId operationId)
    {
        var report = database.Read(c => ReadStored(c, null, operationId)).Report;
        return new OperationStatusResult(operationId, Enum.Parse<OperationStatusResult_State>(report.State.ToString()), report.Phase,
            Enum.Parse<OperationStatusResult_Outcome>(report.Outcome.ToString()), report.CancelRequested);
    }

    public StoredOperation Record(OperationId operationId, long expectedRevision, OperationReport report, Guid? claimId = null)
    {
        byte[] bytes = ContractJson.WriteReport(report);
        report = ContractJson.ReadReport(bytes);
        if (report.OperationId != operationId) throw new StorageException(ErrorCode.IDENTITY_MISMATCH);
        using var proof = VerifyArtifacts(bytes); // Hashing is outside the transaction; handles deny write/delete through commit on Windows.
        return database.Write((c, t) => RecordCore(c, t, operationId, expectedRevision, report, bytes, proof, claimId), checkpoints: true);
    }

    private StoredOperation RecordCore(SqliteConnection c, SqliteTransaction t, OperationId operationId, long expectedRevision,
        OperationReport report, byte[] bytes, ArtifactProof proof, Guid? claimId = null)
    {
        var previous = ReadStored(c, t, operationId);
        if (previous.Revision != expectedRevision || OperationLifecycle.IsTerminal(previous.Report.State)) throw new StorageException(ErrorCode.CONFLICT);
        if (report.Method != previous.Report.Method || report.AcceptedAt != previous.Report.AcceptedAt) throw new StorageException(ErrorCode.IDENTITY_MISMATCH);
        if (report.State != previous.Report.State) OperationLifecycle.ValidateTransition(previous.Report.State, report.State, report.Outcome);
        else OperationLifecycle.Validate(report.State, report.Outcome);
        if (report.State == OperationState.RUNNING && previous.Report.State != OperationState.RUNNING)
        {
            string? claim = Scalar(c, t, "SELECT claim_id FROM outbox WHERE operation_id=$id AND state='DISPATCH_RECORDED'", ("$id", operationId.ToString())) as string;
            if (claimId is null || claim != claimId.Value.ToString("D")) throw new StorageException(ErrorCode.CONFLICT);
        }
        if (report.State == OperationState.SUCCEEDED && report.Result is null) throw new StorageException(ErrorCode.VALIDATION_FAILED);
        if (OperationLifecycle.IsTerminal(report.State))
        {
            Exec(c, t, "INSERT INTO operation_results VALUES($id,$report,$sha)", ("$id", operationId.ToString()), ("$report", bytes), ("$sha", Hash(bytes)));
            Exec(c, t, "UPDATE outbox SET state='CLOSED' WHERE operation_id=$id AND state!='CLOSED'", ("$id", operationId.ToString()));
        }
        string? project = Scalar(c, t, "SELECT project_key FROM operations WHERE operation_id=$id", ("$id", operationId.ToString())) as string;
        if (report.State == OperationState.RECONCILIATION_REQUIRED && project is not null)
            Exec(c, t, "INSERT INTO quarantines(project_key,operation_id,reason,created_at) VALUES($p,$id,'UNKNOWN_OUTCOME',$at) ON CONFLICT(project_key) DO NOTHING", ("$p", project), ("$id", operationId.ToString()), ("$at", clock.GetUtcNow().ToString("O")));
        long sequence = previous.Revision + 2;
        Exec(c, t, "UPDATE operations SET state=$state,outcome=$outcome,revision=revision+1,report_json=$report,report_sha256=$sha,last_sequence=$seq WHERE operation_id=$id AND revision=$rev", ("$state", report.State.ToString()), ("$outcome", report.Outcome.ToString()), ("$report", bytes), ("$sha", Hash(bytes)), ("$seq", sequence), ("$id", operationId.ToString()), ("$rev", expectedRevision));
        InsertEvent(c, t, report, sequence, bytes);
        foreach (var artifact in proof.Artifacts) Exec(c, t, "INSERT INTO operation_artifacts VALUES($id,$a) ON CONFLICT DO NOTHING", ("$id", operationId.ToString()), ("$a", artifact.Descriptor.ArtifactId));
        return new StoredOperation(report, previous.Revision + 1, previous.RequestJson, previous.BodySha256);
    }

    public IReadOnlyList<JournalEvent> Events(OperationId operationId, long afterSequence = 0, int limit = 100)
    {
        if (afterSequence < 0 || limit is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(limit));
        return database.Read(c =>
        {
            _ = ReadStored(c, null, operationId);
            using var command = Command(c, null, "SELECT event_sequence,occurred_at,report_json,report_sha256 FROM events WHERE operation_id=$id AND event_sequence>$after ORDER BY event_sequence LIMIT $limit", ("$id", operationId.ToString()), ("$after", afterSequence), ("$limit", limit));
            using var reader = command.ExecuteReader(); var events = new List<JournalEvent>();
            while (reader.Read())
            {
                byte[] bytes = (byte[])reader[2]; if (Hash(bytes) != reader.GetString(3)) throw new StorageException(ErrorCode.VERIFY_FAILED);
                events.Add(new(operationId, reader.GetInt64(0), DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture), ContractJson.ReadReport(bytes)));
            }
            return (IReadOnlyList<JournalEvent>)events;
        });
    }

    public IReadOnlyList<OutboxEntry> Pending(int limit = 100)
    {
        if (limit is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(limit));
        return database.Read(c =>
        {
            using var command = Command(c, null, "SELECT o.operation_id FROM outbox o JOIN operations r USING(operation_id) WHERE o.state='PENDING' AND r.state IN ('ACCEPTED','QUEUED') AND NOT EXISTS(SELECT 1 FROM quarantines q WHERE q.project_key=r.project_key) ORDER BY r.accepted_at,o.operation_id LIMIT $limit", ("$limit", limit));
            using var reader = command.ExecuteReader(); var result = new List<OutboxEntry>();
            while (reader.Read()) result.Add(new(new OperationId(Guid.Parse(reader.GetString(0))), OutboxState.PENDING, null));
            return (IReadOnlyList<OutboxEntry>)result;
        });
    }
    public Guid? TryClaim(OperationId operationId) => database.Write<Guid?>((c, t) =>
    {
        Guid claim = Guid.NewGuid();
        using var command = Command(c, t, "UPDATE outbox SET state='CLAIMED',claim_id=$claim WHERE operation_id=$id AND state='PENDING' AND EXISTS(SELECT 1 FROM operations r WHERE r.operation_id=$id AND r.state IN ('ACCEPTED','QUEUED') AND NOT EXISTS(SELECT 1 FROM quarantines q WHERE q.project_key=r.project_key))", ("$id", operationId.ToString()), ("$claim", claim.ToString("D")));
        return command.ExecuteNonQuery() == 1 ? claim : null;
    });
    public bool RecordDispatch(OperationId operationId, Guid claimId) => database.Write((c, t) =>
    {
        using var command = Command(c, t, "UPDATE outbox SET state='DISPATCH_RECORDED' WHERE operation_id=$id AND claim_id=$claim AND state='CLAIMED' AND EXISTS(SELECT 1 FROM operations r WHERE r.operation_id=$id AND r.state IN ('ACCEPTED','QUEUED') AND NOT EXISTS(SELECT 1 FROM quarantines q WHERE q.project_key=r.project_key))", ("$id", operationId.ToString()), ("$claim", claimId.ToString("D")));
        return command.ExecuteNonQuery() == 1;
    });

    private void InsertEvent(SqliteConnection c, SqliteTransaction t, OperationReport report, long sequence, byte[] bytes)
    {
        Exec(c, t, "INSERT INTO events VALUES($id,$seq,$at,$report,$sha)", ("$id", report.OperationId.ToString()), ("$seq", sequence), ("$at", clock.GetUtcNow().ToString("O")), ("$report", bytes), ("$sha", Hash(bytes)));
        database.Faults.Checkpoint(StorageFaultPoint.EventInserted);
    }
    private static StoredOperation ReadStored(SqliteConnection c, SqliteTransaction? t, OperationId id)
    {
        using var command = Command(c, t, "SELECT request_json,body_sha256,canonical_body,report_json,report_sha256,revision,state,outcome,method FROM operations WHERE operation_id=$id", ("$id", id.ToString()));
        using var reader = command.ExecuteReader(); if (!reader.Read()) throw new StorageException(ErrorCode.NOT_FOUND);
        byte[] request = (byte[])reader[0], body = (byte[])reader[2], bytes = (byte[])reader[3];
        var parsed = ContractJson.ReadRequest(request); var report = ContractJson.ReadReport(bytes);
        if (Hash(body) != reader.GetString(1) || Hash(bytes) != reader.GetString(4) || parsed.Identity.BodySha256 != reader.GetString(1)
            || !parsed.Identity.GetCanonicalBody().AsSpan().SequenceEqual(body) || report.OperationId != id
            || report.State.ToString() != reader.GetString(6) || report.Outcome.ToString() != reader.GetString(7) || OperationCatalog.Name(report.Method) != reader.GetString(8)) throw new StorageException(ErrorCode.VERIFY_FAILED);
        long revision = reader.GetInt64(5); string bodyHash = reader.GetString(1); string reportHash = reader.GetString(4);
        if (report.Method != parsed.Identity.Method) throw new StorageException(ErrorCode.VERIFY_FAILED);
        reader.Dispose();
        if (OperationLifecycle.IsTerminal(report.State))
        {
            using var result = Command(c, t, "SELECT report_json,sha256 FROM operation_results WHERE operation_id=$id", ("$id", id.ToString()));
            using var proof = result.ExecuteReader();
            if (!proof.Read() || proof.GetString(1) != reportHash || !((byte[])proof[0]).AsSpan().SequenceEqual(bytes)) throw new StorageException(ErrorCode.VERIFY_FAILED);
        }
        return new(report, revision, request, bodyHash);
    }
    private static void ValidatePrincipal(string principal)
    {
        if (string.IsNullOrWhiteSpace(principal) || principal.Length > 512) throw new ArgumentException("Authenticated principal required.");
        try { _ = new System.Text.UTF8Encoding(false, true).GetByteCount(principal); }
        catch (System.Text.EncoderFallbackException) { throw new ArgumentException("Invalid principal."); }
    }
    private ArtifactProof VerifyArtifacts(byte[] report)
    {
        var proof = new ArtifactProof();
        try
        {
            using var doc = JsonDocument.Parse(report);
            void Walk(JsonElement node)
            {
                if (node.ValueKind == JsonValueKind.Object)
                {
                    if (node.TryGetProperty("artifact_id", out var id) && node.TryGetProperty("sha256", out var sha))
                    {
                        var artifact = Artifacts.OpenVerified(id.GetString()!); proof.Artifacts.Add(artifact);
                        if (artifact.Descriptor.Sha256 != sha.GetString()) throw new StorageException(ErrorCode.VERIFY_FAILED);
                    }
                    foreach (var p in node.EnumerateObject())
                    {
                        if (p.Name.EndsWith("_refs", StringComparison.Ordinal) && p.Name != "artifact_refs" && p.Value.ValueKind == JsonValueKind.Array)
                            foreach (var reference in p.Value.EnumerateArray()) if (reference.ValueKind == JsonValueKind.String) proof.Artifacts.Add(Artifacts.OpenVerified(reference.GetString()!));
                        Walk(p.Value);
                    }
                }
                else if (node.ValueKind == JsonValueKind.Array) foreach (var item in node.EnumerateArray()) Walk(item);
            }
            Walk(doc.RootElement); return proof;
        }
        catch { proof.Dispose(); throw; }
    }
    private sealed class ArtifactProof : IDisposable
    {
        internal List<VerifiedArtifact> Artifacts { get; } = new();
        public void Dispose() { foreach (var artifact in Artifacts) artifact.Dispose(); }
    }
}
