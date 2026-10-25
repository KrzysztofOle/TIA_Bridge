using System.Text.Json;
using System.Text.Json.Nodes;
using TiaBridge.Core.Operations;
using TiaBridge.Core.Transport.V1;
using Xunit;

namespace TiaBridge.Core.Tests;

public class ContractMatrixTests
{
    internal static JsonNode Schema(string name) => JsonNode.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Schemas", name + ".json")))!;
    internal static JsonNode Message(string target, string method) => JsonNode.Parse(WorkspaceTests.Fixture((target == "request" ? "request." : "response.") + method + ".json"))!;
    internal static JsonNode Error(string code) => new JsonObject
    {
        ["code"] = code,
        ["category"] = "EXECUTION",
        ["message"] = "Synthetic failure",
        ["retryable"] = false,
        ["outcome"] = "NOT_STARTED"
    };
    internal static void Read(string target, JsonNode node)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(node);
        if (target == "request") _ = ContractJson.ReadRequest(bytes);
        else if (target == "report") _ = ContractJson.ReadReport(bytes);
        else _ = ContractJson.ReadError(bytes);
    }

    [Theory]
    [MemberData(nameof(ContractTests.Methods), MemberType = typeof(ContractTests))]
    public void Shared_shape_mutations_have_the_same_verdict_as_independent_schema_tests(OperationMethod method)
    {
        string name = OperationCatalog.Name(method);
        foreach (var rule in JsonNode.Parse(WorkspaceTests.Fixture("contract-mutations.v1.json"))!.AsArray())
        {
            string target = rule!["target"]!.GetValue<string>();
            var message = target == "error" ? Error("TIMEOUT") : Message(target, name);
            var path = rule["path"]!.AsArray().Select(p => p!.GetValue<string>()).ToArray();
            JsonNode parent = message;
            foreach (var part in path.SkipLast(1)) parent = parent[part]!;
            if (path.Length > 0)
            {
                if (rule["remove"]!.GetValue<bool>()) parent.AsObject().Remove(path[^1]);
                else parent[path[^1]] = rule["value"]?.DeepClone();
            }
            bool expected = rule["valid"]!.GetValue<bool>();
            var exception = Record.Exception(() => Read(target, message));
            Assert.True(expected == (exception is null), name + ": " + rule["name"] + " " + exception?.GetType().Name);
            if (!expected) Assert.IsType<ContractValidationException>(exception);
        }
    }

    [Theory]
    [MemberData(nameof(ContractTests.Methods), MemberType = typeof(ContractTests))]
    public void Every_required_parameter_result_and_nested_member_is_individually_checked(OperationMethod method)
    {
        string name = OperationCatalog.Name(method);
        var requestShape = Schema("operation-request.schema")["oneOf"]!.AsArray().Single(s => s!["properties"]!["method"]!["const"]!.GetValue<string>() == name)!;
        var request = Message("request", name);
        AssertRequired(request["params"]!, requestShape["properties"]!["params"]!, n => { var copy = request.DeepClone(); copy["params"] = n; Read("request", copy); });
        var report = Message("report", name);
        var schema = Schema("operation.schema");
        var resultShape = schema["$defs"]![MethodContracts.Result(method).Name]!;
        AssertRequired(report["result"]!, resultShape, n => { var copy = report.DeepClone(); copy["result"] = n; Read("report", copy); });
        CheckNested(report["result"]!, resultShape, schema, n => { var copy = report.DeepClone(); copy["result"] = n; Read("report", copy); });
    }

    private static void AssertRequired(JsonNode original, JsonNode shape, Action<JsonNode> read)
    {
        if (shape["required"] is not JsonArray required) return;
        foreach (string field in required.Select(v => v!.GetValue<string>()))
        {
            var missing = original.DeepClone();
            missing.AsObject().Remove(field);
            Assert.Throws<ContractValidationException>(() => read(missing));
            var wrongType = original.DeepClone();
            wrongType[field] = new JsonObject { ["invalid_type"] = true };
            Assert.Throws<ContractValidationException>(() => read(wrongType));
        }
    }

    private static void CheckNested(JsonNode original, JsonNode shape, JsonNode schema, Action<JsonNode> read)
    {
        if (shape["$ref"] is JsonValue reference) shape = schema["$defs"]![reference.GetValue<string>().Split('/')[^1]]!;
        if (original is JsonObject obj && shape["properties"] is JsonObject props)
        {
            foreach (var field in obj.Where(p => p.Value is JsonObject or JsonArray))
            {
                var child = field.Value!;
                var childShape = props[field.Key]!;
                CheckNested(child, childShape, schema, n => { var copy = original.DeepClone(); copy[field.Key] = n; read(copy); });
            }
            // A valid sample supplies each required nested field. Removing one checks its actual boundary.
            if (shape["required"] is JsonArray required)
                foreach (string field in required.Select(v => v!.GetValue<string>()))
                {
                    var copy = original.DeepClone(); copy.AsObject().Remove(field);
                    Assert.Throws<ContractValidationException>(() => read(copy));
                }
        }
        else if (original is JsonArray array && shape["items"] is JsonNode items)
            for (int i = 0; i < array.Count; i++)
            {
                int index = i;
                CheckNested(array[i]!, items, schema, n => { var copy = original.DeepClone(); copy[index] = n; read(copy); });
            }
    }

    [Theory]
    [MemberData(nameof(ContractTests.Methods), MemberType = typeof(ContractTests))]
    public void Method_specific_errors_round_trip_and_reject_bad_shapes(OperationMethod method)
    {
        string name = OperationCatalog.Name(method);
        var row = Schema("operations")["operations"]!.AsArray().Single(o => o!["name"]!.GetValue<string>() == name)!;
        foreach (string code in row["error_codes"]!.AsArray().Select(c => c!.GetValue<string>()))
        {
            var error = Error(code);
            var report = Message("report", name);
            report["state"] = "FAILED"; report["outcome"] = "NOT_STARTED"; report["error"] = error.DeepClone();
            var parsed = ContractJson.ReadReport(JsonSerializer.SerializeToUtf8Bytes(report));
            Assert.Equal(code, parsed.Error!.Code.ToString());
            Assert.Equal(parsed.Error, ContractJson.ReadError(ContractJson.WriteError(parsed.Error)));
            Assert.Equal(parsed.Error, ContractJson.ReadReport(ContractJson.WriteReport(parsed)).Error);
            foreach (string required in Schema("error.schema")["required"]!.AsArray().Select(v => v!.GetValue<string>()))
            {
                var bad = report.DeepClone(); bad["error"]!.AsObject().Remove(required);
                Assert.Throws<ContractValidationException>(() => Read("report", bad));
            }
            report["error"]!["outcome"] = "UNKNOWN";
            report["error"]!["retryable"] = true;
            Assert.Throws<ContractValidationException>(() => Read("report", report));
        }
        var shape = Schema("operation.schema");
        var branch = shape["allOf"]!.AsArray().Single(r => r!["if"]?["properties"]?["method"]?["const"]?.GetValue<string>() == name)!;
        var allowed = branch["then"]!["properties"]!["error"]!["oneOf"]![1]!["allOf"]![1]!["properties"]!["code"]!["enum"]!.AsArray().Select(c => c!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
        string foreignCode = Schema("error.schema")["properties"]!["code"]!["enum"]!.AsArray().Select(c => c!.GetValue<string>()).First(c => !allowed.Contains(c));
        var foreign = Message("report", name);
        foreign["state"] = "FAILED"; foreign["outcome"] = "NOT_STARTED"; foreign["error"] = Error(foreignCode);
        Assert.Throws<ContractValidationException>(() => Read("report", foreign));
    }
}
