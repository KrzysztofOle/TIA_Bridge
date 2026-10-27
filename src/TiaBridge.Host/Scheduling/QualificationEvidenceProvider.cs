using System.Security.Cryptography;
using System.Text;
using TiaBridge.Core.Capabilities;
using TiaBridge.Host.Api;
using TiaBridge.Host.Qualification;

namespace TiaBridge.Host.Scheduling;

internal sealed record SignedQualificationEnvelope(string SignerId, string PayloadBase64, string SignatureBase64);
internal sealed record QualificationEvidenceDecision(IReadOnlySet<string> VerifiedIds, string SnapshotHash, string Reason)
{
    public bool Available => Reason == "QUALIFIED";
    public static QualificationEvidenceDecision Denied(string reason) => new(new HashSet<string>(StringComparer.Ordinal), "", reason);
}

/// <summary>Reads only the fixed local store; no signing, enrollment or client-supplied paths.</summary>
internal sealed class QualificationEvidenceProvider
{
    private readonly HostConfiguration config;
    private readonly IReadOnlyDictionary<string, byte[]> anchors;
    internal QualificationEvidenceProvider(HostConfiguration config)
    {
        this.config = config; anchors = ReadAnchors(config.QualificationTrust);
    }
    internal static IReadOnlyDictionary<string, byte[]> ReadAnchors(QualificationTrustConfiguration? policy)
    {
        var keys = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (string value in policy?.TrustedSignerKeys ?? [])
        {
            int split = value.IndexOf('=');
            if (split < 1 || split == value.Length - 1) throw new ArgumentException("Invalid qualification public key.");
            string id = value[..split];
            if (id.Length > 128 || id.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_'))) throw new ArgumentException("Invalid signer ID.");
            byte[] key = Convert.FromBase64String(value[(split + 1)..]);
            using var ecdsa = ECDsa.Create(); ecdsa.ImportSubjectPublicKeyInfo(key, out int read);
            if (read != key.Length || ecdsa.ExportParameters(false).Curve.Oid.Value != "1.2.840.10045.3.1.7"
                || !keys.TryAdd(id, key) || keys.Count > 1) throw new ArgumentException("One P-256 public key required.");
        }
        return keys;
    }
    internal QualificationEvidenceDecision Assess(QualificationRequirement requirement)
    {
        if (anchors.Count == 0) return QualificationEvidenceDecision.Denied("NO_TRUSTED_SIGNERS");
        if (!OperatingSystem.IsWindows()) return QualificationEvidenceDecision.Denied("WINDOWS_REQUIRED");
        if (!Directory.Exists(Path.Combine(config.StatePath, "qualification-evidence"))) return QualificationEvidenceDecision.Denied("EVIDENCE_STORE_UNAVAILABLE");
        try
        {
            var store = new LocalTrustStore(config); store.Check(host: true);
            var active = store.Current(requirement.EvidenceId);
            if (active is null) return QualificationEvidenceDecision.Denied("EVIDENCE_NOT_ACTIVE");
            var selected = store.Verify(requirement.EvidenceId, active.Revision, anchors);
            var match = QualificationEvidencePolicy.Assess(requirement, [selected.Evidence]);
            if (!match.Qualified) return QualificationEvidenceDecision.Denied(match.Denial.ToString());
            string snapshot = LocalTrustStore.Sha(Encoding.UTF8.GetBytes(requirement.EvidenceId + "\n" + selected.EnvelopeSha256));
            return new(new HashSet<string>([requirement.EvidenceId], StringComparer.Ordinal), snapshot, "QUALIFIED");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException
            or FormatException or CryptographicException or ArgumentException or NotSupportedException or TiaBridge.Ipc.ProtocolException)
        { return QualificationEvidenceDecision.Denied("EVIDENCE_STORE_INVALID_OR_UNAVAILABLE"); }
    }
}
