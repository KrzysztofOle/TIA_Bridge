using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Json.Schema;
using TiaBridge.Core.Identity;
using TiaBridge.Core.Operations;

namespace TiaBridge.Core.Workspace;

/// <summary>Offline artifact boundary; no resolver, network, vendor parser or filesystem operations.</summary>
internal static class ArtifactJson
{
    internal const int MaxBytes = 10 * 1024 * 1024;
    internal const int MaxDepth = 32;
    private static readonly JsonSerializerOptions Options = CreateOptions();
    private static readonly Dictionary<string, JsonSchema> Schemas = new(StringComparer.Ordinal);
    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            RespectNullableAnnotations = true,
            RespectRequiredConstructorParameters = true,
            MaxDepth = MaxDepth
        };
        options.Converters.Add(new MethodConverter());
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        options.Converters.Add(new StrictStringConverter());
        options.Converters.Add(new IdentityConverter<ProjectKey>(g => new ProjectKey(g)));
        options.Converters.Add(new IdentityConverter<ObjectId>(g => new ObjectId(g)));
        return options;
    }

    private sealed class MethodConverter : JsonConverter<OperationMethod>
    {
        public override OperationMethod Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            string? name = reader.GetString();
            foreach (var method in Enum.GetValues<OperationMethod>())
                if (OperationCatalog.Name(method) == name) return method;
            throw new ArgumentException("Unknown capability operation.");
        }
        public override void Write(Utf8JsonWriter writer, OperationMethod value, JsonSerializerOptions options) => writer.WriteStringValue(OperationCatalog.Name(value));
    }

    private sealed class StrictStringConverter : JsonConverter<string>
    {
        private static void Check(string value)
        {
            try { _ = new UTF8Encoding(false, true).GetByteCount(value); }
            catch (EncoderFallbackException) { throw new ArgumentException("Invalid Unicode in artifact."); }
        }
        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.GetString()!;
        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) { Check(value); writer.WriteStringValue(value); }
        public override string ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.GetString()!;
        public override void WriteAsPropertyName(Utf8JsonWriter writer, string value, JsonSerializerOptions options) { Check(value); writer.WritePropertyName(value); }
    }

    private sealed class IdentityConverter<T>(Func<Guid, T> create) : JsonConverter<T> where T : UuidIdentity
    {
        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => create(UuidIdentity.ParseValue(reader.GetString()!));
        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString());
    }

    internal static T Read<T>(ReadOnlyMemory<byte> bytes, string schemaName)
    {
        try
        {
            using var doc = Parse(bytes);
            lock (Schemas)
            {
                if (!Schemas.TryGetValue(schemaName, out var schema))
                {
                    using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"TiaBridge.Core.Contracts.{schemaName}.schema.json")!;
                    using var reader = new StreamReader(stream);
                    schema = JsonSchema.FromText(reader.ReadToEnd());
                    Schemas.Add(schemaName, schema);
                }
                if (!schema.Evaluate(JsonSerializer.SerializeToNode(doc.RootElement), new EvaluationOptions { OutputFormat = OutputFormat.Flag, RequireFormatValidation = true }).IsValid)
                    throw new ArgumentException("Invalid artifact schema.");
            }
            return doc.RootElement.Deserialize<T>(Options)!;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException or FormatException or OverflowException)
        { throw new ArgumentException("Invalid versioned artifact."); }
    }

    internal static byte[] Serialize<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, Options);

    internal static JsonDocument Parse(ReadOnlyMemory<byte> bytes)
    {
        try
        {
            if (bytes.Length > MaxBytes) throw new ArgumentException("Artifact size limit exceeded.");
            _ = new UTF8Encoding(false, true).GetCharCount(bytes.Span);
            var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = MaxDepth });
            try { using var sink = new BoundedStream(); using var writer = new Utf8JsonWriter(sink); WriteCanonical(writer, doc.RootElement); }
            catch { doc.Dispose(); throw; }
            return doc;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException or FormatException or OverflowException)
        { throw new ArgumentException("Invalid bounded artifact JSON."); }
    }

    internal static byte[] Canonical(ReadOnlyMemory<byte> bytes)
    {
        using var doc = Parse(bytes);
        using var stream = new BoundedStream();
        using (var writer = new Utf8JsonWriter(stream)) WriteCanonical(writer, doc.RootElement);
        return stream.ToArray();
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                var keys = new HashSet<string>(StringComparer.Ordinal);
                foreach (var field in value.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    if (!keys.Add(field.Name)) throw new ArgumentException("Duplicate artifact key.");
                    writer.WritePropertyName(field.Name);
                    WriteCanonical(writer, field.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray()) WriteCanonical(writer, item);
                writer.WriteEndArray();
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(CanonicalNumber(value.GetRawText()));
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(value.GetString());
                break;
            default: value.WriteTo(writer); break;
        }
    }

    // Exact decimal arithmetic: no float/decimal rounding of unknown vendor values.
    private static string CanonicalNumber(string token)
    {
        if (token.Length > 128) throw new ArgumentException("Number limit exceeded.");
        string[] parts = token.ToLowerInvariant().Split('e');
        int exponent = parts.Length == 1 ? 0 : int.Parse(parts[1], CultureInfo.InvariantCulture);
        if (Math.Abs((long)exponent) > 1000) throw new ArgumentException("Exponent limit exceeded.");
        string mantissa = parts[0];
        int dot = mantissa.IndexOf('.');
        int scale = (dot < 0 ? 0 : mantissa.Length - dot - 1) - exponent;
        string integer = BigInteger.Parse(mantissa.Replace(".", ""), CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
        if (integer == "0") return "0";
        bool negative = integer[0] == '-';
        string digits = negative ? integer[1..] : integer;
        string result = scale <= 0 ? digits + new string('0', -scale)
            : scale >= digits.Length ? "0." + new string('0', scale - digits.Length) + digits
            : digits.Insert(digits.Length - scale, ".");
        if (result.Contains('.')) result = result.TrimEnd('0').TrimEnd('.');
        return (negative ? "-" : "") + result;
    }

    internal static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private sealed class BoundedStream : MemoryStream
    {
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (Position + buffer.Length > MaxBytes) throw new ArgumentException("Canonical artifact size limit exceeded.");
            base.Write(buffer);
        }
        public override void Write(byte[] buffer, int offset, int count)
        {
            if (Position + count > MaxBytes) throw new ArgumentException("Canonical artifact size limit exceeded.");
            base.Write(buffer, offset, count);
        }
    }
}

