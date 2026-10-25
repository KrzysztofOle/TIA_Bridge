using System.Text.Json;
using System.Text.Json.Nodes;
using TiaBridge.Core.Identity;
using TiaBridge.Core.Operations;
using TiaBridge.Core.Transport.V1;
using Xunit;

namespace TiaBridge.Core.Tests;

public class LifecycleMatrixTests
{
    public static IEnumerable<object[]> Transitions => from current in Enum.GetValues<OperationState>()
                                                       from next in Enum.GetValues<OperationState>()
                                                       from outcome in Enum.GetValues<OperationOutcome>()
                                                       select new object[] { current, next, outcome };

    [Theory]
    [MemberData(nameof(Transitions))]
    public void Every_lifecycle_edge_obeys_the_documented_state_and_outcome_table(OperationState current, OperationState next, OperationOutcome outcome)
    {
        // Independent table of permitted edges, not a call to the implementation to construct expectations.
        var edges = new Dictionary<OperationState, OperationState[]>
        {
            [OperationState.ACCEPTED] = new[] { OperationState.QUEUED, OperationState.FAILED, OperationState.CANCELLED },
            [OperationState.QUEUED] = new[] { OperationState.RUNNING, OperationState.FAILED, OperationState.CANCELLED },
            [OperationState.RUNNING] = new[] { OperationState.SUCCEEDED, OperationState.FAILED, OperationState.CANCELLED, OperationState.RECONCILIATION_REQUIRED }
        };
        var outcomes = new Dictionary<OperationState, OperationOutcome[]>
        {
            [OperationState.ACCEPTED] = new[] { OperationOutcome.NOT_STARTED },
            [OperationState.QUEUED] = new[] { OperationOutcome.NOT_STARTED },
            [OperationState.RUNNING] = Enum.GetValues<OperationOutcome>(),
            [OperationState.SUCCEEDED] = new[] { OperationOutcome.CONFIRMED },
            [OperationState.FAILED] = new[] { OperationOutcome.NOT_STARTED, OperationOutcome.CONFIRMED },
            [OperationState.CANCELLED] = new[] { OperationOutcome.NOT_STARTED, OperationOutcome.CONFIRMED },
            [OperationState.RECONCILIATION_REQUIRED] = new[] { OperationOutcome.UNKNOWN }
        };
        bool allowed = edges.TryGetValue(current, out var destinations) && destinations.Contains(next) && outcomes[next].Contains(outcome)
            && (current == OperationState.RUNNING || next == OperationState.RUNNING || outcome == OperationOutcome.NOT_STARTED);
        var exception = Record.Exception(() => OperationLifecycle.ValidateTransition(current, next, outcome));
        Assert.Equal(allowed, exception is null);
        if (!allowed) Assert.True(exception is ArgumentException or InvalidOperationException);
        Assert.Equal(current is OperationState.SUCCEEDED or OperationState.FAILED or OperationState.CANCELLED or OperationState.RECONCILIATION_REQUIRED, OperationLifecycle.IsTerminal(current));
    }

    [Fact]
    public void Phase_sync_state_timeout_and_cancellation_remain_independent_after_serialization()
    {
        var node = ContractMatrixTests.Message("report", "project.status");
        node["state"] = "RUNNING"; node["phase"] = "VERIFYING";
        node["result"]!["objects"] = new JsonArray(new JsonObject { ["object_id"] = "00000000-0000-4000-8000-000000000020", ["status"] = "UNSUPPORTED", ["reason_code"] = "COVERAGE_GAP" });
        var report = ContractJson.ReadReport(JsonSerializer.SerializeToUtf8Bytes(node));
        var copy = ContractJson.ReadReport(ContractJson.WriteReport(report with { Phase = "WAITING_PROJECT", CancelRequested = true }));
        Assert.Equal(OperationState.RUNNING, copy.State);
        Assert.Equal("WAITING_PROJECT", copy.Phase);
        Assert.True(copy.CancelRequested);
        Assert.IsType<ProjectStatusResult>(copy.Result);
        Assert.Equal((OperationState.RECONCILIATION_REQUIRED, OperationOutcome.UNKNOWN), OperationLifecycle.OnExecutionTimeout(true));
        Assert.NotEqual(OperationState.CANCELLED, OperationLifecycle.OnExecutionTimeout(true).State);
        node["completed_units"] = 2; node["total_units"] = 1;
        Assert.Throws<ContractValidationException>(() => ContractMatrixTests.Read("report", node));
    }

