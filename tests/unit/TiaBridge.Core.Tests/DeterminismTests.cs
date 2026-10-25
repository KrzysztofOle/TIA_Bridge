using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TiaBridge.Core.Capabilities;
using TiaBridge.Core.Identity;
using TiaBridge.Core.Workspace;
using Xunit;

namespace TiaBridge.Core.Tests;

public class DeterminismTests
{
    public static IEnumerable<object[]> Cases => JsonNode.Parse(WorkspaceTests.Fixture("core-determinism.v1.json"))!.AsArray()
        .Select(n => new object[] { n!["case_id"]!.GetValue<int>(), n.ToJsonString() });

    [Theory]
    [MemberData(nameof(Cases))]
    public void Golden_models_paths_manifests_and_capabilities_are_repeatable_without_input_mutation(int caseId, string json)
    {
        var vector = JsonNode.Parse(json)!;
        Assert.Equal(caseId, vector["case_id"]!.GetValue<int>());
        string name = vector["original_name"]!.GetValue<string>();
        var id = new ObjectId(Guid.Parse(vector["object_id"]!.GetValue<string>()));
        string normalizer = vector["normalizer"]!.GetValue<string>();
        string profile = vector["profile"]!.GetValue<string>();
        var baseline = WorkspaceTests.Plc();
        byte[] untouchedBaseline = WorkspaceJson.WriteManifest(baseline);
        var original = baseline.Inventory[0];
        var changed = original with
        {
            ObjectId = id,
            VendorLocator = original.VendorLocator with { OriginalName = name },
            NormalizerVersion = normalizer,
            ComparisonProfile = profile,
            ModelSha256 = vector["model_sha256"]!.GetValue<string>()
        };
        string directory = WorkspaceLayout.ObjectDirectory(baseline.Project, changed);
        Assert.EndsWith("/" + vector["expected_segment"]!.GetValue<string>(), directory);
        Assert.Equal(name, WorkspaceLayout.DecodeObjectDirectory(baseline.Project, changed, directory).ObjectName);
        var files = baseline.ExchangeUnits[0].Files.Select(f => f with { RelativePath = directory + "/" + f.RelativePath.Split('/')[^1] }).ToArray();
        changed = changed with { FilePaths = files.Select(f => f.RelativePath).ToArray() };
        var inventory = baseline.Inventory.Select(o => o == original ? changed : o with
        { Dependencies = o.Dependencies.Select(d => d.TargetObjectId == original.ObjectId ? d with { TargetObjectId = id } : d).ToArray() }).ToArray();
        var unit = baseline.ExchangeUnits[0] with { Members = new[] { id }, Files = files };
        var manifest = baseline with
        {
            Inventory = inventory,
            ExchangeUnits = baseline.ExchangeUnits.Select(u => u == baseline.ExchangeUnits[0] ? unit : u)
                .Select(u => u with { UnitSha256 = WorkspaceHashing.Unit(u, inventory) }).ToArray()
        };
        byte[] before = WorkspaceJson.WriteManifest(manifest);
        byte[] model = JsonSerializer.SerializeToUtf8Bytes(vector["model"]);
        string modelHash = WorkspaceHashing.Model(model, normalizer, profile);
        Assert.Equal(vector["model_sha256"]!.GetValue<string>(), modelHash);
        Assert.Equal(vector["raw_sha256"]!.GetValue<string>(), WorkspaceHashing.Raw(Encoding.UTF8.GetBytes($"synthetic-{caseId}\r\n")));
        var shuffledModel = new JsonObject(vector["model"]!.AsObject().Reverse().Select(p => new KeyValuePair<string, JsonNode?>(p.Key, p.Value?.DeepClone())));
        Assert.Equal(modelHash, WorkspaceHashing.Model(JsonSerializer.SerializeToUtf8Bytes(shuffledModel), normalizer, profile));
        Assert.NotEqual(modelHash, WorkspaceHashing.Model(model, normalizer + "-changed", profile));
        var permuted = manifest with
        {
            Inventory = inventory.Reverse().Select(o => o with { FilePaths = o.FilePaths.Reverse().ToArray(), Coverage = new Coverage(o.Coverage.Dimensions.Reverse().ToArray()) }).ToArray(),
            ExchangeUnits = manifest.ExchangeUnits.Reverse().Select(u => u with { Files = u.Files.Reverse().ToArray(), Members = u.Members.Reverse().ToArray() }).ToArray()
        };
        foreach (var u in permuted.ExchangeUnits) Assert.Equal(u.UnitSha256, WorkspaceHashing.Unit(u, permuted.Inventory));
        _ = WorkspaceJson.WriteManifest(permuted);

        var config = CapabilityJson.Read(WorkspaceTests.Fixture("capabilities.synthetic.json"));
        int variant = vector["variant"]!.GetValue<int>();
        var entry = config.Entries[0] with
        {
            NormalizerVersion = normalizer,
            ComparisonProfile = profile,
            AdapterImplemented = variant != 3,
            Coverage = new Coverage(config.Entries[0].Coverage.Dimensions.Select(d => variant == 1 && d.Dimension == CoverageDimension.CONTENT
                ? d with { State = CoverageState.PARTIAL, Reason = "Synthetic gap" } : d).ToArray())
        };
        config = config with { Entries = new[] { entry } };
        byte[] configBefore = CapabilityJson.Write(config);
        var registry = new CapabilityRegistry(config);
        var query = new CapabilityQuery(variant == 4 ? entry.Key with { ObjectKind = "Unknown" } : entry.Key,
            config.RegistryVersion, normalizer, profile, new[] { "content" }, Array.Empty<string>(), new[] { CoverageDimension.CONTENT });
        var evidence = variant == 2 ? null : new HashSet<string>(entry.RequiredQualifications, StringComparer.Ordinal);
        var assessment = registry.Assess(query, evidence);
        Assert.Equal(new[] { CapabilityDenial.NONE, CapabilityDenial.COVERAGE_GAP, CapabilityDenial.QUALIFICATION_REQUIRED,
            CapabilityDenial.NOT_IMPLEMENTED, CapabilityDenial.MISSING_CAPABILITY }[variant], assessment.Denial);
        Assert.Equal(variant == 0, assessment.CapabilityConfirmed);
        Assert.Equal(SessionPermissionState.NOT_EVALUATED, assessment.SessionPermission);
        var reordered = config with { Entries = config.Entries.Select(e => e with { ReadScope = e.ReadScope.Reverse().ToArray(), Coverage = new Coverage(e.Coverage.Dimensions.Reverse().ToArray()) }).Reverse().ToArray() };
        Assert.Equal(registry.CapabilitySetHash, new CapabilityRegistry(reordered).CapabilitySetHash);

        // Same suffix plus case, NFC or shortened-name aliases must fail closed; no new identity is guessed.
        var collisionId = new ObjectId(Guid.Parse("11111111" + id.ToString()[8..]));
        string encoded = WorkspaceNameCodec.Encode(name, id);
        string colliding = WorkspaceNameCodec.Encode(name.Normalize(NormalizationForm.FormC), collisionId);
        Assert.Throws<ArgumentException>(() => WorkspacePath.ValidateUnique(new[] { encoded, colliding }));
        foreach (string culture in new[] { "pl-PL", "tr-TR" })
        {
            var old = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                Assert.Equal(modelHash, WorkspaceHashing.Model(model, normalizer, profile));
                Assert.Equal(encoded, WorkspaceNameCodec.Encode(name, id));
                Assert.Equal(registry.CapabilitySetHash, new CapabilityRegistry(config).CapabilitySetHash);
            }
            finally { CultureInfo.CurrentCulture = old; }
        }
        Assert.Equal(before, WorkspaceJson.WriteManifest(manifest));
        Assert.Equal(before, WorkspaceJson.WriteManifest(WorkspaceJson.ReadManifest(before)));
        Assert.Equal(untouchedBaseline, WorkspaceJson.WriteManifest(baseline));
        Assert.Equal(configBefore, CapabilityJson.Write(config));
        Assert.Equal(new ObjectIdentity(baseline.ProjectKey, id), new ObjectIdentity(baseline.ProjectKey, changed.ObjectId));
    }
}
