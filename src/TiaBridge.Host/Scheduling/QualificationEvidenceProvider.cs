using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TiaBridge.Core.Capabilities;
using TiaBridge.Core.Workspace;
using TiaBridge.Host.Api;

namespace TiaBridge.Host.Scheduling;

internal sealed record SignedQualificationEnvelope(string SignerId, string PayloadBase64, string SignatureBase64);
internal sealed record QualificationEvidenceDecision(IReadOnlySet<string> VerifiedIds, string SnapshotHash, string Reason)
{
    public bool Available => Reason == "QUALIFIED";
    public static QualificationEvidenceDecision Denied(string reason) => new(new HashSet<string>(StringComparer.Ordinal), "", reason);
}

/// <summary>Reads only the fixed Host state store. Client requests never select paths or trust anchors.</summary>
internal sealed class QualificationEvidenceProvider
{
    private const int MaxEvidenceBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions EvidenceJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 24,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };
    private readonly string root;
    private readonly IReadOnlyDictionary<string, byte[]> anchors;

    internal QualificationEvidenceProvider(HostConfiguration config)
    {
        root = Path.Combine(Path.GetFullPath(config.StatePath), "qualification-evidence");
        var keys = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (string value in config.QualificationTrust?.TrustedSignerKeys ?? [])
        {
            int split = value.IndexOf('=');
            if (split < 1 || split == value.Length - 1) throw new ArgumentException("Qualification signer key must be signerId=base64-SPKI.");
            string id = value[..split];
            byte[] key = Convert.FromBase64String(value[(split + 1)..]);
            using var ecdsa = ECDsa.Create(); ecdsa.ImportSubjectPublicKeyInfo(key, out int read);
            if (read != key.Length || ecdsa.KeySize != 256 || !keys.TryAdd(id, key)) throw new ArgumentException("Invalid or duplicate qualification signer key.");
        }
        anchors = keys;
    }

    internal QualificationEvidenceDecision Assess(QualificationRequirement requirement)
    {
        if (anchors.Count == 0) return QualificationEvidenceDecision.Denied("NO_TRUSTED_SIGNERS");
        if (!Directory.Exists(root)) return QualificationEvidenceDecision.Denied("EVIDENCE_STORE_UNAVAILABLE");
        try
        {
            if (IsReparse(root)) return QualificationEvidenceDecision.Denied("EVIDENCE_STORE_REPARSE");
            var verified = new Dictionary<string, (QualificationEvidence Evidence, string EnvelopeHash)>(StringComparer.Ordinal);
            foreach (string file in Directory.EnumerateFiles(root, "*.signed.json", SearchOption.TopDirectoryOnly).Order(StringComparer.Ordinal))
            {
                if (IsReparse(file)) return QualificationEvidenceDecision.Denied("EVIDENCE_FILE_REPARSE");
                var info = new FileInfo(file); if (info.Length is <= 0 or > MaxEvidenceBytes) return QualificationEvidenceDecision.Denied("EVIDENCE_SIZE_INVALID");
                byte[] envelopeBytes = File.ReadAllBytes(file);
                var envelope = JsonSerializer.Deserialize<SignedQualificationEnvelope>(envelopeBytes, EvidenceJson);
                if (envelope is null || !anchors.TryGetValue(envelope.SignerId, out byte[]? publicKey)) continue;
                byte[] payload = Convert.FromBase64String(envelope.PayloadBase64);
                byte[] signature = Convert.FromBase64String(envelope.SignatureBase64);
                using var ecdsa = ECDsa.Create(); ecdsa.ImportSubjectPublicKeyInfo(publicKey, out _);
                if (!ecdsa.VerifyData(payload, signature, HashAlgorithmName.SHA256)) continue;
                var evidence = JsonSerializer.Deserialize<QualificationEvidence>(payload, EvidenceJson);
                if (evidence is null || !QualificationEvidencePolicy.Valid(evidence)
                    || Path.GetFileName(file) != evidence.EvidenceId + ".signed.json"
                    || !VerifyArtifacts(evidence)) continue;
                verified[evidence.EvidenceId] = (evidence, Convert.ToHexStringLower(SHA256.HashData(envelopeBytes)));
            }
            var match = verified.Values.Where(v => v.Evidence.EvidenceId == requirement.EvidenceId)
                .Select(v => QualificationEvidencePolicy.Assess(requirement, [v.Evidence])).SingleOrDefault();
            if (match is null || !match.Qualified) return QualificationEvidenceDecision.Denied(match?.Denial.ToString() ?? "EVIDENCE_NOT_FOUND_OR_UNTRUSTED");
            var selected = verified[requirement.EvidenceId];
            string snapshot = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(requirement.EvidenceId + "\n" + selected.EnvelopeHash)));
            return new(new HashSet<string>([requirement.EvidenceId], StringComparer.Ordinal), snapshot, "QUALIFIED");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or FormatException or CryptographicException or ArgumentException or NotSupportedException)
        { return QualificationEvidenceDecision.Denied("EVIDENCE_STORE_INVALID_OR_UNAVAILABLE"); }
    }

    private bool VerifyArtifacts(QualificationEvidence evidence)
    {
        string directory = Path.Combine(root, evidence.EvidenceId);
        if (!Directory.Exists(directory) || IsReparse(directory)) return false;
        foreach (var artifact in evidence.Artifacts)
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(artifact.Name, "\\A[A-Za-z0-9][A-Za-z0-9._-]{0,127}\\z", System.Text.RegularExpressions.RegexOptions.CultureInvariant)) return false;
            string path = Path.Combine(directory, artifact.Name);
            if (!File.Exists(path) || IsReparse(path)) return false;
            using var stream = File.OpenRead(path);
            if (!string.Equals(Convert.ToHexStringLower(SHA256.HashData(stream)), artifact.Sha256, StringComparison.Ordinal)) return false;
        }
        return true;
    }

    private static bool IsReparse(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return true;
        return false;
    }
}
