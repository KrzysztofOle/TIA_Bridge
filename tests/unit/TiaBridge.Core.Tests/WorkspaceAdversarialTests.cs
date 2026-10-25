using System.Reflection;
using System.Text.Json;
using TiaBridge.Core.Capabilities;
using TiaBridge.Core.Identity;
using TiaBridge.Core.Operations;
using TiaBridge.Core.Workspace;
using Xunit;

namespace TiaBridge.Core.Tests;

public class WorkspaceAdversarialTests
{
    private static WorkspaceManifest Rehash(WorkspaceManifest manifest) => manifest with
    { ExchangeUnits = manifest.ExchangeUnits.Select(u => u with { UnitSha256 = WorkspaceHashing.Unit(u, manifest.Inventory) }).ToArray() };

    [Theory]
    [InlineData("device-duplicate")]
    [InlineData("group-duplicate")]
    [InlineData("unknown-device")]
    [InlineData("two-group-cycle")]
    [InlineData("missing-parent")]
    [InlineData("kind-mismatch")]
    [InlineData("public-id-without-scope")]
    [InlineData("empty-evidence")]
    [InlineData("missing-unit")]
    [InlineData("missing-model-hash")]
    [InlineData("missing-native-path")]
    [InlineData("unsupported-without-reason")]
    [InlineData("duplicate-coverage")]
    [InlineData("unknown-required-feature")]
    public void Invalid_manifest_graphs_are_rejected_for_their_actual_invariant(string defect)
    {
        var manifest = WorkspaceTests.Plc();
        var first = manifest.Inventory[0];
        WorkspaceObject changed = first;
        switch (defect)
        {
            case "device-duplicate": manifest = manifest with { Project = manifest.Project with { Devices = manifest.Project.Devices.Concat(manifest.Project.Devices).ToArray() } }; break;
            case "group-duplicate": manifest = manifest with { Project = manifest.Project with { Groups = manifest.Project.Groups.Concat(manifest.Project.Groups).ToArray() } }; break;
            case "unknown-device": changed = first with { VendorLocator = first.VendorLocator with { DeviceId = new ObjectId(Guid.Empty) } }; break;
            case "two-group-cycle": manifest = manifest with { Project = manifest.Project with { Groups = new[] { manifest.Project.Groups[0] with { ParentGroupId = manifest.Project.Groups[1].GroupId }, manifest.Project.Groups[1] } } }; break;
            case "missing-parent": manifest = manifest with { Project = manifest.Project with { Groups = new[] { manifest.Project.Groups[0] with { ParentGroupId = new ObjectId(Guid.Empty) }, manifest.Project.Groups[1] } } }; break;
            case "kind-mismatch": changed = first with { VendorLocator = first.VendorLocator with { Kind = "Different" } }; break;
            case "public-id-without-scope": changed = first with { VendorLocator = first.VendorLocator with { PublicId = "synthetic" } }; break;
            case "empty-evidence": changed = first with { VendorLocator = first.VendorLocator with { IdentityEvidence = Array.Empty<string>() } }; break;
            case "missing-unit": changed = first with { ExchangeUnitId = null }; break;
            case "missing-model-hash": changed = first with { ModelSha256 = null }; break;
            case "missing-native-path": changed = first with { FilePaths = first.FilePaths.Take(1).ToArray() }; break;
            case "unsupported-without-reason": changed = first with { Status = InventoryStatus.UNSUPPORTED, Reason = null }; break;
            case "duplicate-coverage": changed = first with { Coverage = new Coverage(first.Coverage.Dimensions.Concat(first.Coverage.Dimensions).ToArray()) }; break;
            case "unknown-required-feature":
                var node = System.Text.Json.Nodes.JsonNode.Parse(WorkspaceJson.WriteManifest(manifest))!;
                node["required_features"] = new System.Text.Json.Nodes.JsonArray("future");
                Assert.Throws<ArgumentException>(() => WorkspaceJson.ReadManifest(JsonSerializer.SerializeToUtf8Bytes(node)));
                return;
        }
        manifest = manifest with { Inventory = manifest.Inventory.Select(o => o == first ? changed : o).ToArray() };
        // Recompute digests so a stale hash does not hide the graph defect under test.
        if (defect is not ("missing-unit" or "missing-native-path")) manifest = Rehash(manifest);
        Assert.Throws<ArgumentException>(() => WorkspaceJson.WriteManifest(manifest));
    }

