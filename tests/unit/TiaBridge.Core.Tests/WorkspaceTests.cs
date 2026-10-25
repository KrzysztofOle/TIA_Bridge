using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TiaBridge.Core.Identity;
using TiaBridge.Core.Workspace;
using Xunit;

namespace TiaBridge.Core.Tests;

public class WorkspaceTests
{
    internal static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
    internal static WorkspaceManifest Plc() => WorkspaceJson.ReadManifest(Fixture("plc.manifest.json"));
    private static readonly ObjectId Id = new(Guid.Parse("11111111-1111-4111-8111-123456789abc"));
    private static readonly ObjectId OtherId = new(Guid.Parse("11111111-1111-4111-8111-123456789abd"));

    [Theory]
    [InlineData("Pump")]
    [InlineData("CON")]
    [InlineData("com1.txt")]
    [InlineData("LPT²")]
    [InlineData("NUL.")]
    [InlineData("tail. ")]
    [InlineData("<>:\"/\\|?*%")]
    [InlineData(".\u0001")]
    [InlineData("..")]
    [InlineData("e\u0301")]
    [InlineData("日本語 😀")]
    public void Name_codec_preserves_original_metadata_and_produces_safe_segments(string name)
    {
        string encoded = WorkspaceNameCodec.Encode(name, Id);
        WorkspacePath.ValidateRelative(encoded);
        Assert.Equal(name, WorkspaceNameCodec.Decode(encoded, name, Id));
        Assert.EndsWith("--123456789abc", encoded);
        for (int i = 0; i < 100; i++) Assert.Equal(encoded, WorkspaceNameCodec.Encode(name, Id));
        Assert.NotEqual(encoded, WorkspaceNameCodec.Encode(name, OtherId));
        Assert.Throws<ArgumentException>(() => WorkspaceNameCodec.Decode(encoded, name, OtherId));
    }

    [Fact]
    public void Escaping_is_percent_utf8_and_reserved_prefix_is_explicit()
    {
        Assert.Equal("A%2FB%25%3AC--123456789abc", WorkspaceNameCodec.Encode("A/B%:C", Id));
        Assert.StartsWith("_CON--", WorkspaceNameCodec.Encode("CON", Id));
        Assert.StartsWith("_tail. --", WorkspaceNameCodec.Encode("tail. ", Id));
        Assert.Equal(WorkspaceNameCodec.Encode("é", Id), WorkspaceNameCodec.Encode("e\u0301", Id));
        Assert.NotEqual(WorkspaceNameCodec.Encode("Pump", Id).ToUpperInvariant(), WorkspaceNameCodec.Encode("pump", OtherId).ToUpperInvariant());
        Assert.Throws<ArgumentException>(() => WorkspaceNameCodec.Encode("", Id));
        Assert.Throws<ArgumentException>(() => WorkspaceNameCodec.Encode("\ud800", Id));
        Assert.Throws<ArgumentException>(() => WorkspaceNameCodec.Encode(new string('x', 32769), Id));
    }

    [Fact]
    public void Long_names_are_shortened_without_splitting_runes_or_escapes()
    {
        foreach (string name in new[] { new string('a', 200), string.Concat(Enumerable.Repeat("😀/", 100)) })
        {
            string result = WorkspaceNameCodec.Encode(name, Id);
            Assert.InRange(result.Length, 90, 100);
            Assert.Equal(name, WorkspaceNameCodec.Decode(result, name, Id));
            Assert.Equal(result, WorkspaceNameCodec.Encode(name, Id));
            WorkspacePath.ValidateRelative(result);
            Assert.NotEqual(result, WorkspaceNameCodec.Encode(name, OtherId));
        }
        // A collision in the short UUID suffix is an explicit conflict, never a guessed identity.
        var collisionId = new ObjectId(Guid.Parse("22222222-1111-4111-8111-123456789abc"));
        var paths = new[] { WorkspaceNameCodec.Encode(new string('a', 200) + "x", Id), WorkspaceNameCodec.Encode(new string('a', 200) + "y", collisionId) };
        Assert.Throws<ArgumentException>(() => WorkspacePath.ValidateUnique(paths));
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("a/../b")]
    [InlineData("a/./b")]
    [InlineData("/absolute")]
    [InlineData("C:/absolute")]
    [InlineData("C:relative")]
    [InlineData("\\\\server\\share")]
    [InlineData("\\\\?\\C:\\work")]
    [InlineData("a\\b")]
    [InlineData("a//b")]
    [InlineData("file:stream")]
    [InlineData("a/NUL.txt")]
    [InlineData("a/COM9")]
    [InlineData("a/PRN")]
    [InlineData("a/CON .txt")]
    [InlineData("a/CONIN$")]
    [InlineData("a/space ")]
    [InlineData("a/dot.")]
    [InlineData("a/e\u0301")]
    [InlineData("a/*")]
    [InlineData("a/\u0001")]
    public void Unsafe_workspace_paths_are_rejected(string path) => Assert.Throws<ArgumentException>(() => WorkspacePath.ValidateRelative(path));

