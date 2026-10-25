using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Json.Schema;
using TiaBridge.Core.Identity;
using TiaBridge.Core.Operations;

namespace TiaBridge.Core.Transport.V1;

/// <summary>Versioned, bounded JSON boundary. Does not dispatch or grant permissions.</summary>
public static class ContractJson
{
    public const int MaxRequestBytes = 1024 * 1024;
    public const int MaxReportBytes = 10 * 1024 * 1024;
    public const int MaxEventBytes = 64 * 1024;
    public const int MaxDepth = 32;
    private static readonly JsonDocument RequestShape = JsonDocument.Parse(LoadSchemaText("operation-request"));
    private static readonly JsonSchema RequestSchema = LoadSchema("operation-request");
    private static readonly JsonSchema ReportSchema = LoadSchema("operation");
    private static readonly JsonSchema AcceptanceSchema = LoadSchema("acceptance");
    private static readonly JsonSchema ErrorSchema = LoadSchema("error");
    private static readonly JsonSchema SessionSchema = LoadSchema("session-identity");
    private static readonly JsonSerializerOptions Options = CreateOptions();

    public static ValidatedRequest ReadRequest(ReadOnlyMemory<byte> utf8)
    {
        using JsonDocument document = Parse(utf8, MaxRequestBytes);
        var root = document.RootElement;
        CheckVersion(root);
        Validate(RequestSchema, root);
        try
        {
            var method = Enum.GetValues<OperationMethod>().Single(m => OperationCatalog.Name(m) == root.GetProperty("method").GetString());
            var shape = RequestShape.RootElement.GetProperty("oneOf").EnumerateArray().Single(b => b.GetProperty("properties").GetProperty("method").GetProperty("const").GetString() == OperationCatalog.Name(method));
            var parametersJson = Canonicalize(root.GetProperty("params"), shape: shape.GetProperty("properties").GetProperty("params"));
            var parameters = (OperationParameters)JsonSerializer.Deserialize(parametersJson, MethodContracts.Parameters(method), Options)!;
            ProjectKey? project = root.TryGetProperty("project_key", out var key) ? new ProjectKey(UuidIdentity.ParseValue(key.GetString()!)) : null;
            var identity = new OperationRequest(ContractVersion.V1, method, project,
                new IdempotencyKey(root.GetProperty("idempotency_key").GetString()!), Canonicalize(root, "idempotency_key", shape));
            return new ValidatedRequest(identity, parameters);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or FormatException or OverflowException)
        {
            throw new ContractValidationException(ErrorCode.VALIDATION_FAILED);
        }
    }

    public static byte[] WriteRequest(OperationMethod method, ProjectKey? project, IdempotencyKey key, OperationParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(parameters);
        if (parameters.GetType() != MethodContracts.Parameters(method)) throw new ContractValidationException(ErrorCode.VALIDATION_FAILED);
        var root = new JsonObject
        {
            ["schema_version"] = "1.0",
            ["method"] = OperationCatalog.Name(method),
            ["idempotency_key"] = key.Value,
            ["params"] = JsonSerializer.SerializeToNode(parameters, parameters.GetType(), Options)
        };
        if (project is not null) root["project_key"] = project.ToString();
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(root, Options);
        _ = ReadRequest(bytes);
        return bytes;
    }

