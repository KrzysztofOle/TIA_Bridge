using System.Text;
using System.Text.Json;
using TiaBridge.Core.Hmi;
using Xunit;

namespace TiaBridge.Core.Tests;

public class HmiScriptTests
{
    // All source text is synthetic. The requested names do not contain customer implementation.
    private const string Global = "\uFEFF// definicje: Zażółć gęślą jaźń 😀\r\nimport * as lib from 'Synthetic';\r\nconst state = { text: 'export function ghost() { }' };\r\n\r\n";
    private static string Function(string name) => "export function " + name + "(value) {\r\n"
        + "  // multi-line body, braces } { and a false export function ignored() {}\r\n"
        + "  /* komentarz Unicode: Łódź 😀 */\r\n"
        + "  const text = \"escaped \\\" quote and } {\";\r\n"
        + "  const regex = /[{}]\\/export function/g;\r\n"
        + "  const template = `outer ${JSON.stringify({ nested: `inner ${value}` })}`;\r\n"
        + "  function nested() { return { text, template }; }\r\n"
        + "  return nested();\r\n}\r\n\r\n";
    private static byte[] Source() => Encoding.UTF8.GetBytes(Global + string.Concat(HmiScriptSource.CatalogFunctions.Select(Function)));
    private static NativeScriptArtifact[] Native(byte[]? source = null) =>
        [new("T05RecipeCatalog.hmi.js", source ?? Source()), new("T05RecipeCatalog.hmi.yml", Encoding.UTF8.GetBytes("# opaque metadata\r\nunknown: [żółć, 19]\r\n")), new("extra.vendor", [0, 255, 1])];
    private sealed class Temp : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "hmi-js-synthetic-" + Guid.NewGuid().ToString("N"));
        internal string PathFor(string name) => Path.Combine(Root, name);
        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }

    [Fact]
    public void Nine_functions_and_global_area_have_exact_names_bytes_ranges_and_hashes()
    {
        byte[] source = Source();
        var split = HmiScriptSource.Split(source, HmiScriptSource.CatalogFunctions);
        Assert.Equal("PASS", split.Status); Assert.Equal(10, split.Parts.Count);
        Assert.Equal("Global definition area", split.Parts[0].Name);
        Assert.Equal(Encoding.UTF8.GetBytes(Global), split.Parts[0].Content);
        foreach (var part in split.Parts.Skip(1)) Assert.Equal(Encoding.UTF8.GetBytes(Function(part.Name)), part.Content);
        Assert.Equal(source, split.Parts.SelectMany(p => p.Content).ToArray());
        foreach (var part in split.Parts)
        {
            Assert.Equal(source.AsSpan(part.Offset, part.Length).ToArray(), part.Content);
            Assert.Equal(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(part.Content)), part.Sha256);
        }
    }

    [Fact]
    public void Repeat_export_preserves_all_native_bytes_and_produces_identical_manifests_and_ten_files()
    {
        using var temp = new Temp(); var native = Native();
        var a = HmiScriptPackage.Create(temp.PathFor("a"), "T05RecipeCatalog", "synthetic:module-locator", native, HmiScriptSource.CatalogFunctions);
        var b = HmiScriptPackage.Create(temp.PathFor("b"), "T05RecipeCatalog", "synthetic:module-locator", native, HmiScriptSource.CatalogFunctions);
        Assert.Equal(JsonSerializer.Serialize(a), JsonSerializer.Serialize(b));
        Assert.Equal(10, Directory.GetFiles(temp.PathFor("a/T05RecipeCatalog"), "*.js").Length);
        foreach (var artifact in native) Assert.Equal(artifact.Content, File.ReadAllBytes(temp.PathFor("a/native/" + artifact.Name)));
        foreach (var row in a.NativeArtifacts.Concat(a.Parts)) Assert.Equal(row.Sha256, HmiScriptSource.Hash(File.ReadAllBytes(temp.PathFor("a/" + row.Path))));
        Assert.Equal("UNAVAILABLE", a.IdentityState); Assert.Null(a.ObjectId);
        Assert.Equal("NOT_ENGINEERING_VERIFIED", a.SourceStatus);
        Assert.All(HmiScriptPackage.Compare(a, b), c => Assert.Equal("UNCHANGED", c.Status));
        Assert.Equal(a.ModuleName, HmiScriptPackage.ReadVerified(temp.PathFor("a/manifest.json")).ModuleName);
        Assert.Equal(HmiScriptSource.CatalogFunctions.Append("Global definition area").Select(n => n + ".js").Order(),
            Directory.GetFiles(temp.PathFor("a/T05RecipeCatalog")).Select(Path.GetFileName).Order());
        Assert.Throws<IOException>(() => HmiScriptPackage.Create(temp.PathFor("a"), "T05RecipeCatalog", "synthetic", native, HmiScriptSource.CatalogFunctions));
    }

    [Fact]
    public void Comparison_loader_rejects_tampered_content_duplicate_keys_and_path_traversal()
    {
        using var temp = new Temp();
        var manifest = HmiScriptPackage.Create(temp.PathFor("a"), "T05RecipeCatalog", "s", Native(), HmiScriptSource.CatalogFunctions);
        string path = temp.PathFor("a/manifest.json");
        File.WriteAllText(path, JsonSerializer.Serialize(manifest with { Parts = [manifest.Parts[0] with { Path = "../../escape" }] }, HmiScriptPackage.Json));
        Assert.Throws<ArgumentException>(() => HmiScriptPackage.ReadVerified(path));
        File.WriteAllText(path, JsonSerializer.Serialize(manifest, HmiScriptPackage.Json).Replace("\"profile\":", "\"profile\":\"duplicate\",\"profile\":"));
        Assert.Throws<ArgumentException>(() => HmiScriptPackage.ReadVerified(path));
        File.WriteAllText(path, JsonSerializer.Serialize(manifest, HmiScriptPackage.Json));
        File.WriteAllText(temp.PathFor("a/T05RecipeCatalog/render.js"), "tampered");
        Assert.Throws<IOException>(() => HmiScriptPackage.ReadVerified(path));
    }

    [Fact]
    public void Comparing_one_function_edit_does_not_mark_other_functions_with_shifted_offsets_as_modified()
    {
        using var temp = new Temp();
        var a = HmiScriptPackage.Create(temp.PathFor("a"), "T05RecipeCatalog", "s", Native(), HmiScriptSource.CatalogFunctions);
        string text = Encoding.UTF8.GetString(Source()).Replace(Function("render"), Function("render").Replace("return nested();", "return { changed: true, data: nested() };"), StringComparison.Ordinal);
        var b = HmiScriptPackage.Create(temp.PathFor("b"), "T05RecipeCatalog", "s", Native(Encoding.UTF8.GetBytes(text)), HmiScriptSource.CatalogFunctions);
        var changes = HmiScriptPackage.Compare(a, b);
        Assert.Single(changes, c => c.Status == "MODIFIED");
        Assert.Equal("EXPORTED_FUNCTION:render", changes.Single(c => c.Status == "MODIFIED").ComparisonKey);
        Assert.Throws<ArgumentException>(() => HmiScriptPackage.Compare(a, b with { ScopeBinding = "another-project" }));
    }

    [Theory]
    [InlineData("export function one() {", "UNSUPPORTED_OR_INVALID_JAVASCRIPT")]
    [InlineData("export function other() {}", "INCOMPLETE_OR_UNEXPECTED_FUNCTION_INVENTORY")]
    [InlineData("export function one() {}\nconst after = 1;", "UNSUPPORTED_TOP_LEVEL_STRUCTURE")]
    [InlineData("export const one = () => {};", "UNSUPPORTED_TOP_LEVEL_STRUCTURE")]
    [InlineData("export function one() {}\n// ambiguous trailing comment", "AMBIGUOUS_INTER_FUNCTION_CONTENT")]
    [InlineData("export function one() {}\n/*incomplete", "UNSUPPORTED_OR_INVALID_JAVASCRIPT")]
    [InlineData("// area or function comment?\nexport function one() {}", "AMBIGUOUS_GLOBAL_FUNCTION_BOUNDARY")]
    public void Unsupported_or_incomplete_export_never_emits_partial_function_files(string source, string reason)
    {
        using var temp = new Temp();
        byte[] bytes = Encoding.UTF8.GetBytes(source);
        var manifest = HmiScriptPackage.Create(temp.PathFor("out"), "T05RecipeCatalog", "s", Native(bytes), ["one"]);
        Assert.Equal("BLOCKED", manifest.SplitStatus); Assert.Equal(reason, manifest.Reason);
        Assert.Empty(manifest.Parts); Assert.False(Directory.Exists(temp.PathFor("out/T05RecipeCatalog")));
        Assert.Equal(bytes, File.ReadAllBytes(temp.PathFor("out/native/T05RecipeCatalog.hmi.js")));
        Assert.True(File.Exists(temp.PathFor("out/manifest.json")));
    }

    [Fact]
    public void Case_collisions_and_duplicate_declarations_are_blocked_without_renaming()
    {
        Assert.Equal("BLOCKED", HmiScriptSource.Split(Encoding.UTF8.GetBytes("export function f() {}\nexport function F() {}"), ["f", "F"]).Status);
        Assert.Equal("BLOCKED", HmiScriptSource.Split(Encoding.UTF8.GetBytes("export function f() {}\nexport function f() {}"), ["f", "f"]).Status);
        using var temp = new Temp();
        Assert.Throws<ArgumentException>(() => HmiScriptPackage.Create(temp.PathFor("o"), "T05RecipeCatalog", "s",
            [new("file.js", []), new("FILE.js", [])], ["f"]));
        Assert.False(Directory.Exists(temp.PathFor("o")));
    }

    [Fact]
    public void Missing_pair_unknown_native_format_and_invalid_utf8_preserve_native_and_fail_closed()
    {
        using var temp = new Temp();
        var a = HmiScriptPackage.Create(temp.PathFor("a"), "T05RecipeCatalog", "s", [Native()[0]], HmiScriptSource.CatalogFunctions);
        Assert.Equal("EXPECTED_NATIVE_JS_YML_PAIR_MISSING", a.Reason);
        var b = HmiScriptPackage.Create(temp.PathFor("b"), "T05RecipeCatalog", "s", Native([0xff, 0xfe, 0]), HmiScriptSource.CatalogFunctions);
        Assert.Equal("BLOCKED", b.SplitStatus);
        var c = HmiScriptPackage.Create(temp.PathFor("c"), "T05RecipeCatalog", "s", [new("opaque.bin", [0, 1])], HmiScriptSource.CatalogFunctions);
        Assert.Equal("BLOCKED", c.SplitStatus); Assert.Single(c.NativeArtifacts);
        Assert.Equal("BLOCKED", HmiScriptSource.Split(new byte[HmiScriptSource.MaxSourceBytes + 1], ["f"]).Status);
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("CON")]
    [InlineData("name.")]
    [InlineData("name ")]
    [InlineData("a/b")]
    public void Unsafe_names_are_rejected_instead_of_normalized(string name)
    {
        using var temp = new Temp();
        Assert.Throws<ArgumentException>(() => HmiScriptPackage.Create(temp.PathFor("out"), name, "s", Native(), ["f"]));
        Assert.False(Directory.Exists(temp.PathFor("out")));
    }

    [Fact]
    public void Comments_unicode_async_regex_templates_and_multiline_signatures_are_parsed_without_execution()
    {
        byte[] source = Encoding.UTF8.GetBytes("const text = 'Łódź';\nexport async function próba(\n x,\n y\n) {\n /* } */ return /[{}]/.test(`${x}${{v:y}.v}`);\n}\n");
        var result = HmiScriptSource.Split(source, ["próba"]);
        Assert.Equal("PASS", result.Status); Assert.Equal("próba", result.Parts[1].Name);
        Assert.Equal(source, result.Parts.SelectMany(p => p.Content));
    }
}