    [Theory]
    [InlineData("relative")]
    [InlineData("C:relative")]
    [InlineData("C:/work")]
    [InlineData("\\\\server\\share")]
    [InlineData("C:\\work\\..\\outside")]
    [InlineData("C:\\work:stream")]
    public void Root_is_a_local_absolute_windows_path(string root) => Assert.Throws<ArgumentException>(() => WorkspacePath.Resolve(root, "code.scl"));

    [Fact]
    public void Path_lengths_and_collisions_fail_closed()
    {
        string relative = new string('a', 100) + "/" + new string('b', 100) + "/" + new string('c', 35);
        Assert.Equal(240, WorkspacePath.Resolve("C:\\", relative).Length);
        Assert.Throws<ArgumentException>(() => WorkspacePath.Resolve("C:\\x", relative));
        Assert.Throws<ArgumentException>(() => WorkspacePath.ValidateRelative(new string('a', 101)));
        Assert.Equal("C:\\work\\a%2Fb", WorkspacePath.Resolve("C:\\work", "a%2Fb"));
        foreach (var paths in new[] { new[] { "A/code.scl", "a/native.xml" }, new[] { "A", "a" }, new[] { "a", "a/b" } })
            Assert.Throws<ArgumentException>(() => WorkspacePath.ValidateUnique(paths));
    }

    [Fact]
    public void Observations_require_root_and_every_component_and_reject_reparse_points()
    {
        var observations = new[] { new PathObservation("C:\\", FileAttributes.Directory), new PathObservation("C:\\work", FileAttributes.Directory), new PathObservation("C:\\work\\a", FileAttributes.Directory), new PathObservation("C:\\work\\a\\code.scl", FileAttributes.Normal) };
        WorkspacePath.ValidateObservations("C:\\work", "a/code.scl", observations);
        Assert.Throws<ArgumentException>(() => WorkspacePath.ValidateObservations("C:\\work", "a/code.scl", observations[1..]));
        for (int i = 0; i < observations.Length; i++)
        {
            var bad = observations.ToArray();
            bad[i] = bad[i] with { Attributes = bad[i].Attributes | FileAttributes.ReparsePoint };
            Assert.Throws<ArgumentException>(() => WorkspacePath.ValidateObservations("C:\\work", "a/code.scl", bad));
        }
        var wrong = observations.ToArray();
        wrong[1] = wrong[1] with { AbsolutePath = "C:\\other" };
        Assert.Throws<ArgumentException>(() => WorkspacePath.ValidateObservations("C:\\work", "a/code.scl", wrong));
    }

    [Theory]
    [InlineData("plc.manifest.json")]
    [InlineData("hmi.manifest.json")]
    public void Examples_round_trip_validate_binding_and_preserve_unknown_vendor_data(string file)
    {
        var manifest = WorkspaceJson.ReadManifest(Fixture(file));
        var config = WorkspaceJson.ReadConfiguration(Fixture("workspace.synthetic.json"));
        WorkspaceJson.ValidatePaths(manifest, config);
        foreach (var item in manifest.Inventory)
        {
            string path = WorkspaceLayout.ObjectDirectory(manifest.Project, item);
            var names = WorkspaceLayout.DecodeObjectDirectory(manifest.Project, item, path);
            Assert.Equal(item.VendorLocator.OriginalName, names.ObjectName);
            Assert.Equal(manifest.Project.Devices.Single(d => d.DeviceId == item.VendorLocator.DeviceId).OriginalName, names.DeviceName);
            Assert.Equal(item.VendorLocator.Groups.Select(id => manifest.Project.Groups.Single(g => g.GroupId == id).OriginalName), names.GroupNames);
            Assert.Throws<ArgumentException>(() => WorkspaceLayout.DecodeObjectDirectory(manifest.Project, item, path + "x"));
        }
        byte[] first = WorkspaceJson.WriteManifest(manifest);
        byte[] second = WorkspaceJson.WriteManifest(WorkspaceJson.ReadManifest(first));
        Assert.Equal(first, second);
        Assert.Equal("1.25", manifest.Inventory[0].VendorFields["SyntheticUnknown"].GetProperty("Decimal").GetRawText());
        Assert.Equal(new[] { 2, 1 }, manifest.Inventory[0].VendorFields["SyntheticUnknown"].GetProperty("KeepOrder").EnumerateArray().Select(e => e.GetInt32()));
        Assert.Equal(WorkspaceJson.WriteConfiguration(config), WorkspaceJson.WriteConfiguration(WorkspaceJson.ReadConfiguration(WorkspaceJson.WriteConfiguration(config))));
        Assert.Throws<ArgumentException>(() => WorkspaceJson.ValidatePaths(manifest, config with { WorkspaceId = Guid.NewGuid() }));
    }