    [Theory]
    [MemberData(nameof(ContractTests.Methods), MemberType = typeof(ContractTests))]
    public void Idempotency_binds_method_project_principal_and_canonical_body_without_a_ledger(OperationMethod method)
    {
        var node = ContractMatrixTests.Message("request", OperationCatalog.Name(method));
        var request = ContractJson.ReadRequest(JsonSerializer.SerializeToUtf8Bytes(node));
        var identity = new RequestIdentity("synthetic-principal", request.Identity);
        var sorted = new JsonObject(node.AsObject().Reverse().Select(p => new KeyValuePair<string, JsonNode?>(p.Key, p.Value?.DeepClone())));
        var copy = ContractJson.ReadRequest(JsonSerializer.SerializeToUtf8Bytes(sorted));
        Assert.Equal(IdempotencyComparison.SameRequest, identity.Compare(new RequestIdentity("synthetic-principal", copy.Identity)));
        if (method is OperationMethod.OperationStatus or OperationMethod.OperationResult or OperationMethod.OperationEvents or OperationMethod.OperationCancel)
            node["params"]!["operation_id"] = "00000000-0000-4000-8000-00000000abcd";
        else if (method == OperationMethod.ProjectStatus) node["params"]!["fresh"] = true;
        else if (method == OperationMethod.DiagnosticsHealth) node["params"]!["include_cached_components"] = false;
        else node["params"]!["queue_timeout_s"] = 299;
        var changed = ContractJson.ReadRequest(JsonSerializer.SerializeToUtf8Bytes(node));
        Assert.Equal(IdempotencyComparison.Mismatch, identity.Compare(new RequestIdentity("synthetic-principal", changed.Identity)));
        Assert.Equal(IdempotencyComparison.DifferentScope, identity.Compare(new RequestIdentity("different-principal", copy.Identity)));
        var newKey = ContractJson.ReadRequest(ContractJson.WriteRequest(method, request.Identity.Project, new IdempotencyKey("different-key"), request.Parameters));
        Assert.Equal(request.Identity.BodySha256, newKey.Identity.BodySha256);
        Assert.Equal(IdempotencyComparison.DifferentScope, identity.Compare(new RequestIdentity("synthetic-principal", newKey.Identity)));
        if (request.Identity.Project is not null)
        {
            node["project_key"] = "00000000-0000-4000-8000-00000000abcd";
            Assert.Equal(IdempotencyComparison.DifferentScope, identity.Compare(new RequestIdentity("synthetic-principal", ContractJson.ReadRequest(JsonSerializer.SerializeToUtf8Bytes(node)).Identity)));
        }
    }

    [Theory]
    [MemberData(nameof(ContractTests.Methods), MemberType = typeof(ContractTests))]
    public void Execution_budgets_match_each_registry_boundary(OperationMethod method)
    {
        var limits = OperationCatalog.Timeouts(method);
        Assert.Equal(TimeSpan.FromSeconds(limits.Default), new ExecutionBudget(method).ExecutionTimeout);
        Assert.Equal(TimeSpan.FromSeconds(limits.Maximum), new ExecutionBudget(method, limits.Maximum, 3600).ExecutionTimeout);
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExecutionBudget(method, limits.Maximum + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExecutionBudget(method, queueTimeoutSeconds: 3601));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExecutionBudget(method, queueTimeoutSeconds: 0));
    }
}
