using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TiaBridge.Core.Identity;
using TiaBridge.Core.Operations;
using TiaBridge.Core.Transport.V1;
using TiaBridge.Core.Workspace;
using Xunit;

namespace TiaBridge.Core.Tests;

public class BoundaryTests
{
    private static byte[] Padded(byte[] message, int length) => message.Concat(Enumerable.Repeat((byte)' ', length - message.Length)).ToArray();

    [Fact]
    public void Request_report_and_acceptance_accept_the_exact_byte_limit_and_reject_the_next_byte()
    {
        var request = WorkspaceTests.Fixture("request.project.status.json");
        Assert.Equal(ContractJson.ReadRequest(request).Identity.BodySha256, ContractJson.ReadRequest(Padded(request, ContractJson.MaxRequestBytes)).Identity.BodySha256);
        Assert.Equal(ErrorCode.LIMIT_EXCEEDED, Assert.Throws<ContractValidationException>(() => ContractJson.ReadRequest(Padded(request, ContractJson.MaxRequestBytes + 1))).Code);
        var report = WorkspaceTests.Fixture("response.project.status.json");
        Assert.Equal(ContractJson.ReadReport(report).OperationId, ContractJson.ReadReport(Padded(report, ContractJson.MaxReportBytes)).OperationId);
        Assert.Equal(ErrorCode.LIMIT_EXCEEDED, Assert.Throws<ContractValidationException>(() => ContractJson.ReadReport(Padded(report, ContractJson.MaxReportBytes + 1))).Code);
        var acceptance = new Acceptance(new OperationId(Guid.Parse("00000000-0000-4000-8000-000000000001")), OperationState.QUEUED,
            1000, "/v1/operations/00000000-0000-4000-8000-000000000001", "/v1/operations/00000000-0000-4000-8000-000000000001/result", DateTimeOffset.Parse("2026-10-25T08:00:00Z"));
        byte[] bytes = ContractJson.WriteAcceptance(acceptance);
        Assert.Equal(acceptance, ContractJson.ReadAcceptance(Padded(bytes, ContractJson.MaxRequestBytes)));
        Assert.Throws<ContractValidationException>(() => ContractJson.ReadAcceptance(Padded(bytes, ContractJson.MaxRequestBytes + 1)));
    }

    [Fact]
    public void Event_byte_limit_counts_raw_utf8_not_only_reserialized_content()
    {
        var report = ContractMatrixTests.Message("report", "operation.events");
        report["result"]!["events"] = new JsonArray("EVENT_SENTINEL");
        const string content = "{\"sequence\":1,\"operation_id\":\"00000000-0000-4000-8000-000000000001\",\"state\":\"RUNNING\",\"phase\":\"VERIFYING\",\"occurred_at\":\"2026-10-25T08:00:00Z\",\"evidence_refs\":[]}";
        string Event(int size) => "{" + new string(' ', size - Encoding.UTF8.GetByteCount(content)) + content[1..];
        byte[] Bytes(int size) => Encoding.UTF8.GetBytes(report.ToJsonString().Replace("\"EVENT_SENTINEL\"", Event(size), StringComparison.Ordinal));
        _ = ContractJson.ReadReport(Bytes(ContractJson.MaxEventBytes));
        Assert.Equal(ErrorCode.LIMIT_EXCEEDED, Assert.Throws<ContractValidationException>(() => ContractJson.ReadReport(Bytes(ContractJson.MaxEventBytes + 1))).Code);
    }

    [Theory]
    [InlineData("ff")]
    [InlineData("c080")]
    [InlineData("eda080")]
    [InlineData("f4908080")]
    public void Malformed_utf8_is_rejected_at_every_public_json_boundary(string hex)
    {
        byte[] bytes = Convert.FromHexString(hex);
        foreach (var read in new Action<byte[]>[] { b => ContractJson.ReadRequest(b), b => ContractJson.ReadReport(b), b => ContractJson.ReadError(b), b => ContractJson.ReadAcceptance(b), b => ContractJson.ReadSessionIdentity(b), b => ContractJson.ReadIdentity<ObjectId>(b), b => ContractJson.ReadVersion(b) })
            Assert.Throws<ContractValidationException>(() => read(bytes));
        Assert.Throws<ArgumentException>(() => WorkspaceJson.ReadManifest(bytes));
    }

