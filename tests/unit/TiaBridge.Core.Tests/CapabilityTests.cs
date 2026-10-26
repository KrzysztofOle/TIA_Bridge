using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TiaBridge.Core.Capabilities;
using TiaBridge.Core.Operations;
using TiaBridge.Core.Workspace;
using Xunit;

namespace TiaBridge.Core.Tests;

public class CapabilityTests
{
    private static readonly string[] DiscoveryGetters =
    [
        "TiaPortal.GetProcesses", "TiaPortalProcess.Attach", "TiaPortal.Projects", "Project.Path", "Project.Name",
        "Project.IsModified", "Project.Devices", "Device.Name", "Device.DeviceItems", "DeviceItem.Name",
        "DeviceItem.DeviceItems", "DeviceItem.GetService<SoftwareContainer>", "SoftwareContainer.Software"
    ];
    private static CapabilityKeyV2 ProjectDiscoveryKey(string tia = "19.0.3.0", string? worker = null, OperationMethod operation = OperationMethod.ProjectInspect) =>
        new(OperationScope.PROJECT, tia, "tia-bridge.worker.v19", worker ?? "1.0+" + new string('a', 64), null, "project", "v19.discovery.v1", null, operation);
    private static CapabilityConfigurationV2 ProjectDiscoveryConfiguration(IReadOnlyList<string>? readScope = null) =>
        new("2.0", "synthetic-project-2", [new(ProjectDiscoveryKey(), true, true, "1.0", "v19.discovery.v1",
            readScope ?? DiscoveryGetters, [], new Coverage([new(CoverageDimension.STRUCTURE, CoverageState.COMPLETE)]), ["synthetic-discovery-receipt"])]);
    private static CapabilityQueryV2 ProjectDiscoveryQuery(CapabilityKeyV2? key = null, IReadOnlyList<string>? readScope = null, IReadOnlyList<string>? writeScope = null) =>
        new(key ?? ProjectDiscoveryKey(), "synthetic-project-2", "1.0", "v19.discovery.v1", readScope ?? DiscoveryGetters,
            writeScope ?? [], [CoverageDimension.STRUCTURE]);

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

    [Theory]
    [InlineData("EMPTY")]
    [InlineData("PLC")]
    [InlineData("HMI_UNIFIED")]
    [InlineData("MIXED_PLC_HMI")]
    [InlineData("UNKNOWN_FAMILY")]
    public void Project_discovery_capability_is_independent_of_device_inventory(string projectContents)
    {
        _ = projectContents; // Device identities/families are discovered after the project-scope preflight.
        var registry = new CapabilityRegistry(ProjectDiscoveryConfiguration());
        var result = registry.Assess(ProjectDiscoveryQuery(), new HashSet<string>(["synthetic-discovery-receipt"], StringComparer.Ordinal));
        Assert.True(result.CapabilityConfirmed);
        Assert.Equal(SessionPermissionState.NOT_EVALUATED, result.SessionPermission);
    }

    [Fact]
    public void Project_discovery_requires_exact_build_profile_and_every_named_getter_qualification()
    {
        var config = ProjectDiscoveryConfiguration();
        var registry = new CapabilityRegistry(config);
        var evidence = new HashSet<string>(["synthetic-discovery-receipt"], StringComparer.Ordinal);
        Assert.Equal(CapabilityDenial.MISSING_CAPABILITY, registry.Assess(ProjectDiscoveryQuery(key: ProjectDiscoveryKey(tia: "unknown")), evidence).Denial);
        Assert.Equal(CapabilityDenial.MISSING_CAPABILITY, registry.Assess(ProjectDiscoveryQuery(key: ProjectDiscoveryKey(worker: "unknown")), evidence).Denial);
        Assert.Equal(CapabilityDenial.MISSING_CAPABILITY, registry.Assess(ProjectDiscoveryQuery(key: ProjectDiscoveryKey(operation: OperationMethod.ProjectExport)), evidence).Denial);
        Assert.Equal(CapabilityDenial.MISSING_CAPABILITY, registry.Assess(ProjectDiscoveryQuery(key: ProjectDiscoveryKey() with { Profile = "generic-project" }), evidence).Denial);
        var missingGetter = new CapabilityRegistry(ProjectDiscoveryConfiguration(DiscoveryGetters[..^1]));
        Assert.Equal(CapabilityDenial.SCOPE_NOT_COVERED, missingGetter.Assess(ProjectDiscoveryQuery(), evidence).Denial);
        Assert.Equal(CapabilityDenial.QUALIFICATION_REQUIRED, registry.Assess(ProjectDiscoveryQuery(), null).Denial);
    }