public static class WorkspaceJson
{
    public static WorkspaceManifest ReadManifest(ReadOnlyMemory<byte> bytes)
    {
        var manifest = ArtifactJson.Read<WorkspaceManifest>(bytes, "workspace-manifest");
        WorkspaceValidation.Validate(manifest);
        return manifest;
    }
    public static byte[] WriteManifest(WorkspaceManifest manifest)
    {
        byte[] bytes = ArtifactJson.Serialize(manifest);
        _ = ReadManifest(bytes);
        return ArtifactJson.Canonical(bytes);
    }
    public static WorkspaceConfiguration ReadConfiguration(ReadOnlyMemory<byte> bytes)
    {
        var config = ArtifactJson.Read<WorkspaceConfiguration>(bytes, "workspace-config");
        _ = WorkspacePath.ValidateRoot(config.RootPath);
        if (config.Projects.Distinct().Count() != config.Projects.Count) throw new ArgumentException("Duplicate project binding.");
        return config;
    }
    public static byte[] WriteConfiguration(WorkspaceConfiguration config)
    {
        byte[] bytes = ArtifactJson.Serialize(config);
        _ = ReadConfiguration(bytes);
        return ArtifactJson.Canonical(bytes);
    }

    public static void ValidatePaths(WorkspaceManifest manifest, WorkspaceConfiguration config)
    {
        // Revalidate typed inputs too; constructors are DTOs, not a trust boundary.
        _ = WriteManifest(manifest);
        _ = WriteConfiguration(config);
        if (manifest.WorkspaceId != config.WorkspaceId || !config.Projects.Contains(manifest.ProjectKey))
            throw new ArgumentException("Workspace binding mismatch.");
        foreach (var file in manifest.ExchangeUnits.SelectMany(u => u.Files)) _ = WorkspacePath.Resolve(config.RootPath, file.RelativePath);
        foreach (var item in manifest.Inventory) _ = WorkspacePath.Resolve(config.RootPath, WorkspaceLayout.ObjectDirectory(manifest.Project, item));
        foreach (var group in manifest.Project.Groups) _ = WorkspacePath.Resolve(config.RootPath, WorkspaceLayout.GroupDirectory(manifest.Project, group));
        foreach (var device in manifest.Project.Devices) _ = WorkspacePath.Resolve(config.RootPath, WorkspaceLayout.DeviceDirectory(device));
    }
}

public static class WorkspaceHashing
{
    public static string Raw(ReadOnlySpan<byte> bytes) => ArtifactJson.Hash(bytes);
    public static string Model(ReadOnlyMemory<byte> modelJson, string normalizerVersion, string comparisonProfile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizerVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(comparisonProfile);
        using var model = ArtifactJson.Parse(modelJson);
        return ArtifactJson.Hash(ArtifactJson.Canonical(ArtifactJson.Serialize(new { NormalizerVersion = normalizerVersion, ComparisonProfile = comparisonProfile, Model = model.RootElement })));
    }
    public static string Unit(ExchangeUnit unit, IEnumerable<WorkspaceObject> inventory)
    {
        WorkspacePath.ValidateUnique(unit.Files.Select(f => f.RelativePath));
        var members = inventory.Where(o => unit.Members.Contains(o.ObjectId)).Select(o => o with
        {
            FilePaths = o.FilePaths.Order(StringComparer.Ordinal).ToArray(),
            Dependencies = o.Dependencies.OrderBy(d => d.TargetObjectId.ToString(), StringComparer.Ordinal)
                .ThenBy(d => d.Relation, StringComparer.Ordinal).ToArray(),
            Coverage = new Coverage(o.Coverage.Dimensions.OrderBy(d => d.Dimension).ToArray())
        }).OrderBy(o => o.ObjectId.ToString(), StringComparer.Ordinal).ToArray();
        if (members.Length != unit.Members.Count) throw new ArgumentException("Missing unit metadata.");
        return ArtifactJson.Hash(ArtifactJson.Canonical(ArtifactJson.Serialize(new
        {
            unit.UnitId,
            Members = members,
            Files = unit.Files.OrderBy(f => f.RelativePath, StringComparer.Ordinal).ToArray(),
            unit.VendorFields
        })));
    }
}
