using System.Text;
using System.Text.Json.Nodes;
using TiaBridge.Core.Capabilities;
using TiaBridge.Core.Operations;
using TiaBridge.Core.Workspace;
using Xunit;

namespace TiaBridge.Core.Tests;

public class CapabilityTests
{
    private static CapabilityConfiguration Example() => CapabilityJson.Read(WorkspaceTests.Fixture("capabilities.synthetic.json"));
    private static CapabilityQuery Query(CapabilityEntry entry) => new(entry.Key, "synthetic-1", entry.NormalizerVersion,
        entry.ComparisonProfile, new[] { "content" }, Array.Empty<string>(), new[] { CoverageDimension.CONTENT });
    private static HashSet<string> Evidence() => new(StringComparer.Ordinal) { "synthetic-only-receipt" };

    [Fact]
    public void Default_is_deny_and_missing_capability_never_confirms_support()
    {
        var entry = Example().Entries[0];
        var query = Query(entry) with { RegistryVersion = "empty-1" };
        Assert.Equal(CapabilityDenial.MISSING_CAPABILITY, CapabilityRegistry.DenyAll.Assess(query, Evidence()).Denial);
        var registry = new CapabilityRegistry(Example());
        var missing = registry.Assess(Query(entry) with { Key = entry.Key with { ObjectKind = "Safety" } }, Evidence());
        Assert.False(missing.CapabilityConfirmed);
        Assert.False(missing.TestQualified);
        Assert.Equal(SessionPermissionState.NOT_EVALUATED, missing.SessionPermission);
    }

    [Fact]
    public void Designed_implemented_qualified_and_session_permission_are_distinct()
    {
        var config = Example();
        var registry = new CapabilityRegistry(config);
        var designedOnly = registry.Assess(Query(config.Entries[1]), Evidence());
        Assert.True(designedOnly.ContractDesigned);
        Assert.False(designedOnly.AdapterImplemented);
        Assert.False(designedOnly.TestQualified);
        Assert.Equal(CapabilityDenial.NOT_IMPLEMENTED, designedOnly.Denial);
        var implemented = registry.Assess(Query(config.Entries[0]));
        Assert.True(implemented.AdapterImplemented);
        Assert.False(implemented.CapabilityConfirmed);
        Assert.Equal(CapabilityDenial.QUALIFICATION_REQUIRED, implemented.Denial);
        var qualified = registry.Assess(Query(config.Entries[0]), Evidence());
        Assert.True(qualified.CapabilityConfirmed);
        Assert.True(qualified.TestQualified);
        Assert.Equal(SessionPermissionState.NOT_EVALUATED, qualified.SessionPermission);
        var absent = config with { Entries = new[] { config.Entries[0] with { RequiredQualifications = Array.Empty<string>() } } };
        Assert.Equal(CapabilityDenial.QUALIFICATION_REQUIRED, new CapabilityRegistry(absent).Assess(Query(config.Entries[0]), Evidence()).Denial);
        var notDesigned = config with { Entries = new[] { config.Entries[0] with { ContractDesigned = false, AdapterImplemented = false } } };
        Assert.Equal(CapabilityDenial.NOT_DESIGNED, new CapabilityRegistry(notDesigned).Assess(Query(config.Entries[0]), Evidence()).Denial);
    }

    [Theory]
    [InlineData("tia")]
    [InlineData("adapter")]
    [InlineData("build")]
    [InlineData("family")]
    [InlineData("kind")]
    [InlineData("language")]
    [InlineData("operation")]
    public void Every_capability_key_dimension_matches_exactly(string dimension)
    {
        var config = Example();
        var entry = config.Entries[0];
        var key = dimension switch
        {
            "tia" => entry.Key with { TiaVersion = "20" },
            "adapter" => entry.Key with { AdapterId = "another" },
            "build" => entry.Key with { AdapterBuild = "another" },
            "family" => entry.Key with { DeviceFamily = DeviceFamily.HMI_CLASSIC },
            "kind" => entry.Key with { ObjectKind = "fc" },
            "language" => entry.Key with { Language = "LAD" },
            _ => entry.Key with { Operation = OperationMethod.SyncApply }
        };
        Assert.Equal(CapabilityDenial.MISSING_CAPABILITY, new CapabilityRegistry(config).Assess(Query(entry) with { Key = key }, Evidence()).Denial);
    }

