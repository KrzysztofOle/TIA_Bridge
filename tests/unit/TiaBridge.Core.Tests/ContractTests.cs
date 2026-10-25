using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TiaBridge.Core.Identity;
using TiaBridge.Core.Operations;
using TiaBridge.Core.Transport.V1;
using Xunit;

namespace TiaBridge.Core.Tests;

public class ContractTests
{
    private static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures");
    public static IEnumerable<object[]> Methods => Enum.GetValues<OperationMethod>().Select(m => new object[] { m });
    private static byte[] Request(OperationMethod method) => File.ReadAllBytes(Path.Combine(Fixtures, "request." + OperationCatalog.Name(method) + ".json"));
    private static byte[] Response(OperationMethod method) => File.ReadAllBytes(Path.Combine(Fixtures, "response." + OperationCatalog.Name(method) + ".json"));
    private static byte[] Bytes(JsonNode node) => JsonSerializer.SerializeToUtf8Bytes(node);
    public static IEnumerable<object[]> InvalidExamples => JsonNode.Parse(File.ReadAllBytes(Path.Combine(Fixtures, "invalid-requests.json")))!.AsArray()
        .Select(c => new object[] { c!["name"]!.GetValue<string>(), c["request"]!.ToJsonString() });

    [Theory]
    [MemberData(nameof(InvalidExamples))]
    public void Published_invalid_examples_are_rejected(string name, string json)
    {
        Assert.False(string.IsNullOrEmpty(name));
        Assert.Throws<ContractValidationException>(() => ContractJson.ReadRequest(Encoding.UTF8.GetBytes(json)));
    }

    [Theory]
    [InlineData("1e-1000")]
    [InlineData("0.000000000000000000000000000001")]
    [InlineData("100000000000000000000000000001e-29")]
    [InlineData("1e1000")]
    public void Numeric_underflow_overflow_and_fractional_values_are_not_rounded(string number)
    {
        var original = Encoding.UTF8.GetString(Request(OperationMethod.OperationEvents));
        string json = original.Replace("\"params\": {", "\"params\": {\"after_sequence\":" + number + ",", StringComparison.Ordinal);
        Assert.Throws<ContractValidationException>(() => ContractJson.ReadRequest(Encoding.UTF8.GetBytes(json)));
    }

    [Theory]
    [MemberData(nameof(Methods))]
    public void Every_method_has_valid_typed_request_and_response_round_trip(OperationMethod method)
    {
        var request = ContractJson.ReadRequest(Request(method));
        Assert.Equal(method, request.Identity.Method);
        Assert.Equal(MethodContracts.Parameters(method), request.Parameters.GetType());
        byte[] encoded = ContractJson.WriteRequest(method, request.Identity.Project, request.Identity.IdempotencyKey, request.Parameters);
        Assert.Equal(request.Identity.BodySha256, ContractJson.ReadRequest(encoded).Identity.BodySha256);
        var response = ContractJson.ReadReport(Response(method));
        Assert.Equal(method, response.Method);
        Assert.NotNull(response.Result);
        Assert.Equal(MethodContracts.Result(method), response.Result.GetType());
        var expected = JsonNode.Parse(Response(method))!;
        var actual = JsonNode.Parse(ContractJson.WriteReport(response))!;
        NormalizeTimestamps(expected);
        NormalizeTimestamps(actual);
        Assert.True(JsonNode.DeepEquals(expected, actual));
    }

    private static void NormalizeTimestamps(JsonNode node)
    {
        if (node is JsonObject obj)
            foreach (var pair in obj.ToArray())
            {
                if (pair.Value is null) continue;
                if (pair.Key.EndsWith("_at", StringComparison.Ordinal) && pair.Value is JsonValue)
                    obj[pair.Key] = DateTimeOffset.Parse(pair.Value.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture).ToUniversalTime().ToString("O");
                else NormalizeTimestamps(pair.Value);
            }
        else if (node is JsonArray array)
            foreach (var item in array) if (item is not null) NormalizeTimestamps(item);
    }

    [Fact]
    public void Concurrent_validation_preserves_request_hashes_and_rejections()
    {
        var bytes = Request(OperationMethod.OperationEvents);
        string expected = ContractJson.ReadRequest(bytes).Identity.BodySha256;
        Parallel.For(0, 100, _ =>
        {
            Assert.Equal(expected, ContractJson.ReadRequest(bytes).Identity.BodySha256);
            var invalid = JsonNode.Parse(bytes)!;
            invalid["params"]!["limit"] = -1;
            Assert.Throws<ContractValidationException>(() => ContractJson.ReadRequest(Bytes(invalid)));
        });
    }