    [Fact]
    public void Project_read_does_not_inherit_device_object_or_write_permissions()
    {
        var registry = new CapabilityRegistry(ProjectDiscoveryConfiguration());
        var evidence = new HashSet<string>(["synthetic-discovery-receipt"], StringComparer.Ordinal);
        var device = ProjectDiscoveryKey() with { Scope = OperationScope.DEVICE, DeviceFamily = DeviceFamily.PLC, ObjectKind = "device" };
        var item = ProjectDiscoveryKey() with { Scope = OperationScope.OBJECT, DeviceFamily = DeviceFamily.HMI_UNIFIED, ObjectKind = "screen" };
        Assert.Equal(CapabilityDenial.MISSING_CAPABILITY, registry.Assess(ProjectDiscoveryQuery(key: device), evidence).Denial);
        Assert.Equal(CapabilityDenial.MISSING_CAPABILITY, registry.Assess(ProjectDiscoveryQuery(key: item), evidence).Denial);
        Assert.Equal(CapabilityDenial.SCOPE_NOT_COVERED, registry.Assess(ProjectDiscoveryQuery(writeScope: ["project.write"]), evidence).Denial);
        foreach (var operation in new[] { OperationMethod.ProjectExport, OperationMethod.SyncApply })
            Assert.Equal(CapabilityDenial.MISSING_CAPABILITY, registry.Assess(ProjectDiscoveryQuery(key: ProjectDiscoveryKey(operation: operation)), evidence).Denial);
    }

    [Fact]
    public void Capability_registry_v2_round_trips_without_changing_v1_bytes_and_rejects_inconsistent_project_family()
    {
        var v1 = CapabilityJson.Write(Example());
        var v2 = CapabilityJson.WriteV2(ProjectDiscoveryConfiguration());
        Assert.Equal(v1, CapabilityJson.Write(CapabilityJson.Read(v1)));
        Assert.Equal(v2, CapabilityJson.WriteV2(CapabilityJson.ReadV2(v2)));
        using (var json = JsonDocument.Parse(v2))
        {
            var key = json.RootElement.GetProperty("entries")[0].GetProperty("key");
            Assert.Equal("PROJECT", key.GetProperty("scope").GetString());
            Assert.Equal(JsonValueKind.Null, key.GetProperty("device_family").ValueKind);
        }
        foreach (var mutation in new Action<System.Text.Json.Nodes.JsonNode>[]
        {
            node => node["schema_version"] = "1.0",
            node => node["entries"]![0]!["key"]!["device_family"] = "PLC",
            node => node["entries"]![0]!["key"]!["scope"] = "UNKNOWN",
            node => node["entries"]![0]!["key"]!["operation"] = "project.import",
            node => node["entries"]![0]!["key"]!["unreviewed"] = true
        })
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(System.Text.Encoding.UTF8.GetString(v2))!;
            mutation(node);
            Assert.Throws<ArgumentException>(() => CapabilityJson.ReadV2(System.Text.Encoding.UTF8.GetBytes(node.ToJsonString())));
        }
        Assert.Equal(CapabilityDenial.MISSING_CAPABILITY,
            CapabilityRegistry.DenyAll.Assess(ProjectDiscoveryQuery(), new HashSet<string>(["synthetic-discovery-receipt"], StringComparer.Ordinal)).Denial);
    }
}