    [Fact]
    public void Identity_survives_rename_and_move_but_locator_and_path_change()
    {
        var manifest = Plc();
        var item = manifest.Inventory[0];
        var renamed = item with { VendorLocator = item.VendorLocator with { OriginalName = "Renamed", Groups = Array.Empty<ObjectId>() } };
        Assert.Equal(new ObjectIdentity(manifest.ProjectKey, item.ObjectId), new ObjectIdentity(manifest.ProjectKey, renamed.ObjectId));
        Assert.NotEqual(WorkspaceLayout.ObjectDirectory(manifest.Project, item), WorkspaceLayout.ObjectDirectory(manifest.Project, renamed));
        var caseRenamed = item with { VendorLocator = item.VendorLocator with { OriginalName = "pump" } };
        Assert.Equal(item.ObjectId, caseRenamed.ObjectId);
        Assert.Equal(WorkspaceLayout.ObjectDirectory(manifest.Project, item), WorkspaceLayout.ObjectDirectory(manifest.Project, caseRenamed), ignoreCase: true);
    }

    [Fact]
    public void Shared_hmi_exchange_unit_retains_all_native_members_and_coverage_gaps()
    {
        var manifest = WorkspaceJson.ReadManifest(Fixture("hmi.manifest.json"));
        var unit = Assert.Single(manifest.ExchangeUnits);
        Assert.Equal(2, unit.Members.Count);
        Assert.Contains(unit.Files, f => f.Format == "hmi.js" && f.Role == RepresentationRole.NATIVE);
        Assert.Contains(unit.Files, f => f.Format == "hmi.yml" && f.Role == RepresentationRole.NATIVE);
        Assert.All(manifest.Inventory.Take(2), o => Assert.Equal(unit.Files.Select(f => f.RelativePath), o.FilePaths));
        Assert.Contains(manifest.Inventory[1].Dependencies, d => d.TargetObjectId == manifest.Inventory[0].ObjectId && d.Relation == "contained_by");
        Assert.Contains(CoverageDimension.API, manifest.Coverage.Missing(Enum.GetValues<CoverageDimension>()));
        Assert.NotEmpty(new Coverage(Array.Empty<CoverageItem>()).Missing(Enum.GetValues<CoverageDimension>()));
        Assert.Contains(manifest.Inventory, o => o.Status == InventoryStatus.UNSUPPORTED && o.Reason != null);
    }

    [Fact]
    public void Manifest_rejects_invalid_graphs_omitted_native_files_and_tampered_digests()
    {
        var manifest = Plc();
        var item = manifest.Inventory[0];
        var unit = manifest.ExchangeUnits[0];
        foreach (var invalid in new[]
        {
            manifest with { ProjectKey = new ProjectKey(Guid.NewGuid()) },
            manifest with { Inventory = new[] { item, item } },
            manifest with { Project = manifest.Project with { Groups = new[] { manifest.Project.Groups[0] with { ParentGroupId = manifest.Project.Groups[0].GroupId } } } },
            manifest with { Inventory = manifest.Inventory.Select(o => o == item ? o with { VendorLocator = o.VendorLocator with { Groups = o.VendorLocator.Groups.Reverse().ToArray() } } : o).ToArray() },
            manifest with { Inventory = manifest.Inventory.Select(o => o == item ? o with { Dependencies = new[] { new ObjectRelation(Id, "uses") } } : o).ToArray() },
            manifest with { ExchangeUnits = manifest.ExchangeUnits.Select(u => u == unit ? u with { Files = u.Files.Where(f => f.Role != RepresentationRole.NATIVE).ToArray() } : u).ToArray() },
            manifest with { ExchangeUnits = manifest.ExchangeUnits.Select(u => u == unit ? u with { UnitSha256 = new string('f', 64) } : u).ToArray() },
            manifest with { Coverage = new Coverage(new[] { new CoverageItem(CoverageDimension.API, CoverageState.PARTIAL) }) }
        }) Assert.Throws<ArgumentException>(() => WorkspaceJson.WriteManifest(invalid));
    }