    [Fact]
    public void Version_scope_coverage_and_evidence_fail_closed()
    {
        var config = Example();
        var registry = new CapabilityRegistry(config);
        var query = Query(config.Entries[0]);
        foreach (var mismatch in new[] { query with { RegistryVersion = "next" }, query with { NormalizerVersion = "next" }, query with { ComparisonProfile = "next" } })
            Assert.Equal(CapabilityDenial.VERSION_MISMATCH, registry.Assess(mismatch, Evidence()).Denial);
        foreach (var mismatch in new[] { query with { WriteScope = new[] { "content" } }, query with { ReadScope = new[] { "*" } }, query with { ReadScope = Array.Empty<string>() }, query with { RequiredCoverage = Array.Empty<CoverageDimension>() } })
            Assert.Equal(CapabilityDenial.SCOPE_NOT_COVERED, registry.Assess(mismatch, Evidence()).Denial);
        Assert.Equal(CapabilityDenial.QUALIFICATION_REQUIRED, registry.Assess(query, new HashSet<string> { "another-receipt" }).Denial);
        var partial = Query(config.Entries[2]) with { RequiredCoverage = new[] { CoverageDimension.API } };
        Assert.Equal(CapabilityDenial.COVERAGE_GAP, registry.Assess(partial, Evidence()).Denial);
        var unknown = config with { Entries = new[] { config.Entries[0] with { Coverage = new Coverage(Array.Empty<CoverageItem>()) } } };
        Assert.Equal(CapabilityDenial.COVERAGE_GAP, new CapabilityRegistry(unknown).Assess(query, Evidence()).Denial);
    }

    [Fact]
    public void Registry_snapshot_and_hash_are_stable_and_bound_to_manifest()
    {
        var config = Example();
        var entries = config.Entries.ToArray();
        var registry = new CapabilityRegistry(config with { Entries = entries });
        entries[0] = entries[0] with { AdapterImplemented = false };
        Assert.True(registry.Assess(Query(config.Entries[0]), Evidence()).CapabilityConfirmed);
        var shuffled = config with { Entries = config.Entries.Reverse().Select(e => e with { ReadScope = e.ReadScope.Reverse().ToArray() }).ToArray() };
        Assert.Equal(registry.CapabilitySetHash, new CapabilityRegistry(shuffled).CapabilitySetHash);
        Assert.NotEqual(registry.CapabilitySetHash, new CapabilityRegistry(config with { RegistryVersion = "next" }).CapabilitySetHash);
        Assert.Equal(registry.CapabilitySetHash, WorkspaceTests.Plc().CapabilitySetHash);
        byte[] bytes = CapabilityJson.Write(config);
        Assert.Equal(bytes, CapabilityJson.Write(CapabilityJson.Read(bytes)));
        for (int i = 0; i < 100; i++) Assert.Equal(registry.CapabilitySetHash, new CapabilityRegistry(config).CapabilitySetHash);
    }

    [Fact]
    public void Capability_configuration_rejects_unknown_versions_fields_duplicates_and_inconsistent_claims()
    {
        var config = Example();
        var entry = config.Entries[0];
        foreach (var invalid in new[]
        {
            config with { Entries = new[] { entry, entry } },
            config with { Entries = new[] { entry with { ContractDesigned = false } } },
            config with { Entries = new[] { entry with { ReadScope = new[] { "a", "a" } } } },
            config with { Entries = new[] { entry with { Coverage = new Coverage(new[] { new CoverageItem(CoverageDimension.API, CoverageState.UNKNOWN) }) } } }
        }) Assert.Throws<ArgumentException>(() => CapabilityJson.Write(invalid));
        foreach (var mutation in new Action<JsonNode>[]
        {
            n => n["schema_version"] = "1.1", n => n["session_grant"] = "synthetic",
            n => n["entries"]![0]!["key"]!["operation"] = "ArbitraryVendorMethod",
            n => n["entries"]![0]!["key"]!["device_family"] = 1
        })
        {
            var node = JsonNode.Parse(WorkspaceTests.Fixture("capabilities.synthetic.json"))!;
            mutation(node);
            Assert.Throws<ArgumentException>(() => CapabilityJson.Read(Encoding.UTF8.GetBytes(node.ToJsonString())));
        }
    }

    [Fact]
    public void Artifact_validation_is_safe_under_concurrent_reads()
    {
        Parallel.For(0, 40, i =>
        {
            _ = WorkspaceTests.Plc();
            _ = Example();
            Assert.Throws<ArgumentException>(() => CapabilityJson.Read("{}"u8.ToArray()));
        });
    }
}