    public static OperationReport ReadReport(ReadOnlyMemory<byte> utf8)
    {
        using JsonDocument document = Parse(utf8, MaxReportBytes);
        CheckVersion(document.RootElement);
        Validate(ReportSchema, document.RootElement);
        if (document.RootElement.GetProperty("method").GetString() == "operation.events"
            && document.RootElement.TryGetProperty("result", out var eventResult))
            foreach (var item in eventResult.GetProperty("events").EnumerateArray())
                if (Encoding.UTF8.GetByteCount(item.GetRawText()) > MaxEventBytes)
                    throw new ContractValidationException(ErrorCode.LIMIT_EXCEEDED);
        try
        {
            var node = JsonNode.Parse(Canonicalize(document.RootElement))!.AsObject();
            JsonNode? result = node["result"];
            node.Remove("result");
            var report = node.Deserialize<OperationReport>(Options)!;
            if (result is not null)
                report = report with { Result = (OperationResult)result.Deserialize(MethodContracts.Result(report.Method), Options)! };
            OperationLifecycle.Validate(report.State, report.Outcome);
            if (report.Error is not null && (report.Error.Outcome != report.Outcome || report.Error.Retryable && report.Outcome != OperationOutcome.NOT_STARTED))
                throw new ContractValidationException(ErrorCode.VALIDATION_FAILED);
            if (report.TotalUnits is not null && report.CompletedUnits > report.TotalUnits)
                throw new ContractValidationException(ErrorCode.VALIDATION_FAILED);
            return report;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or FormatException or OverflowException)
        {
            throw new ContractValidationException(ErrorCode.VALIDATION_FAILED);
        }
    }

    public static byte[] WriteReport(OperationReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (report.Result is not null && report.Result.GetType() != MethodContracts.Result(report.Method))
            throw new ContractValidationException(ErrorCode.VALIDATION_FAILED);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(report, Options);
        _ = ReadReport(bytes);
        return bytes;
    }

    public static Acceptance ReadAcceptance(ReadOnlyMemory<byte> utf8)
    {
        using JsonDocument doc = Parse(utf8, MaxRequestBytes);
        Validate(AcceptanceSchema, doc.RootElement);
        return DeserializeValidated<Acceptance>(doc.RootElement);
    }

