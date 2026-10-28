using System.Globalization;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using TiaBridge.Core.Capabilities;
using TiaBridge.Core.Operations;
using TiaBridge.Core.Workspace;
using TiaBridge.Host.Api;
using TiaBridge.Host.Scheduling;
using TiaBridge.Host.Storage;
using TiaBridge.Ipc;

namespace TiaBridge.Host.Qualification;

internal sealed record LocalApproval(string EvidenceId, int Revision, string PackageSha256, string OperatorSid, DateTimeOffset ApprovedAtUtc);
internal sealed record LocalTrustEvent(long Sequence, string Action, string EvidenceId, int Revision, string EnvelopeSha256,
    string PackageSha256, string OperatorSid, DateTimeOffset RecordedAtUtc, string Reason);
internal sealed record InspectedPackage(QualificationEvidence Evidence, byte[] Payload, string PackageSha256, string Directory, bool Eligible);
internal sealed record ActiveQualification(QualificationEvidence Evidence, string EnvelopeSha256, LocalTrustEvent Enrollment);

/// <summary>Fixed local store, immutable revisions and a fail-closed durable lifecycle journal.</summary>
[SupportedOSPlatform("windows")]
internal sealed class LocalTrustStore
{
    internal const int MaxJsonBytes = 1024 * 1024;
    internal const int MaxEnvelopeBytes = 2 * MaxJsonBytes;
    internal const int MaxJournalBytes = 8 * MaxJsonBytes;
    internal const long MaxArtifactBytes = 64L * 1024 * 1024;
    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        MaxDepth = 24,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };
    internal string Root { get; }
    internal QualificationTrustConfiguration Policy { get; }
    private readonly HostConfiguration config;
    internal string JournalPath => Path.Combine(Root, "lifecycle.jsonl");
    internal LocalTrustStore(HostConfiguration config)
    {
        this.config = config;
        Policy = config.QualificationTrust ?? throw new IOException("LOCAL_POLICY_REQUIRED");
        LocalTrustAcl.Policy(Policy);
        Root = Path.Combine(StorageLocation.Validate(config.StatePath, config.RepositoryRoot), "qualification-evidence");
    }
    internal void Check(bool host = false)
    {
        if (host && LocalTrustAcl.CurrentSid != Policy.HostSid) throw new IOException("HOST_IDENTITY_MISMATCH");
        LocalTrustAcl.Parents(Root, Policy);
        LocalTrustAcl.Directory(Root, Policy, protectedRoot: true);
    }
    internal string RevisionPath(string area, string id, int revision)
    {
        Name(id); if (revision < 1) throw new IOException("INVALID_REVISION");
        if (area is not ("inbox" or "records" or "approvals")) throw new IOException("INVALID_AREA");
        return Path.Combine(Root, area, id, revision.ToString(CultureInfo.InvariantCulture));
    }
    internal static void Name(string id)
    {
        if (id is null || !Regex.IsMatch(id, "\\A[A-Za-z0-9][A-Za-z0-9._-]{0,127}\\z", RegexOptions.CultureInvariant)
            || id.EndsWith('.') || id.Split('.')[0].ToUpperInvariant() is "CON" or "PRN" or "AUX" or "NUL"
            || Regex.IsMatch(id, "\\A(?:COM|LPT)[0-9](?:\\.|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            throw new IOException("INVALID_LOCAL_NAME");
    }
    internal FileStream Open(string path, long maximum)
    {
        string full = Path.GetFullPath(path);
        if (!full.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("STORE_PATH_REQUIRED");
        for (string? current = Path.GetDirectoryName(full); current is not null && current.Length >= Root.Length; current = Path.GetDirectoryName(current))
            LocalTrustAcl.Directory(current, Policy, current == Root);
        StorageLocation.CheckChain(full);
        var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            LocalTrustAcl.Verify(stream.GetAccessControl(), Policy);
            if (stream.Length > maximum) throw new IOException("QUALIFICATION_SIZE_LIMIT");
            return stream;
        }
        catch { stream.Dispose(); throw; }
    }
    internal byte[] Bytes(string path, int maximum = MaxJsonBytes)
    {
        using var stream = Open(path, maximum); byte[] bytes = new byte[checked((int)stream.Length)]; stream.ReadExactly(bytes); return bytes;
    }
    internal static T Parse<T>(byte[] bytes)
    {
        Protocol.CheckJson(bytes);
        return JsonSerializer.Deserialize<T>(bytes, Json) ?? throw new IOException("INVALID_LOCAL_RECORD");
    }
    internal IReadOnlyList<LocalTrustEvent> Events()
    {
        Check(); byte[] bytes = Bytes(JournalPath, MaxJournalBytes);
        string text = new UTF8Encoding(false, true).GetString(bytes);
        if (text.Length > 0 && !text.EndsWith('\n')) throw new IOException("INCOMPLETE_LIFECYCLE_JOURNAL");
        var events = new List<LocalTrustEvent>();
        var revisions = new Dictionary<(string, int), LocalTrustEvent>();
        var latest = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var entry = Parse<LocalTrustEvent>(Encoding.UTF8.GetBytes(line)); Name(entry.EvidenceId);
            if (entry.Sequence != events.Count + 1 || entry.Revision < 1 || entry.OperatorSid != Policy.OperatorSid
                || entry.RecordedAtUtc.Offset != TimeSpan.Zero || !Hash(entry.EnvelopeSha256) || !Hash(entry.PackageSha256)
                || string.IsNullOrWhiteSpace(entry.Reason) || entry.Reason.Length > 512) throw new IOException("INVALID_LIFECYCLE_JOURNAL");
            var key = (entry.EvidenceId.ToUpperInvariant(), entry.Revision);
            if (entry.Action == "ENROLLED")
            {
                if (revisions.ContainsKey(key) || entry.Revision <= latest.GetValueOrDefault(entry.EvidenceId)) throw new IOException("REVISION_REUSE");
                revisions.Add(key, entry); latest[entry.EvidenceId] = entry.Revision;
            }
            else if (entry.Action == "REVOKED")
            {
                if (!revisions.TryGetValue(key, out var enrolled) || enrolled.Action != "ENROLLED"
                    || enrolled.EnvelopeSha256 != entry.EnvelopeSha256 || enrolled.PackageSha256 != entry.PackageSha256)
                    throw new IOException("INVALID_REVOCATION");
                revisions[key] = entry;
            }
            else throw new IOException("INVALID_LIFECYCLE_ACTION");
            events.Add(entry);
        }
        return events;
    }
    internal LocalTrustEvent? Current(string id)
    {
        Name(id);
        var events = Events().Where(e => string.Equals(e.EvidenceId, id, StringComparison.OrdinalIgnoreCase)).ToArray();
        var enrollment = events.LastOrDefault(e => e.Action == "ENROLLED");
        if (enrollment is null || enrollment.EvidenceId != id) return null;
        return events.Any(e => e.Action == "REVOKED" && e.Revision == enrollment.Revision) ? null : enrollment;
    }
    internal InspectedPackage Inspect(string id, int revision)
    {
        Check(); string directory = RevisionPath("inbox", id, revision);
        byte[] payload = Bytes(Path.Combine(directory, "evidence.json"));
        var evidence = Parse<QualificationEvidence>(payload); Validate(evidence);
        if (evidence.EvidenceId != id || evidence.Revision != revision) throw new IOException("PACKAGE_IDENTITY_MISMATCH");
        VerifyArtifacts(directory, evidence);
        return new(evidence, payload, PackageHash(payload, evidence), directory, Eligible(evidence));
    }
    internal ActiveQualification Verify(string id, int revision, IReadOnlyDictionary<string, byte[]> anchors)
    {
        Check(); var activation = Current(id);
        if (activation is null || activation.Revision != revision) throw new IOException("QUALIFICATION_NOT_ACTIVE");
        string directory = RevisionPath("records", id, revision);
        byte[] bytes = Bytes(Path.Combine(directory, "evidence.signed.json"), MaxEnvelopeBytes);
        if (Sha(bytes) != activation.EnvelopeSha256) throw new IOException("ENVELOPE_CHANGED");
        var envelope = Parse<SignedQualificationEnvelope>(bytes);
        if (!anchors.TryGetValue(envelope.SignerId, out var key)) throw new IOException("UNTRUSTED_SIGNER");
        byte[] payload = Convert.FromBase64String(envelope.PayloadBase64);
        if (payload.Length > MaxJsonBytes) throw new IOException("QUALIFICATION_PAYLOAD_SIZE_LIMIT");
        using var verifier = ECDsa.Create(); verifier.ImportSubjectPublicKeyInfo(key, out _);
        if (!verifier.VerifyData(payload, Convert.FromBase64String(envelope.SignatureBase64), HashAlgorithmName.SHA256)) throw new IOException("INVALID_SIGNATURE");
        var evidence = Parse<QualificationEvidence>(payload); Validate(evidence);
        if (evidence.EvidenceId != id || evidence.Revision != revision || !Eligible(evidence)
            || PackageHash(payload, evidence) != activation.PackageSha256) throw new IOException("ENROLLED_PACKAGE_MISMATCH");
        VerifyArtifacts(directory, evidence);
        // Re-read lifecycle after hashing: a revoke observed before return denies this assessment.
        if (Current(id) != activation) throw new IOException("QUALIFICATION_CHANGED");
        return new(evidence, activation.EnvelopeSha256, activation);
    }
    private void VerifyArtifacts(string directory, QualificationEvidence evidence)
    {
        long total = 0;
        foreach (var artifact in evidence.Artifacts)
        {
            Name(artifact.Name);
            if (artifact.Name.Equals("evidence.json", StringComparison.OrdinalIgnoreCase) || artifact.Name.Equals("evidence.signed.json", StringComparison.OrdinalIgnoreCase)) throw new IOException("RESERVED_ARTIFACT_NAME");
            using var stream = Open(Path.Combine(directory, artifact.Name), MaxArtifactBytes); total += stream.Length;
            if (total > 128L * 1024 * 1024 || Sha(stream) != artifact.Sha256) throw new IOException("ARTIFACT_INTEGRITY_FAILED");
        }
    }
    internal bool Eligible(QualificationEvidence e) => e.Result == QualificationResult.PASS
        && e.Scope == OperationScope.PROJECT && e.DeviceFamily is null
        && e.ProjectKey is not null && config.RegisteredProjects.Contains(e.ProjectKey, StringComparer.Ordinal)
        && e.AdapterId == "tia-bridge.worker.v19" && e.AdapterBuild == e.WorkerBuild
        && Regex.IsMatch(e.WorkerBuild, "\\A1\\.0\\+[0-9a-f]{64}\\z", RegexOptions.CultureInvariant)
        && (e.Operation == OperationMethod.ProjectInspect && e.ObjectKind == "project" && e.ProjectClass is null
        && e.Profile == V19DiscoveryProfile.Id && e.ReadScope.Count == V19InspectionProfile.DiscoveryReadScope.Count
        && e.ReadScope.ToHashSet(StringComparer.Ordinal).SetEquals(V19InspectionProfile.DiscoveryReadScope)
        && e.Coverage.Any(c => c.Dimension == CoverageDimension.STRUCTURE && c.State == CoverageState.COMPLETE)
        || e.Operation == OperationMethod.ProjectExport && e.Kind == QualificationEvidenceKind.ENGINEERING_TEST
        && e.ObjectKind == "hmi-script-module" && e.ProjectClass is not null
        && Regex.IsMatch(e.ProjectClass, "\\Ahmi-scope:[0-9a-f]{64}\\z", RegexOptions.CultureInvariant)
        && e.Profile == V19HmiExportPolicy.Profile && e.ReadScope.Count == V19InspectionProfile.HmiExportReadScope.Count
        && e.ReadScope.ToHashSet(StringComparer.Ordinal).SetEquals(V19InspectionProfile.HmiExportReadScope)
        && e.Coverage.Any(c => c.Dimension == CoverageDimension.CONTENT && c.State == CoverageState.COMPLETE));
    private static void Validate(QualificationEvidence e)
    {
        if (e.EvidenceId is null || e.ReadScope is null || e.Coverage is null || e.Artifacts is null
            || e.Coverage.Any(c => c is null) || e.Artifacts.Any(a => a is null || a.Name is null || a.Sha256 is null)
            || !QualificationEvidencePolicy.Valid(e) || e.Artifacts.Count > 64
            || e.Artifacts.Select(a => a.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != e.Artifacts.Count)
            throw new IOException("INVALID_QUALIFICATION_PAYLOAD");
        Name(e.EvidenceId);
    }
    internal static string PackageHash(byte[] payload, QualificationEvidence evidence) => Sha(JsonSerializer.SerializeToUtf8Bytes(new
    { payload_sha256 = Sha(payload), artifacts = evidence.Artifacts.OrderBy(a => a.Name, StringComparer.Ordinal).ToArray() }, Json));
    internal static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    internal static string Sha(Stream stream) => Convert.ToHexStringLower(SHA256.HashData(stream));
    internal static bool Hash(string value) => value is not null && Regex.IsMatch(value, "\\A[0-9a-f]{64}\\z", RegexOptions.CultureInvariant);
}