    [Fact]
    public void Hashes_are_exact_repeatable_versioned_and_sensitive_to_metadata()
    {
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", WorkspaceHashing.Raw("abc"u8));
        string first = WorkspaceHashing.Model("{\"b\":1e0,\"a\":\"\\u0061\"}"u8.ToArray(), "n1", "p1");
        Assert.Equal("dd0f73a225fe2ba05545a0f8f3daa3d52b376c551eedc6fd21cc276ea0870960", first);
        for (int i = 0; i < 100; i++) Assert.Equal(first, WorkspaceHashing.Model("{\"a\":\"a\",\"b\":1.00}"u8.ToArray(), "n1", "p1"));
        Assert.NotEqual(first, WorkspaceHashing.Model("{\"a\":\"a\",\"b\":1}"u8.ToArray(), "n2", "p1"));
        Assert.NotEqual(first, WorkspaceHashing.Model("{\"a\":\"a\",\"b\":1}"u8.ToArray(), "n1", "p2"));
        Assert.Equal(WorkspaceHashing.Model("1.2500"u8.ToArray(), "n", "p"), WorkspaceHashing.Model("125e-2"u8.ToArray(), "n", "p"));
        Assert.NotEqual(WorkspaceHashing.Model("1.00000000000000000000000000001"u8.ToArray(), "n", "p"), WorkspaceHashing.Model("1"u8.ToArray(), "n", "p"));
        Assert.NotEqual(WorkspaceHashing.Raw("a\r\n"u8), WorkspaceHashing.Raw("a\n"u8));
        var manifest = Plc();
        var unit = manifest.ExchangeUnits[0];
        Assert.Equal(unit.UnitSha256, WorkspaceHashing.Unit(unit with { Files = unit.Files.Reverse().ToArray() }, manifest.Inventory.Reverse()));
        Assert.NotEqual(unit.UnitSha256, WorkspaceHashing.Unit(unit, manifest.Inventory.Select(o => o with { NormalizerVersion = "changed" })));
        Assert.NotEqual(unit.UnitSha256, WorkspaceHashing.Unit(unit with { Files = unit.Files.Select(f => f with { RawSha256 = new string('f', 64) }).ToArray() }, manifest.Inventory));
    }

    [Theory]
    [InlineData("{\"a\":1,\"\\u0061\":2}")]
    [InlineData("NaN")]
    [InlineData("1e1001")]
    [InlineData("[1,]")]
    [InlineData("{ /* comment */ }")]
    [InlineData("\"\\uD800\"")]
    public void Untrusted_model_json_fails_closed(string json) => Assert.ThrowsAny<ArgumentException>(() => WorkspaceHashing.Model(Encoding.UTF8.GetBytes(json), "n", "p"));

    [Fact]
    public void Artifact_limits_unknown_fields_versions_nulls_and_invalid_utf8_are_rejected()
    {
        foreach (var mutation in new Action<JsonNode>[]
        {
            n => n["schema_version"] = "2.0", n => n["unknown"] = "untrusted-value",
            n => n["inventory"]![0]!["unknown"] = 1, n => n["project_key"] = "bad-uuid",
            n => n["inventory"]![0]!["object_id"] = null, n => n.AsObject().Remove("coverage"),
            n => n["inventory"]![0]!["status"] = "SYNCED"
        })
        {
            var node = JsonNode.Parse(Fixture("plc.manifest.json"))!;
            mutation(node);
            var error = Assert.Throws<ArgumentException>(() => WorkspaceJson.ReadManifest(Encoding.UTF8.GetBytes(node.ToJsonString())));
            Assert.DoesNotContain("untrusted-value", error.Message);
        }
        Assert.Throws<ArgumentException>(() => WorkspaceJson.ReadManifest(new byte[] { 0xff }));
        Assert.Throws<ArgumentException>(() => WorkspaceJson.ReadManifest(new byte[10 * 1024 * 1024 + 1]));
        Assert.Throws<ArgumentException>(() => WorkspaceHashing.Model(Encoding.UTF8.GetBytes(new string('[', 33) + "0" + new string(']', 33)), "n", "p"));
        // Expansion is bounded too, rather than allocating a huge canonical result.
        string expansion = "[" + string.Join(',', Enumerable.Repeat("1e1000", 11000)) + "]";
        Assert.Throws<ArgumentException>(() => WorkspaceHashing.Model(Encoding.UTF8.GetBytes(expansion), "n", "p"));
    }

    [Fact]
    public void Invalid_unicode_is_never_replaced_when_writing_typed_metadata()
    {
        var manifest = Plc();
        Assert.Throws<ArgumentException>(() => WorkspaceJson.WriteManifest(manifest with
        { Project = manifest.Project with { OriginalName = "\ud800" } }));
        var fields = new Dictionary<string, JsonElement> { ["\ud800"] = JsonSerializer.SerializeToElement(1) };
        var unit = manifest.ExchangeUnits[0] with { VendorFields = fields };
        Assert.Throws<ArgumentException>(() => WorkspaceHashing.Unit(unit, manifest.Inventory));
    }
}