    public static byte[] WriteAcceptance(Acceptance acceptance)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(acceptance, Options);
        _ = ReadAcceptance(bytes);
        return bytes;
    }

    public static OperationError ReadError(ReadOnlyMemory<byte> utf8)
    {
        using JsonDocument doc = Parse(utf8, MaxRequestBytes);
        Validate(ErrorSchema, doc.RootElement);
        return DeserializeValidated<OperationError>(doc.RootElement);
    }

    public static byte[] WriteError(OperationError error)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(error, Options);
        _ = ReadError(bytes);
        return bytes;
    }

    public static byte[] WriteIdentity<T>(T identity) where T : UuidIdentity
    {
        ArgumentNullException.ThrowIfNull(identity);
        return JsonSerializer.SerializeToUtf8Bytes(identity, Options);
    }

    public static T ReadIdentity<T>(ReadOnlyMemory<byte> utf8) where T : UuidIdentity
    {
        using JsonDocument doc = Parse(utf8, 128);
        if (doc.RootElement.ValueKind != JsonValueKind.String) throw new ContractValidationException(ErrorCode.INVALID_ARGUMENT);
        try { return doc.RootElement.Deserialize<T>(Options)!; }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        { throw new ContractValidationException(ErrorCode.INVALID_ARGUMENT); }
    }

    private sealed record SessionBinding(string SchemaVersion, ProjectKey ProjectKey, SessionEpoch SessionEpoch,
        int ProcessId, DateTimeOffset ProcessStartedAt, Guid HandshakeInstance);

    public static byte[] WriteSessionIdentity(SessionIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return JsonSerializer.SerializeToUtf8Bytes(new SessionBinding("1.0", identity.Project, identity.Epoch,
            identity.ProcessId, identity.ProcessStartedAt, identity.HandshakeInstance), Options);
    }

    public static SessionIdentity ReadSessionIdentity(ReadOnlyMemory<byte> utf8)
    {
        using JsonDocument doc = Parse(utf8, MaxRequestBytes);
        CheckVersion(doc.RootElement);
        Validate(SessionSchema, doc.RootElement);
        try
        {
            var binding = DeserializeValidated<SessionBinding>(doc.RootElement);
            return new SessionIdentity(binding.ProjectKey, binding.SessionEpoch, binding.ProcessId, binding.ProcessStartedAt, binding.HandshakeInstance);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        { throw new ContractValidationException(ErrorCode.VALIDATION_FAILED); }
    }

    public static ContractVersion ReadVersion(ReadOnlyMemory<byte> utf8)
    {
        using JsonDocument doc = Parse(utf8, 128);
        if (doc.RootElement.ValueKind != JsonValueKind.String || doc.RootElement.GetString() != "1.0")
            throw new ContractValidationException(ErrorCode.SCHEMA_VERSION);
        return ContractVersion.V1;
    }

    public static byte[] WriteVersion(ContractVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);
        return JsonSerializer.SerializeToUtf8Bytes(version.Value);
    }

    private static JsonDocument Parse(ReadOnlyMemory<byte> utf8, int maximum)
    {
        if (utf8.Length > maximum) throw new ContractValidationException(ErrorCode.LIMIT_EXCEEDED);
        try
        {
            // Reject invalid UTF-8 before GetString can substitute replacement characters.
            _ = new UTF8Encoding(false, true).GetCharCount(utf8.Span);
            var doc = JsonDocument.Parse(utf8, new JsonDocumentOptions { MaxDepth = MaxDepth });
            try { CheckDuplicates(doc.RootElement); }
            catch { doc.Dispose(); throw; }
            return doc;
        }
        catch (Exception ex) when (ex is JsonException or DecoderFallbackException or InvalidOperationException or ArgumentException)
        { throw new ContractValidationException(ErrorCode.INVALID_ARGUMENT); }
    }

    private static void CheckDuplicates(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!keys.Add(property.Name)) throw new ContractValidationException(ErrorCode.INVALID_ARGUMENT);
                CheckDuplicates(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var element in value.EnumerateArray()) CheckDuplicates(element);
        else if (value.ValueKind == JsonValueKind.Number) _ = NormalizeInteger(value.GetRawText());
        else if (value.ValueKind == JsonValueKind.String) _ = value.GetString();
    }

    private static void CheckVersion(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("schema_version", out var version)
            || version.ValueKind != JsonValueKind.String || version.GetString() != "1.0")
            throw new ContractValidationException(ErrorCode.SCHEMA_VERSION);
    }

    private static void Validate(JsonSchema schema, JsonElement value)
    {
        try
        {
            // The pinned evaluator caches constraints. Serialize evaluation of each schema;
            // callers may validate concurrently without sharing mutable evaluation state.
            lock (schema)
            {
                if (schema.Evaluate(JsonNode.Parse(value.GetRawText()), new EvaluationOptions
                { RequireFormatValidation = true, OutputFormat = OutputFormat.Flag }).IsValid) return;
            }
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or OverflowException or InvalidOperationException)
        { throw new ContractValidationException(ErrorCode.VALIDATION_FAILED); }
        throw new ContractValidationException(ErrorCode.VALIDATION_FAILED);
    }

    private static T DeserializeValidated<T>(JsonElement value)
    {
        try { return JsonSerializer.Deserialize<T>(Canonicalize(value), Options)!; }
        catch (Exception ex) when (ex is JsonException or ArgumentException or FormatException or OverflowException)
        { throw new ContractValidationException(ErrorCode.VALIDATION_FAILED); }
    }

    private static JsonSchema LoadSchema(string name) => JsonSchema.FromText(LoadSchemaText(name));

    private static string LoadSchemaText(string name)
    {
        Assembly assembly = typeof(ContractJson).Assembly;
        string resource = assembly.GetManifestResourceNames().Single(n => n.EndsWith("." + name + ".schema.json", StringComparison.Ordinal));
        using Stream stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        // Only trusted embedded schemas with local # references. No caller schemas or resolver.
        return reader.ReadToEnd();
    }

    private static byte[] Canonicalize(JsonElement root, string? excludedRootProperty = null, JsonElement? shape = null)
    {
        using var output = new MemoryStream();
        using (var writer = new Utf8JsonWriter(output, new JsonWriterOptions { Encoder = JavaScriptEncoder.Default }))
            WriteCanonical(writer, root, excludedRootProperty, shape);
        return output.ToArray();
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement value, string? excluded = null, JsonElement? shape = null)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    if (property.Name == excluded) continue;
                    writer.WritePropertyName(property.Name);
                    JsonElement? childShape = shape is { } definition && definition.TryGetProperty("properties", out var properties)
                        && properties.TryGetProperty(property.Name, out var child) ? child : null;
                    WriteCanonical(writer, property.Value, shape: childShape);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray())
                    WriteCanonical(writer, item, shape: shape is { } arrayShape && arrayShape.TryGetProperty("items", out var itemShape) ? itemShape : null);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                string? text = value.GetString();
                if (shape is { } stringShape && stringShape.TryGetProperty("format", out var format) && format.GetString() == "uuid")
                    text = Guid.Parse(text!).ToString("D");
                writer.WriteStringValue(text);
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(NormalizeInteger(value.GetRawText()));
                break;
            case JsonValueKind.True: writer.WriteBooleanValue(true); break;
            case JsonValueKind.False: writer.WriteBooleanValue(false); break;
            case JsonValueKind.Null: writer.WriteNullValue(); break;
            default: throw new ContractValidationException(ErrorCode.INVALID_ARGUMENT);
        }
    }

    private static string NormalizeInteger(string raw)
    {
        // All numeric v1 fields are Int64 integers. Inspect decimal digits exactly;
        // a floating-point underflow must never turn a non-integral value into zero.
        if (raw.Length > 128) throw new ContractValidationException(ErrorCode.LIMIT_EXCEEDED);
        bool negative = raw[0] == '-';
        string magnitude = negative ? raw[1..] : raw;
        int exponentAt = magnitude.IndexOfAny(['e', 'E']);
        int exponent = 0;
        if (exponentAt >= 0)
        {
            if (!int.TryParse(magnitude[(exponentAt + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out exponent))
                throw new ContractValidationException(ErrorCode.VALIDATION_FAILED);
            magnitude = magnitude[..exponentAt];
        }
        int dot = magnitude.IndexOf('.');
        long shift = (long)exponent - (dot < 0 ? 0 : magnitude.Length - dot - 1);
        string digits = magnitude.Replace(".", "", StringComparison.Ordinal).TrimStart('0');
        if (digits.Length == 0) return "0";
        if (shift < 0)
        {
            if (-shift >= digits.Length || digits[^((int)-shift)..].Any(c => c != '0'))
                throw new ContractValidationException(ErrorCode.VALIDATION_FAILED);
            digits = digits[..(digits.Length + (int)shift)];
        }
        else
        {
            if (digits.Length + shift > 19) throw new ContractValidationException(ErrorCode.VALIDATION_FAILED);
            digits += new string('0', (int)shift);
        }
        if (!long.TryParse((negative ? "-" : "") + digits, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long value))
            throw new ContractValidationException(ErrorCode.VALIDATION_FAILED);
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            MaxDepth = MaxDepth,
            RespectRequiredConstructorParameters = true
        };
        options.Converters.Add(new UuidConverterFactory());
        options.Converters.Add(new MethodConverter());
        options.Converters.Add(new ResultConverter());
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        return options;
    }

    private sealed class UuidConverterFactory : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert) => typeof(UuidIdentity).IsAssignableFrom(typeToConvert);
        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
            => (JsonConverter)Activator.CreateInstance(typeof(UuidConverter<>).MakeGenericType(typeToConvert))!;
    }

    private sealed class UuidConverter<T> : JsonConverter<T> where T : UuidIdentity
    {
        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            Guid value = UuidIdentity.ParseValue(reader.GetString()!);
            return (T)Activator.CreateInstance(typeof(T), value)!;
        }
        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString());
    }

    private sealed class MethodConverter : JsonConverter<OperationMethod>
    {
        public override OperationMethod Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            string? name = reader.GetString();
            foreach (var method in Enum.GetValues<OperationMethod>())
                if (OperationCatalog.Name(method) == name) return method;
            throw new JsonException();
        }
        public override void Write(Utf8JsonWriter writer, OperationMethod value, JsonSerializerOptions options) => writer.WriteStringValue(OperationCatalog.Name(value));
    }

    private sealed class ResultConverter : JsonConverter<OperationResult>
    {
        public override OperationResult Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => throw new JsonException();
        public override void Write(Utf8JsonWriter writer, OperationResult value, JsonSerializerOptions options)
            => JsonSerializer.Serialize(writer, value, value.GetType(), options);
    }
}
