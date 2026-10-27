using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TiaBridge.Host.Api;
using TiaBridge.Host.Qualification;
using TiaBridge.Host.Scheduling;

namespace TiaBridge.Qualification;

/// <summary>Operator-only local writes. Not referenced by the Host executable.</summary>
[SupportedOSPlatform("windows")]
internal sealed class LocalEnrollment(HostConfiguration config)
{
    internal LocalTrustStore Store { get; } = new(config);
    internal InspectedPackage Inspect(string id, int revision) => Store.Inspect(id, revision);
    internal LocalApproval Approve(string id, int revision, string reviewedHash, bool explicitApproval)
    {
        LocalTrustAcl.Operator(Store.Policy); Store.Check();
        var package = Inspect(id, revision);
        if (!explicitApproval || reviewedHash != package.PackageSha256) throw new IOException("OPERATOR_APPROVAL_REQUIRED");
        if (!package.Eligible) throw new IOException("READ_QUALIFICATION_REQUIRED");
        using var gate = Lock(); _ = Store.Events();
        var approval = new LocalApproval(id, revision, reviewedHash, LocalTrustAcl.CurrentSid, DateTimeOffset.UtcNow);
        string directory = Store.RevisionPath("approvals", id, revision); CreateDirectory(directory);
        WriteNew(Path.Combine(directory, "approval.json"), JsonSerializer.SerializeToUtf8Bytes(approval, LocalTrustStore.Json));
        return approval;
    }
    internal LocalTrustEvent Enroll(string id, int revision, ECDsa signer)
    {
        LocalTrustAcl.Operator(Store.Policy); Store.Check();
        using var gate = Lock(); var events = Store.Events();
        if (events.Any(e => string.Equals(e.EvidenceId, id, StringComparison.OrdinalIgnoreCase) && e.Revision >= revision)) throw new IOException("REVISION_ALREADY_USED");
        var package = Inspect(id, revision);
        if (!package.Eligible) throw new IOException("READ_QUALIFICATION_REQUIRED");
        var approval = LocalTrustStore.Parse<LocalApproval>(Store.Bytes(Path.Combine(Store.RevisionPath("approvals", id, revision), "approval.json")));
        if (approval.EvidenceId != id || approval.Revision != revision || approval.PackageSha256 != package.PackageSha256
            || approval.OperatorSid != LocalTrustAcl.CurrentSid || approval.ApprovedAtUtc.Offset != TimeSpan.Zero) throw new IOException("APPROVAL_BINDING_MISMATCH");
        var anchor = QualificationEvidenceProvider.ReadAnchors(Store.Policy).Single();
        if (signer.ExportParameters(false).Curve.Oid.Value != "1.2.840.10045.3.1.7"
            || !signer.ExportSubjectPublicKeyInfo().AsSpan().SequenceEqual(anchor.Value)) throw new IOException("SIGNER_KEY_MISMATCH");
        var envelope = new SignedQualificationEnvelope(anchor.Key, Convert.ToBase64String(package.Payload),
            Convert.ToBase64String(signer.SignData(package.Payload, HashAlgorithmName.SHA256)));
        byte[] envelopeBytes = JsonSerializer.SerializeToUtf8Bytes(envelope, LocalTrustStore.Json);
        if (envelopeBytes.Length > LocalTrustStore.MaxEnvelopeBytes) throw new IOException("QUALIFICATION_ENVELOPE_SIZE_LIMIT");
        string staging = Path.Combine(Store.Root, "staging", Guid.NewGuid().ToString("N")); CreateDirectory(staging);
        foreach (var artifact in package.Evidence.Artifacts)
        {
            using var input = Store.Open(Path.Combine(package.Directory, artifact.Name), LocalTrustStore.MaxArtifactBytes);
            string target = Path.Combine(staging, artifact.Name);
            using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 65536, FileOptions.WriteThrough))
            {
                input.CopyTo(output); output.Flush(true); output.Position = 0;
                if (LocalTrustStore.Sha(output) != artifact.Sha256) throw new IOException("ARTIFACT_CHANGED_DURING_ENROLLMENT");
                LocalTrustAcl.Verify(output.GetAccessControl(), Store.Policy);
            }
        }
        WriteNew(Path.Combine(staging, "evidence.signed.json"), envelopeBytes);
        string destination = Store.RevisionPath("records", id, revision); CreateDirectory(Path.GetDirectoryName(destination)!);
        Directory.Move(staging, destination); LocalTrustAcl.Directory(destination, Store.Policy);
        var entry = new LocalTrustEvent(events.Count + 1, "ENROLLED", id, revision, LocalTrustStore.Sha(envelopeBytes),
            package.PackageSha256, LocalTrustAcl.CurrentSid, DateTimeOffset.UtcNow, "OPERATOR_APPROVED");
        Append(entry); _ = Store.Verify(id, revision, QualificationEvidenceProvider.ReadAnchors(Store.Policy));
        return entry;
        // Failure retains staging/published data; only a flushed journal entry activates it. No overwrite, retry or auto-adoption.
    }
    internal LocalTrustEvent Revoke(string id, int revision, string reason)
    {
        LocalTrustAcl.Operator(Store.Policy); Store.Check();
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 512) throw new IOException("REVOCATION_REASON_REQUIRED");
        using var gate = Lock(); var events = Store.Events();
        var enrolled = events.SingleOrDefault(e => e.Action == "ENROLLED" && e.EvidenceId == id && e.Revision == revision) ?? throw new IOException("ENROLLMENT_NOT_FOUND");
        if (events.Any(e => e.Action == "REVOKED" && e.EvidenceId == id && e.Revision == revision)) throw new IOException("ALREADY_REVOKED");
        var revoked = enrolled with { Sequence = events.Count + 1, Action = "REVOKED", RecordedAtUtc = DateTimeOffset.UtcNow, Reason = reason };
        Append(revoked); return revoked;
    }
    internal IReadOnlyList<ActiveQualification> List()
    {
        var anchors = QualificationEvidenceProvider.ReadAnchors(Store.Policy);
        return Store.Events().Select(e => e.EvidenceId).Distinct(StringComparer.Ordinal).Select(Store.Current)
            .Where(e => e is not null).Select(e => Store.Verify(e!.EvidenceId, e.Revision, anchors)).ToArray();
    }
    private FileStream Lock()
    {
        string path = Path.Combine(Store.Root, "operator.lock");
        TiaBridge.Host.Storage.StorageLocation.CheckChain(path);
        var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try { LocalTrustAcl.Verify(stream.GetAccessControl(), Store.Policy); return stream; }
        catch { stream.Dispose(); throw; }
    }
    private void CreateDirectory(string directory)
    {
        // New children inherit the already verified root policy. Never repairs a foreign ACL.
        TiaBridge.Host.Storage.StorageLocation.CheckChain(directory);
        Directory.CreateDirectory(directory);
        for (string? current = directory; current is not null && current.Length >= Store.Root.Length; current = Path.GetDirectoryName(current))
            LocalTrustAcl.Directory(current, Store.Policy, current == Store.Root);
    }
    private void WriteNew(string path, byte[] bytes)
    {
        TiaBridge.Host.Storage.StorageLocation.CheckChain(path);
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough);
        LocalTrustAcl.Verify(stream.GetAccessControl(), Store.Policy); stream.Write(bytes); stream.Flush(true);
    }
    private void Append(LocalTrustEvent entry)
    {
        // Missing history is never re-created. The operator must explicitly provision the journal.
        byte[] line = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(entry, LocalTrustStore.Json) + "\n");
        using var stream = new FileStream(Store.JournalPath, FileMode.Open, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough);
        LocalTrustAcl.Verify(stream.GetAccessControl(), Store.Policy);
        if (stream.Length > LocalTrustStore.MaxJournalBytes - line.Length) throw new IOException("LIFECYCLE_JOURNAL_FULL");
        stream.Seek(0, SeekOrigin.End); stream.Write(line); stream.Flush(true);
    }
}