    [Theory]
    [MemberData(nameof(Methods))]
    public void Every_method_rejects_missing_required_data_and_unknown_fields(OperationMethod method)
    {
        var root = JsonNode.Parse(Request(method))!;
        root["params"]!["execute_shell"] = "synthetic forbidden command";
        Assert.Equal(ErrorCode.VALIDATION_FAILED, Assert.Throws<ContractValidationException>(() => ContractJson.ReadRequest(Bytes(root))).Code);
        root = JsonNode.Parse(Request(method))!;
        root.AsObject().Remove("idempotency_key");
        Assert.Throws<ContractValidationException>(() => ContractJson.ReadRequest(Bytes(root)));
        root = JsonNode.Parse(Response(method))!;
        root["result"]!["undeclared"] = "synthetic";
        Assert.Throws<ContractValidationException>(() => ContractJson.ReadReport(Bytes(root)));
        root = JsonNode.Parse(Response(method))!;
        root.AsObject().Remove("result");
        Assert.Throws<ContractValidationException>(() => ContractJson.ReadReport(Bytes(root)));
    }

    [Theory]
    [InlineData("schema_version", "2.0", ErrorCode.SCHEMA_VERSION)]
    [InlineData("project_key", "../project.ap19", ErrorCode.VALIDATION_FAILED)]
    [InlineData("idempotency_key", "", ErrorCode.VALIDATION_FAILED)]
    [InlineData("method", "shell.execute", ErrorCode.VALIDATION_FAILED)]
    public void Invalid_envelope_data_is_classified(string field, string value, ErrorCode expected)
    {
        var root = JsonNode.Parse(Request(OperationMethod.SyncApply))!;
        root[field] = value;
        Assert.Equal(expected, Assert.Throws<ContractValidationException>(() => ContractJson.ReadRequest(Bytes(root))).Code);
    }

    [Fact]
    public void Authority_cannot_be_constructed_in_json()
    {
        var root = JsonNode.Parse(Request(OperationMethod.SyncApply))!;
        root["params"]!.AsObject().Remove("authorization_ref");
        Assert.Throws<ContractValidationException>(() => ContractJson.ReadRequest(Bytes(root)));
        root["params"]!["authorization_ref"] = "synthetic.unverified.reference";
        Assert.IsType<SyncApplyParameters>(ContractJson.ReadRequest(Bytes(root)).Parameters);
        // Shape acceptance deliberately has no permission/grant output.
        root["permissions"] = "E";
        Assert.Throws<ContractValidationException>(() => ContractJson.ReadRequest(Bytes(root)));
        root.AsObject().Remove("permissions");
        root["params"]!["authorization_ref"] = new JsonObject { ["permissions"] = "E" };
        Assert.Throws<ContractValidationException>(() => ContractJson.ReadRequest(Bytes(root)));
        root = JsonNode.Parse(Request(OperationMethod.TestPlc))!;
        root["params"]!["allow_writes"] = true;
        Assert.Throws<ContractValidationException>(() => ContractJson.ReadRequest(Bytes(root)));
    }

    [Theory]
    [InlineData("{\"schema_version\":\"1.0\",\"schema_version\":\"1.0\"}")]
    [InlineData("{\"params\":{\"a\":1,\"\\u0061\":2}}")]
    [InlineData("{\"schema_version\":\"1.0\",\"params\":NaN}")]
    [InlineData("{\"schema_version\":\"1.0\",}")]
    [InlineData("null")]
    public void Ambiguous_or_malformed_json_is_rejected_without_echoing_input(string text)
    {
        var exception = Assert.Throws<ContractValidationException>(() => ContractJson.ReadRequest(Encoding.UTF8.GetBytes(text)));
        Assert.DoesNotContain(text, exception.ToString());
    }

    [Fact]
    public void Limits_and_invalid_utf8_are_enforced_before_deserialization()
    {
        Assert.Equal(ErrorCode.LIMIT_EXCEEDED, Assert.Throws<ContractValidationException>(() => ContractJson.ReadRequest(new byte[ContractJson.MaxRequestBytes + 1])).Code);
        string deep = new('[', ContractJson.MaxDepth + 1);
        deep += new string(']', ContractJson.MaxDepth + 1);
        Assert.Throws<ContractValidationException>(() => ContractJson.ReadRequest(Encoding.UTF8.GetBytes(deep)));
        Assert.Throws<ContractValidationException>(() => ContractJson.ReadRequest(new byte[] { 0xff, 0xff }));
        var root = JsonNode.Parse(Request(OperationMethod.OperationEvents))!;
        root["params"]!["after_sequence"] = JsonNode.Parse("9223372036854775808");
        Assert.Throws<ContractValidationException>(() => ContractJson.ReadRequest(Bytes(root)));
        root["params"]!["limit"] = 1001;
        Assert.Throws<ContractValidationException>(() => ContractJson.ReadRequest(Bytes(root)));
    }

    [Fact]
    public void Diagnostics_do_not_include_untrusted_values()
    {
        var root = JsonNode.Parse(Request(OperationMethod.SyncApply))!;
        const string secret = "SYNTHETIC_SENSITIVE_VALUE";
        root[secret] = secret;
        var error = Assert.Throws<ContractValidationException>(() => ContractJson.ReadRequest(Bytes(root)));
        Assert.DoesNotContain(secret, error.ToString());
        Assert.Equal(ErrorCategory.VALIDATION, error.Category);
    }