    [Fact]
    public void Rename_and_move_validate_as_a_new_representation_with_the_same_object_identity()
    {
        var manifest = WorkspaceTests.Plc();
        var original = manifest.Inventory[0];
        var item = original with { VendorLocator = original.VendorLocator with { OriginalName = "Renamed. ", Groups = new[] { manifest.Project.Groups[0].GroupId } } };
        string directory = WorkspaceLayout.ObjectDirectory(manifest.Project, item);
        var files = manifest.ExchangeUnits[0].Files.Select(f => f with { RelativePath = directory + "/" + f.RelativePath.Split('/')[^1] }).ToArray();
        item = item with { FilePaths = files.Select(f => f.RelativePath).ToArray() };
        var moved = Rehash(manifest with
        {
            Inventory = manifest.Inventory.Select(o => o == original ? item : o).ToArray(),
            ExchangeUnits = manifest.ExchangeUnits.Select((u, i) => i == 0 ? u with { Files = files } : u).ToArray()
        });
        var parsed = WorkspaceJson.ReadManifest(WorkspaceJson.WriteManifest(moved));
        Assert.Equal(new ObjectIdentity(manifest.ProjectKey, original.ObjectId), new ObjectIdentity(parsed.ProjectKey, parsed.Inventory[0].ObjectId));
        Assert.NotEqual(manifest.ExchangeUnits[0].UnitSha256, parsed.ExchangeUnits[0].UnitSha256);
        Assert.Equal("Renamed. ", WorkspaceLayout.DecodeObjectDirectory(parsed.Project, parsed.Inventory[0], directory).ObjectName);
    }

    [Fact]
    public void Semantically_ordered_vendor_arrays_and_locator_hierarchy_are_not_normalized_away()
    {
        Assert.NotEqual(WorkspaceHashing.Model("[1,2]"u8.ToArray(), "n", "p"), WorkspaceHashing.Model("[2,1]"u8.ToArray(), "n", "p"));
        var manifest = WorkspaceTests.Plc();
        var item = manifest.Inventory[0];
        var fields = item.VendorFields.ToDictionary(p => p.Key, p => p.Value);
        fields["UnknownFuture"] = JsonSerializer.SerializeToElement(new { KeepOrder = new[] { 2, 1 }, ExactType = "Int32" });
        var changed = Rehash(manifest with { Inventory = manifest.Inventory.Select(o => o == item ? o with { VendorFields = fields } : o).ToArray() });
        var parsed = WorkspaceJson.ReadManifest(WorkspaceJson.WriteManifest(changed));
        Assert.Equal(new[] { 2, 1 }, parsed.Inventory[0].VendorFields["UnknownFuture"].GetProperty("KeepOrder").EnumerateArray().Select(v => v.GetInt32()));
        Assert.NotEqual(manifest.ExchangeUnits[0].UnitSha256, parsed.ExchangeUnits[0].UnitSha256);
    }

    [Theory]
    [InlineData(CoverageState.PARTIAL)]
    [InlineData(CoverageState.UNKNOWN)]
    [InlineData(CoverageState.UNSUPPORTED)]
    public void Every_incomplete_coverage_state_denies_even_with_qualification_evidence(CoverageState state)
    {
        var config = CapabilityJson.Read(WorkspaceTests.Fixture("capabilities.synthetic.json"));
        var entry = config.Entries[0] with { Coverage = new Coverage(new[] { new CoverageItem(CoverageDimension.CONTENT, state, "Synthetic gap") }) };
        var registry = new CapabilityRegistry(config with { Entries = new[] { entry } });
        var query = new CapabilityQuery(entry.Key, config.RegistryVersion, entry.NormalizerVersion, entry.ComparisonProfile,
            new[] { "content" }, Array.Empty<string>(), new[] { CoverageDimension.CONTENT });
        var result = registry.Assess(query, new HashSet<string>(entry.RequiredQualifications));
        Assert.Equal(CapabilityDenial.COVERAGE_GAP, result.Denial);
        Assert.False(result.CapabilityConfirmed);
        Assert.Equal(SessionPermissionState.NOT_EVALUATED, result.SessionPermission);
    }

    [Fact]
    public void Reparse_observations_are_model_data_and_cannot_claim_a_safe_missing_ancestor()
    {
        var observations = new[] { new PathObservation("C:\\", FileAttributes.Directory), new PathObservation("C:\\work", FileAttributes.Normal), new PathObservation("C:\\work\\code.scl", FileAttributes.Normal) };
        Assert.Throws<ArgumentException>(() => WorkspacePath.ValidateObservations("C:\\work", "code.scl", observations));
        Assert.Throws<ArgumentException>(() => WorkspacePath.ValidateObservations("C:\\work", "code.scl", observations.Reverse().ToArray()));
    }

    [Fact]
    public void Core_transitive_assembly_dependencies_remain_vendor_and_host_free()
    {
        var pending = new Queue<Assembly>(); pending.Enqueue(typeof(OperationRequest).Assembly);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (pending.TryDequeue(out var assembly))
            foreach (var reference in assembly.GetReferencedAssemblies())
            {
                if (!visited.Add(reference.FullName)) continue;
                Assert.DoesNotContain(new[] { "Siemens", "PLCSIM", "WinCC", "TiaBridge.Host", "TiaBridge.Worker" }, name => reference.Name!.Contains(name, StringComparison.OrdinalIgnoreCase));
                pending.Enqueue(Assembly.Load(reference));
            }
        Assert.Contains(visited, name => name.StartsWith("JsonSchema.Net,", StringComparison.Ordinal));
    }
}
