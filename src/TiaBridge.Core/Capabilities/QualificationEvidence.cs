using TiaBridge.Core.Operations;
using TiaBridge.Core.Workspace;

namespace TiaBridge.Core.Capabilities;

public enum QualificationResult { PASS, FAIL, NOT_RUN, BLOCKED }
public enum QualificationEvidenceKind { OFFLINE_TEST, ENGINEERING_TEST, RUNTIME_TEST, REVIEW }

public sealed record QualificationArtifact(string Name, string Sha256);

/// <summary>Signed qualification claims are data until Host verifies signer, payload and artifacts.</summary>
public sealed record QualificationEvidence(
    string EvidenceId,
    int Revision,
    QualificationEvidenceKind Kind,
    string Source,
    string Provenance,
    string TiaBuild,
    string AdapterId,
    string AdapterBuild,
    string WorkerBuild,
    OperationMethod Operation,
    OperationScope Scope,
    DeviceFamily? DeviceFamily,
    string? ObjectKind,
    string Profile,
    IReadOnlyList<string> ReadScope,
    IReadOnlyList<CoverageItem> Coverage,
    QualificationResult Result,
    string RunId,
    string? ProjectKey,
    string? ProjectClass,
    IReadOnlyList<QualificationArtifact> Artifacts,
    DateTimeOffset CreatedAtUtc);

public sealed record QualificationRequirement(
    string EvidenceId,
    string TiaBuild,
    string AdapterId,
    string AdapterBuild,
    string WorkerBuild,
    OperationMethod Operation,
    OperationScope Scope,
    DeviceFamily? DeviceFamily,
    string? ObjectKind,
    string Profile,
    IReadOnlyList<string> ReadScope,
    IReadOnlyList<CoverageDimension> Coverage,
    string? ProjectKey,
    string? ProjectClass);

public enum QualificationEvidenceDenial
{
    NONE, INVALID, NOT_FOUND, RESULT_NOT_PASS, SCOPE_MISMATCH, BUILD_MISMATCH,
    PROFILE_MISMATCH, READ_SCOPE_MISMATCH, COVERAGE_GAP, PROJECT_MISMATCH
}

public sealed record QualificationEvidenceAssessment(bool Qualified, QualificationEvidenceDenial Denial);

/// <summary>Pure exact-match policy. Authenticity and artifact hashes must be verified by Host first.</summary>
public static class QualificationEvidencePolicy
{
    public static QualificationEvidenceAssessment Assess(
        QualificationRequirement requirement, IReadOnlyCollection<QualificationEvidence> verifiedEvidence)
    {
        ArgumentNullException.ThrowIfNull(requirement);
        ArgumentNullException.ThrowIfNull(verifiedEvidence);
        var evidence = verifiedEvidence.SingleOrDefault(e => e.EvidenceId == requirement.EvidenceId);
        if (evidence is null) return new(false, QualificationEvidenceDenial.NOT_FOUND);
        if (!Valid(evidence)) return new(false, QualificationEvidenceDenial.INVALID);
        if (evidence.Result != QualificationResult.PASS) return new(false, QualificationEvidenceDenial.RESULT_NOT_PASS);
        if (evidence.Operation != requirement.Operation || evidence.Scope != requirement.Scope
            || evidence.DeviceFamily != requirement.DeviceFamily || evidence.ObjectKind != requirement.ObjectKind)
            return new(false, QualificationEvidenceDenial.SCOPE_MISMATCH);
        if (evidence.TiaBuild != requirement.TiaBuild || evidence.AdapterId != requirement.AdapterId
            || evidence.AdapterBuild != requirement.AdapterBuild || evidence.WorkerBuild != requirement.WorkerBuild)
            return new(false, QualificationEvidenceDenial.BUILD_MISMATCH);
        if (evidence.Profile != requirement.Profile) return new(false, QualificationEvidenceDenial.PROFILE_MISMATCH);
        if (!SetEquals(evidence.ReadScope, requirement.ReadScope)) return new(false, QualificationEvidenceDenial.READ_SCOPE_MISMATCH);
        if (requirement.Coverage.Any(c => !evidence.Coverage.Any(x => x.Dimension == c && x.State == CoverageState.COMPLETE)))
            return new(false, QualificationEvidenceDenial.COVERAGE_GAP);
        if (requirement.ProjectKey is not null && evidence.ProjectKey != requirement.ProjectKey
            || requirement.ProjectClass is not null && evidence.ProjectClass != requirement.ProjectClass)
            return new(false, QualificationEvidenceDenial.PROJECT_MISMATCH);
        return new(true, QualificationEvidenceDenial.NONE);
    }

    public static bool Valid(QualificationEvidence e) =>
        System.Text.RegularExpressions.Regex.IsMatch(e.EvidenceId, "\\A[A-Za-z0-9][A-Za-z0-9._-]{0,127}\\z", System.Text.RegularExpressions.RegexOptions.CultureInvariant)
        && e.Revision > 0 && Enum.IsDefined(e.Kind)
        && !string.IsNullOrWhiteSpace(e.Source) && !string.IsNullOrWhiteSpace(e.Provenance)
        && !string.IsNullOrWhiteSpace(e.TiaBuild) && !string.IsNullOrWhiteSpace(e.AdapterId)
        && !string.IsNullOrWhiteSpace(e.AdapterBuild) && !string.IsNullOrWhiteSpace(e.WorkerBuild)
        && Enum.IsDefined(e.Operation) && Enum.IsDefined(e.Scope)
        && ((e.Scope == OperationScope.PROJECT) == (e.DeviceFamily is null))
        && !string.IsNullOrWhiteSpace(e.Profile) && e.ReadScope is { Count: > 0 }
        && e.ReadScope.All(s => !string.IsNullOrWhiteSpace(s))
        && e.ReadScope.Distinct(StringComparer.Ordinal).Count() == e.ReadScope.Count
        && e.Coverage is { Count: > 0 } && e.Coverage.All(c => Enum.IsDefined(c.Dimension) && Enum.IsDefined(c.State))
        && e.Coverage.Select(c => c.Dimension).Distinct().Count() == e.Coverage.Count
        && Enum.IsDefined(e.Result) && !string.IsNullOrWhiteSpace(e.RunId)
        && (e.ProjectKey is null || Guid.TryParse(e.ProjectKey, out _))
        && (e.ProjectClass is null || !string.IsNullOrWhiteSpace(e.ProjectClass))
        && e.Artifacts is { Count: > 0 }
        && e.Artifacts.All(a => System.Text.RegularExpressions.Regex.IsMatch(a.Name, "\\A[A-Za-z0-9][A-Za-z0-9._-]{0,127}\\z", System.Text.RegularExpressions.RegexOptions.CultureInvariant)
            && System.Text.RegularExpressions.Regex.IsMatch(a.Sha256, "\\A[0-9a-f]{64}\\z", System.Text.RegularExpressions.RegexOptions.CultureInvariant))
        && e.Artifacts.Select(a => a.Name).Distinct(StringComparer.Ordinal).Count() == e.Artifacts.Count
        && e.CreatedAtUtc.Offset == TimeSpan.Zero;

    private static bool SetEquals(IReadOnlyList<string> left, IReadOnlyList<string> right) =>
        left.Count == right.Count && left.ToHashSet(StringComparer.Ordinal).SetEquals(right);
}