    [Fact]
    public void Unknown_mutation_outcome_cannot_claim_success_or_retry()
    {
        var report = JsonNode.Parse(Response(OperationMethod.SyncApply))!;
        report["state"] = "RECONCILIATION_REQUIRED";
        report["outcome"] = "UNKNOWN";
        report["error"] = new JsonObject { ["code"] = "VENDOR_CALL_TIMEOUT", ["category"] = "EXECUTION", ["message"] = "Synthetic timeout", ["retryable"] = false, ["outcome"] = "UNKNOWN" };
        Assert.Equal(OperationState.RECONCILIATION_REQUIRED, ContractJson.ReadReport(Bytes(report)).State);
        report["error"]!["retryable"] = true;
        Assert.Throws<ContractValidationException>(() => ContractJson.ReadReport(Bytes(report)));
        report["error"]!["retryable"] = false;
        report["state"] = "SUCCEEDED";
        Assert.Throws<ContractValidationException>(() => ContractJson.ReadReport(Bytes(report)));
    }

    [Fact]
    public void Nested_enums_and_result_method_mismatches_are_rejected()
    {
        var report = JsonNode.Parse(Response(OperationMethod.CompilePlc))!;
        report["result"]!["status"] = "SYNCED";
        Assert.Throws<ContractValidationException>(() => ContractJson.ReadReport(Bytes(report)));
        var parsed = ContractJson.ReadReport(Response(OperationMethod.CompilePlc));
        var foreign = ContractJson.ReadReport(Response(OperationMethod.DiagnosticsHealth));
        Assert.Throws<ContractValidationException>(() => ContractJson.WriteReport(parsed with { Result = foreign.Result }));
    }

    [Fact]
    public void Acceptance_is_nonterminal_and_cannot_claim_success()
    {
        var id = new OperationId(Guid.NewGuid());
        var acceptance = new Acceptance(id, OperationState.ACCEPTED, 1000,
            "/v1/operations/" + id, "/v1/operations/" + id + "/result", DateTimeOffset.UtcNow);
        Assert.Equal(acceptance, ContractJson.ReadAcceptance(ContractJson.WriteAcceptance(acceptance)));
        Assert.Throws<ContractValidationException>(() => ContractJson.WriteAcceptance(acceptance with { State = OperationState.SUCCEEDED }));
    }

    [Fact]
    public void Reports_and_acceptance_accept_equivalent_integral_notation()
    {
        string report = Encoding.UTF8.GetString(Response(OperationMethod.ProjectStatus)).Replace("\"age_ms\": 0", "\"age_ms\": 0e0", StringComparison.Ordinal)
            .Replace("\"poll_after_ms\": 0", "\"poll_after_ms\": 0.0", StringComparison.Ordinal);
        Assert.Equal(0, ContractJson.ReadReport(Encoding.UTF8.GetBytes(report)).PollAfterMs);
        const string acceptance = "{\"operation_id\":\"11111111-1111-4111-8111-111111111111\",\"state\":\"ACCEPTED\",\"poll_after_ms\":1e3,\"status_url\":\"/v1/operations/11111111-1111-4111-8111-111111111111\",\"result_url\":\"/v1/operations/11111111-1111-4111-8111-111111111111/result\",\"accepted_at\":\"2026-10-25T08:00:00Z\"}";
        Assert.Equal(1000, ContractJson.ReadAcceptance(Encoding.UTF8.GetBytes(acceptance)).PollAfterMs);
        var error = new OperationError(ErrorCode.TIMEOUT, ErrorCategory.EXECUTION, "Synthetic timeout", false, OperationOutcome.UNKNOWN);
        Assert.Equal(error, ContractJson.ReadError(ContractJson.WriteError(error)));
    }

    [Fact]
    public void Individual_events_are_bounded_even_when_the_report_fits()
    {
        var report = JsonNode.Parse(Response(OperationMethod.OperationEvents))!;
        var evidence = new JsonArray(Enumerable.Range(0, 600).Select(_ => (JsonNode?)JsonValue.Create(new string('a', 128))).ToArray());
        report["result"]!["events"] = new JsonArray(new JsonObject
        {
            ["sequence"] = 1,
            ["operation_id"] = "11111111-1111-4111-8111-111111111111",
            ["state"] = "RUNNING",
            ["phase"] = "VERIFYING",
            ["occurred_at"] = "2026-10-25T08:00:00Z",
            ["evidence_refs"] = evidence
        });
        Assert.Equal(ErrorCode.LIMIT_EXCEEDED, Assert.Throws<ContractValidationException>(() => ContractJson.ReadReport(Bytes(report))).Code);
    }

    [Fact]
    public void Core_assembly_has_no_host_or_vendor_dependencies()
    {
        string[] references = typeof(OperationRequest).Assembly.GetReferencedAssemblies().Select(a => a.Name!).ToArray();
        Assert.DoesNotContain(references, name => name.Contains("Siemens", StringComparison.OrdinalIgnoreCase) || name.Contains("TiaBridge.Host", StringComparison.OrdinalIgnoreCase) || name.Contains("PLCSIM", StringComparison.OrdinalIgnoreCase) || name.Contains("WinCC", StringComparison.OrdinalIgnoreCase));
    }
}
