using TiaBridge.Core.Operations;
using TiaBridge.Core.Workspace;

namespace TiaBridge.Core.Capabilities;

public sealed record CapabilityKey(string TiaVersion, string AdapterId, string AdapterBuild,
    DeviceFamily DeviceFamily, string ObjectKind, string? Language, OperationMethod Operation);
public sealed record CapabilityEntry(CapabilityKey Key, bool ContractDesigned, bool AdapterImplemented,
    string NormalizerVersion, string ComparisonProfile, IReadOnlyList<string> ReadScope,
    IReadOnlyList<string> WriteScope, Coverage Coverage, IReadOnlyList<string> RequiredQualifications);
public sealed record CapabilityConfiguration(string SchemaVersion, string RegistryVersion, IReadOnlyList<CapabilityEntry> Entries);
public sealed record CapabilityQuery(CapabilityKey Key, string RegistryVersion, string NormalizerVersion,
    string ComparisonProfile, IReadOnlyList<string> ReadScope, IReadOnlyList<string> WriteScope,
    IReadOnlyList<CoverageDimension> RequiredCoverage);

public enum CapabilityDenial { NONE, VERSION_MISMATCH, MISSING_CAPABILITY, NOT_DESIGNED, NOT_IMPLEMENTED, SCOPE_NOT_COVERED, COVERAGE_GAP, QUALIFICATION_REQUIRED }
public enum SessionPermissionState { NOT_EVALUATED }
public sealed record CapabilityAssessment(bool ContractDesigned, bool AdapterImplemented, bool TestQualified,
    bool CapabilityConfirmed, CapabilityDenial Denial)
{
    // B-01B deliberately cannot grant an operation, even with a confirmed capability.
    public SessionPermissionState SessionPermission => SessionPermissionState.NOT_EVALUATED;
}

/// <summary>Snapshot of a versioned registry. Exact matching; no wildcard, fallback or session grants.</summary>
public sealed class CapabilityRegistry
{
    private readonly CapabilityConfiguration configuration;
    public string RegistryVersion => configuration.RegistryVersion;
    public string CapabilitySetHash { get; }
    public static CapabilityRegistry DenyAll { get; } = new(new CapabilityConfiguration("1.0", "empty-1", Array.Empty<CapabilityEntry>()));

    public CapabilityRegistry(CapabilityConfiguration configuration)
    {
        // Boundary round-trip snapshots mutable caller-owned collections.
        byte[] bytes = CapabilityJson.Write(configuration);
        this.configuration = CapabilityJson.Read(bytes);
        CapabilitySetHash = ArtifactJson.Hash(bytes);
    }

    /// <param name="availableQualifications">Trusted caller-supplied evidence IDs, verified outside B-01B;
    /// their mere appearance in JSON or in RequiredQualifications is not availability.</param>
    public CapabilityAssessment Assess(CapabilityQuery query, IReadOnlySet<string>? availableQualifications = null)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.RegistryVersion != RegistryVersion)
            return new(false, false, false, false, CapabilityDenial.VERSION_MISMATCH);
        var entry = configuration.Entries.SingleOrDefault(e => e.Key == query.Key);
        if (entry is null) return new(false, false, false, false, CapabilityDenial.MISSING_CAPABILITY);
        bool qualified = entry.AdapterImplemented && entry.ContractDesigned && entry.RequiredQualifications.Count > 0
            && availableQualifications is not null && entry.RequiredQualifications.All(q => availableQualifications.Contains(q));
        CapabilityAssessment Denied(CapabilityDenial reason) => new(entry.ContractDesigned, entry.AdapterImplemented, qualified, false, reason);
        if (!entry.ContractDesigned) return Denied(CapabilityDenial.NOT_DESIGNED);
        if (!entry.AdapterImplemented) return Denied(CapabilityDenial.NOT_IMPLEMENTED);
        if (entry.NormalizerVersion != query.NormalizerVersion || entry.ComparisonProfile != query.ComparisonProfile)
            return Denied(CapabilityDenial.VERSION_MISMATCH);
        if (query.ReadScope.Count + query.WriteScope.Count == 0 || query.RequiredCoverage.Count == 0
            || query.ReadScope.Any(s => !entry.ReadScope.Contains(s, StringComparer.Ordinal))
            || query.WriteScope.Any(s => !entry.WriteScope.Contains(s, StringComparer.Ordinal)))
            return Denied(CapabilityDenial.SCOPE_NOT_COVERED);
        if (entry.Coverage.Missing(query.RequiredCoverage).Count != 0) return Denied(CapabilityDenial.COVERAGE_GAP);
        if (!qualified) return Denied(CapabilityDenial.QUALIFICATION_REQUIRED);
        return new(true, true, true, true, CapabilityDenial.NONE);
    }
}

public static class CapabilityJson
{
    public static CapabilityConfiguration Read(ReadOnlyMemory<byte> bytes)
    {
        var configuration = ArtifactJson.Read<CapabilityConfiguration>(bytes, "capability-registry");
        if (configuration.Entries.Select(e => e.Key).Distinct().Count() != configuration.Entries.Count)
            throw new ArgumentException("Ambiguous capability entries.");
        foreach (var entry in configuration.Entries)
        {
            WorkspaceValidation.ValidateCoverage(entry.Coverage);
            if (entry.AdapterImplemented && !entry.ContractDesigned
                || entry.ReadScope.Distinct(StringComparer.Ordinal).Count() != entry.ReadScope.Count
                || entry.WriteScope.Distinct(StringComparer.Ordinal).Count() != entry.WriteScope.Count
                || entry.RequiredQualifications.Distinct(StringComparer.Ordinal).Count() != entry.RequiredQualifications.Count)
                throw new ArgumentException("Invalid capability model.");
        }
        return configuration;
    }

    public static byte[] Write(CapabilityConfiguration configuration)
    {
        byte[] bytes = ArtifactJson.Serialize(configuration);
        configuration = Read(bytes);
        // Registry entries and set-valued scopes have no semantic order. Vendor arrays are never normalized.
        var canonical = configuration with
        {
            Entries = configuration.Entries.Select(e => e with
            {
                ReadScope = e.ReadScope.Order(StringComparer.Ordinal).ToArray(),
                WriteScope = e.WriteScope.Order(StringComparer.Ordinal).ToArray(),
                RequiredQualifications = e.RequiredQualifications.Order(StringComparer.Ordinal).ToArray(),
                Coverage = new Coverage(e.Coverage.Dimensions.OrderBy(d => d.Dimension).ToArray())
            }).OrderBy(e => Convert.ToHexString(ArtifactJson.Canonical(ArtifactJson.Serialize(e.Key))), StringComparer.Ordinal).ToArray()
        };
        return ArtifactJson.Canonical(ArtifactJson.Serialize(canonical));
    }
}