    [Theory]
    [InlineData("{\"a\":1,\"\\u0061\":2}")]
    [InlineData("{\"x\":\"\\uD800\"}")]
    [InlineData("{\"x\":NaN}")]
    [InlineData("{\"x\":1,}")]
    public void Ambiguous_or_malformed_json_is_rejected_without_payload_disclosure(string json)
    {
        foreach (var read in new Action<byte[]>[] { b => ContractJson.ReadReport(b), b => ContractJson.ReadError(b), b => ContractJson.ReadAcceptance(b) })
        {
            var exception = Assert.Throws<ContractValidationException>(() => read(Encoding.UTF8.GetBytes(json)));
            Assert.DoesNotContain(json, exception.ToString());
        }
    }

    [Fact]
    public void Typed_wire_values_must_not_silently_replace_invalid_unicode()
    {
        Assert.ThrowsAny<ArgumentException>(() => new IdempotencyKey("\ud800"));
        var report = ContractJson.ReadReport(WorkspaceTests.Fixture("response.project.status.json"));
        Assert.Throws<ContractValidationException>(() => ContractJson.WriteReport(report with { Phase = "\ud800" }));
        var parameters = new OperationCancelParameters(new OperationId(Guid.Parse("00000000-0000-4000-8000-000000000001")), Reason: "\ud800");
        Assert.Throws<ContractValidationException>(() => ContractJson.WriteRequest(OperationMethod.OperationCancel, null, new IdempotencyKey("synthetic"), parameters));
        Assert.Throws<ContractValidationException>(() => ContractJson.WriteError(new OperationError(ErrorCode.TIMEOUT, ErrorCategory.EXECUTION, "\ud800", false, OperationOutcome.NOT_STARTED)));
    }

    [Fact]
    public void Nested_operation_status_cannot_report_success_with_unknown_outcome()
    {
        foreach (string method in new[] { "operation.status", "operation.result" })
        {
            var report = ContractMatrixTests.Message("report", method);
            report["result"]!["state"] = "SUCCEEDED"; report["result"]!["outcome"] = "UNKNOWN";
            Assert.Throws<ContractValidationException>(() => ContractMatrixTests.Read("report", report));
        }
    }

    [Fact]
    public void Identity_and_session_versions_reject_missing_features_and_bad_bindings()
    {
        foreach (string value in new[] { "\"2.0\"", "\"1.1\"", "null", "{}" }) Assert.Throws<ContractValidationException>(() => ContractJson.ReadVersion(Encoding.UTF8.GetBytes(value)));
        var identity = new SessionIdentity(new ProjectKey(Guid.Parse("00000000-0000-4000-8000-000000000001")), new SessionEpoch(Guid.Parse("00000000-0000-4000-8000-000000000002")), 12,
            DateTimeOffset.Parse("2026-10-25T08:00:00Z"), Guid.Parse("00000000-0000-4000-8000-000000000003"));
        foreach (var mutation in new Action<JsonNode>[] { n => n["required_features"] = new JsonArray("future"), n => n["schema_version"] = "1.1", n => n["process_id"] = 0, n => n["handshake_instance"] = Guid.Empty.ToString(), n => n["process_started_at"] = "2026-10-25T09:00:00+01:00" })
        {
            var node = JsonNode.Parse(ContractJson.WriteSessionIdentity(identity))!; mutation(node);
            Assert.Throws<ContractValidationException>(() => ContractJson.ReadSessionIdentity(JsonSerializer.SerializeToUtf8Bytes(node)));
        }
    }
}
