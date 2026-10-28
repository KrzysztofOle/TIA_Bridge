using System.Text.Json;

namespace TiaBridge.Core.Hmi;

public sealed record NativeScriptArtifact(string Name, byte[] Content);
public sealed record ScriptArtifact(string Name, string Path, string Kind, string ComparisonKey, string Sha256, int Bytes,
    int? SourceOffset = null, string IdentityState = "SOURCE_LOCAL_NOT_SIEMENS_ID");
public sealed record ScriptManifest(string SchemaVersion, string Profile, string ModuleName, string ScopeBinding,
    string IdentityState, string? ObjectId, string SourceStatus, string SplitStatus, string Reason,
    IReadOnlyList<ScriptArtifact> NativeArtifacts, IReadOnlyList<ScriptArtifact> Parts);
public sealed record ScriptChange(string ComparisonKey, string Status);

/// <summary>Immutable per-run package. Native preservation happens even when splitting is blocked.</summary>
public static class HmiScriptPackage
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        MaxDepth = 16,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true
    };
    public static ScriptManifest Create(string destination, string moduleName, string scopeBinding,
        IReadOnlyList<NativeScriptArtifact> native, IReadOnlyList<string> expectedFunctions)
    {
        if (!HmiScriptSource.SafeName(moduleName) || string.IsNullOrWhiteSpace(scopeBinding) || scopeBinding.Length > 4096)
            throw new ArgumentException("Invalid module name or scope binding.");
        if (native.Count is < 1 or > 32 || native.Sum(n => (long)n.Content.Length) > 16 * 1024 * 1024)
            throw new ArgumentException("Native artifact limits exceeded.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (native.Any(n => !HmiScriptSource.SafeName(n.Name) || !names.Add(n.Name))) throw new ArgumentException("Native filename collision or unsafe name.");
        string root = Path.GetFullPath(destination);
        CheckChain(root);
        // A create-new claim also rejects an existing empty directory; concurrent runs never overwrite.
        if (Directory.Exists(root) || File.Exists(root)) throw new IOException("Destination already exists.");
        Directory.CreateDirectory(root);
        WriteNew(Path.Combine(root, ".package-claim"), []);
        var nativeRows = new List<ScriptArtifact>();
        Directory.CreateDirectory(Path.Combine(root, "native"));
        foreach (var item in native.OrderBy(n => n.Name, StringComparer.Ordinal))
        {
            string relative = "native/" + item.Name;
            WriteNew(Path.Combine(root, relative), item.Content);
            nativeRows.Add(new(item.Name, relative, "NATIVE", "native:" + item.Name, HmiScriptSource.Hash(item.Content), item.Content.Length));
        }
        var js = native.Where(n => n.Name == moduleName + ".hmi.js").ToArray();
        bool pair = native.Any(n => n.Name == moduleName + ".hmi.yml");
        var split = js.Length == 1 && pair ? HmiScriptSource.Split(js[0].Content, expectedFunctions)
            : new ScriptSplit("BLOCKED", "EXPECTED_NATIVE_JS_YML_PAIR_MISSING", []);
        var rows = new List<ScriptArtifact>();
        if (split.Status == "PASS")
        {
            Directory.CreateDirectory(Path.Combine(root, moduleName));
            foreach (var part in split.Parts)
            {
                string relative = moduleName + "/" + part.Name + ".js";
                WriteNew(Path.Combine(root, relative), part.Content);
                rows.Add(new(part.Name, relative, part.Kind, part.Kind + ":" + part.Name, part.Sha256, part.Length, part.Offset));
            }
        }
        var manifest = new ScriptManifest("tia-bridge-hmi-js-functions/1.0", HmiScriptSource.Profile, moduleName, scopeBinding,
            "UNAVAILABLE", null, "NOT_ENGINEERING_VERIFIED", split.Status, split.Reason, nativeRows, rows);
        WriteNew(Path.Combine(root, "manifest.json"), JsonSerializer.SerializeToUtf8Bytes(manifest, Json));
        return manifest;
    }

    public static IReadOnlyList<ScriptChange> Compare(ScriptManifest previous, ScriptManifest current)
    {
        if (previous.SchemaVersion != current.SchemaVersion || previous.Profile != current.Profile
            || previous.ScopeBinding != current.ScopeBinding || previous.ModuleName != current.ModuleName)
            throw new ArgumentException("Comparison needs the same explicitly reviewed scope binding and profile.");
        var left = previous.Parts.ToDictionary(p => p.ComparisonKey, StringComparer.Ordinal);
        var right = current.Parts.ToDictionary(p => p.ComparisonKey, StringComparer.Ordinal);
        return left.Keys.Union(right.Keys).Order(StringComparer.Ordinal).Select(key =>
            new ScriptChange(key, previous.SplitStatus != "PASS" || current.SplitStatus != "PASS" ? "UNKNOWN"
                : !right.ContainsKey(key) ? "MISSING_OBSERVATION_ONLY" : !left.ContainsKey(key) ? "ADDED"
                : left[key].Sha256 == right[key].Sha256 ? "UNCHANGED" : "MODIFIED")).ToArray();
    }

    public static ScriptManifest ReadVerified(string manifestPath)
    {
        CheckChain(manifestPath);
        using var stream = File.OpenRead(manifestPath);
        if (stream.Length > 1024 * 1024) throw new ArgumentException("Manifest exceeds limits.");
        using var document = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 16 });
        void Unique(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Object)
            {
                var keys = new HashSet<string>(StringComparer.Ordinal);
                foreach (var p in value.EnumerateObject())
                { if (!keys.Add(p.Name)) throw new ArgumentException("Duplicate manifest key."); Unique(p.Value); }
            }
            else if (value.ValueKind == JsonValueKind.Array) foreach (var v in value.EnumerateArray()) Unique(v);
        }
        Unique(document.RootElement);
        var manifest = document.RootElement.Deserialize<ScriptManifest>(Json) ?? throw new ArgumentException("Invalid manifest.");
        if (manifest.SchemaVersion != "tia-bridge-hmi-js-functions/1.0" || manifest.Profile != HmiScriptSource.Profile
            || !HmiScriptSource.SafeName(manifest.ModuleName) || string.IsNullOrWhiteSpace(manifest.ScopeBinding)
            || manifest.IdentityState != "UNAVAILABLE" || manifest.ObjectId is not null || manifest.SourceStatus != "NOT_ENGINEERING_VERIFIED"
            || manifest.SplitStatus is not ("PASS" or "BLOCKED") || manifest.NativeArtifacts is not { Count: >= 1 and <= 32 }
            || manifest.Parts is not { Count: <= 129 } || manifest.SplitStatus == "BLOCKED" && manifest.Parts.Count != 0)
            throw new ArgumentException("Unsupported manifest.");
        string root = Path.GetDirectoryName(Path.GetFullPath(manifestPath))!;
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in manifest.NativeArtifacts.Concat(manifest.Parts))
        {
            if (row is null) throw new ArgumentException("Null artifact row.");
            bool native = row.Kind == "NATIVE";
            string relative = native ? "native/" + row.Name : manifest.ModuleName + "/" + row.Name + ".js";
            string key = native ? "native:" + row.Name : row.Kind + ":" + row.Name;
            if (!HmiScriptSource.SafeName(row.Name) || row.Path != relative || row.ComparisonKey != key || !paths.Add(row.Path)
                || row.Kind is not ("NATIVE" or "GLOBAL_PREFIX" or "EXPORTED_FUNCTION")
                || row.Bytes is < 0 or > 16 * 1024 * 1024 || row.IdentityState != "SOURCE_LOCAL_NOT_SIEMENS_ID"
                || !native && row.SourceOffset is not >= 0) throw new ArgumentException("Invalid artifact row.");
            string path = Path.Combine(root, row.Path); CheckChain(path);
            using var artifact = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (artifact.Length != row.Bytes) throw new IOException("Artifact length mismatch.");
            string hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(artifact));
            if (row.Sha256 != hash) throw new IOException("Artifact hash mismatch.");
        }
        return manifest;
    }

    public static void CheckChain(string path)
    {
        for (string? p = Path.GetFullPath(path); p is not null; p = Path.GetDirectoryName(p))
            if ((Directory.Exists(p) || File.Exists(p)) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Reparse points are forbidden.");
    }
    private static void WriteNew(string path, byte[] bytes)
    {
        CheckChain(path);
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        stream.Write(bytes); stream.Flush(true);
    }
}
