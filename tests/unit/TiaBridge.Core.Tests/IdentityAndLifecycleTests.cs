using System.Text;
using TiaBridge.Core.Identity;
using TiaBridge.Core.Operations;
using TiaBridge.Core.Transport.V1;
using Xunit;

namespace TiaBridge.Core.Tests;

public class IdentityAndLifecycleTests
{
    [Fact]
    public void Uuid_identity_is_typed_and_serializes_as_a_canonical_scalar()
    {
        Guid value = Guid.Parse("abcdefab-1111-4111-8111-111111111111");
        var project = new ProjectKey(value);
        Assert.Equal(project, new ProjectKey(UuidIdentity.ParseValue(value.ToString().ToUpperInvariant())));
        Assert.NotEqual<UuidIdentity>(project, new ObjectId(value));
        Assert.Equal(0, project.CompareTo(new ProjectKey(value)));
        Assert.Equal("\"abcdefab-1111-4111-8111-111111111111\"", Encoding.UTF8.GetString(ContractJson.WriteIdentity(project)));
        Assert.Equal(project, ContractJson.ReadIdentity<ProjectKey>(ContractJson.WriteIdentity(project)));
        Assert.Throws<ContractValidationException>(() => ContractJson.ReadIdentity<ObjectId>(Encoding.UTF8.GetBytes("\"not-a-uuid\"")));
        Assert.Throws<ContractValidationException>(() => ContractJson.ReadIdentity<ObjectId>(Encoding.UTF8.GetBytes("{}")));
        Assert.Throws<ContractValidationException>(() => ContractJson.ReadIdentity<ObjectId>(Encoding.UTF8.GetBytes("null")));
        Assert.Equal(ContractVersion.V1, ContractJson.ReadVersion(ContractJson.WriteVersion(ContractVersion.V1)));
    }

    [Fact]
    public void Object_identity_excludes_mutable_names_paths_and_vendor_handles()
    {
        var objectId = new ObjectId(Guid.NewGuid());
        var project = new ProjectKey(Guid.NewGuid());
        Assert.Equal(new ObjectIdentity(project, objectId), new ObjectIdentity(project, objectId));
        Assert.NotEqual(new ObjectIdentity(project, objectId), new ObjectIdentity(new ProjectKey(Guid.NewGuid()), objectId));
        Assert.Throws<ArgumentNullException>(() => new ObjectIdentity(project, null!));
        Assert.Equal(new[] { "Project", "Object" }, typeof(ObjectIdentity).GetProperties().Select(p => p.Name));
    }

    [Fact]
    public void Session_binding_includes_process_start_and_handshake_not_just_pid()
    {
        var project = new ProjectKey(Guid.NewGuid());
        var epoch = new SessionEpoch(Guid.NewGuid());
        var start = DateTimeOffset.Parse("2026-10-25T08:00:00Z");
        var session = new SessionIdentity(project, epoch, 123, start, Guid.NewGuid());
        Assert.NotEqual(session, new SessionIdentity(project, epoch, 123, start.AddSeconds(1), Guid.NewGuid()));
        Assert.Equal(session, ContractJson.ReadSessionIdentity(ContractJson.WriteSessionIdentity(session)));
        Assert.Throws<ContractValidationException>(() => ContractJson.ReadSessionIdentity(Encoding.UTF8.GetBytes("{\"schema_version\":\"1.0\"}")));
        Assert.Throws<ArgumentException>(() => new SessionIdentity(project, epoch, 0, start, Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() => new SessionIdentity(project, epoch, 123, start, Guid.Empty));
        Assert.Throws<ArgumentException>(() => new ContractVersion("1.1"));
    }

    [Fact]
    public void Canonicalization_normalizes_typed_uuids_but_preserves_arrays_and_optional_fields()
    {
        const string id = "abcdefab-1111-4111-8111-111111111111";
        var first = Read("{\"operation_id\":\"" + id + "\"}");
        var second = Read("{\"operation_id\":\"" + id.ToUpperInvariant() + "\"}");
        Assert.Equal(first.Identity.BodySha256, second.Identity.BodySha256);
        Assert.NotEqual(first.Identity.BodySha256, Read("{\"operation_id\":\"" + id + "\",\"limit\":100}").Identity.BodySha256);
        string Body(string scope) => "{\"schema_version\":\"1.0\",\"method\":\"project.export\",\"project_key\":\"" + id + "\",\"idempotency_key\":\"synthetic\",\"params\":{\"scope\":" + scope + "}}";
        Assert.NotEqual(ContractJson.ReadRequest(Encoding.UTF8.GetBytes(Body("[\"a\",\"b\"]"))).Identity.BodySha256,
            ContractJson.ReadRequest(Encoding.UTF8.GetBytes(Body("[\"b\",\"a\"]"))).Identity.BodySha256);
    }

    [Fact]
    public void Cancellation_before_dispatch_cannot_claim_confirmed_execution()
    {
        OperationLifecycle.ValidateTransition(OperationState.QUEUED, OperationState.CANCELLED, OperationOutcome.NOT_STARTED);
        Assert.Throws<ArgumentException>(() => OperationLifecycle.ValidateTransition(OperationState.QUEUED, OperationState.CANCELLED, OperationOutcome.CONFIRMED));
        Assert.Throws<InvalidOperationException>(() => OperationLifecycle.ValidateTransition(OperationState.CANCELLED, OperationState.RUNNING, OperationOutcome.NOT_STARTED));
        Assert.Throws<ArgumentException>(() => new OperationPhase("../unsafe phase"));
        Assert.Equal(new OperationPhase("IMPORTING"), new OperationPhase("IMPORTING"));
    }

    private static ValidatedRequest Read(string parameters, string key = "synthetic-key") => ContractJson.ReadRequest(Encoding.UTF8.GetBytes("{\"schema_version\":\"1.0\",\"method\":\"operation.events\",\"idempotency_key\":\"" + key + "\",\"params\":" + parameters + "}"));

    [Fact]
    public void Canonicalization_normalizes_whitespace_key_order_escapes_and_integral_notation()
    {
        const string id = "11111111-1111-4111-8111-111111111111";
        var first = Read("{\"operation_id\":\"" + id + "\", \"limit\":1.0}");
        var second = Read("{ \"limit\":1e0,\"operation_\\u0069d\":\"" + id + "\" }");
        Assert.Equal(first.Identity.BodySha256, second.Identity.BodySha256);
        Assert.Equal(first.Identity.GetCanonicalBody(), second.Identity.GetCanonicalBody());
        Assert.DoesNotContain("idempotency_key", Encoding.UTF8.GetString(first.Identity.GetCanonicalBody()));
        for (int i = 0; i < 100; i++) Assert.Equal(first.Identity.BodySha256, Read("{\"limit\":1,\"operation_id\":\"" + id + "\"}").Identity.BodySha256);
        byte[] copy = first.Identity.GetCanonicalBody();
        copy[0] = 0;
        Assert.NotEqual(copy, first.Identity.GetCanonicalBody());
    }

    [Fact]
    public void Idempotency_is_scoped_to_authenticated_principal_project_and_key()
    {
        const string parameters = "{\"operation_id\":\"11111111-1111-4111-8111-111111111111\"}";
        var first = Read(parameters);
        var identity = new RequestIdentity("synthetic-principal", first.Identity);
        Assert.Equal(IdempotencyComparison.SameRequest, identity.Compare(new RequestIdentity("synthetic-principal", Read(parameters).Identity)));
        Assert.Equal(IdempotencyComparison.DifferentScope, identity.Compare(new RequestIdentity("another-principal", first.Identity)));
        Assert.Equal(IdempotencyComparison.DifferentScope, identity.Compare(new RequestIdentity("synthetic-principal", Read(parameters, "another-key").Identity)));
        Assert.Equal(IdempotencyComparison.Mismatch, identity.Compare(new RequestIdentity("synthetic-principal", Read(parameters.Replace("111111111111", "222222222222")).Identity)));
        Assert.DoesNotContain("synthetic-principal", identity.ToString());
        Assert.Throws<ArgumentException>(() => new IdempotencyKey(new string('a', 129)));
    }

    [Fact]
    public void Terminal_unknown_outcome_never_requeues_or_accepts_a_late_success()
    {
        OperationLifecycle.ValidateTransition(OperationState.ACCEPTED, OperationState.QUEUED, OperationOutcome.NOT_STARTED);
        OperationLifecycle.ValidateTransition(OperationState.QUEUED, OperationState.RUNNING, OperationOutcome.NOT_STARTED);
        OperationLifecycle.ValidateTransition(OperationState.RUNNING, OperationState.RECONCILIATION_REQUIRED, OperationOutcome.UNKNOWN);
        Assert.Throws<InvalidOperationException>(() => OperationLifecycle.ValidateTransition(OperationState.RECONCILIATION_REQUIRED, OperationState.QUEUED, OperationOutcome.NOT_STARTED));
        Assert.Throws<InvalidOperationException>(() => OperationLifecycle.ValidateTransition(OperationState.RECONCILIATION_REQUIRED, OperationState.SUCCEEDED, OperationOutcome.CONFIRMED));
        Assert.Throws<ArgumentException>(() => OperationLifecycle.Validate(OperationState.SUCCEEDED, OperationOutcome.UNKNOWN));
        Assert.Throws<ArgumentException>(() => OperationLifecycle.Validate(OperationState.RECONCILIATION_REQUIRED, OperationOutcome.CONFIRMED));
        Assert.Throws<ArgumentException>(() => OperationLifecycle.Validate((OperationState)100, OperationOutcome.CONFIRMED));
    }

    [Fact]
    public void Timeout_is_not_a_cancellation_or_proof_of_worker_termination()
    {
        Assert.Equal((OperationState.RECONCILIATION_REQUIRED, OperationOutcome.UNKNOWN), OperationLifecycle.OnExecutionTimeout(true));
        Assert.Equal((OperationState.FAILED, OperationOutcome.NOT_STARTED), OperationLifecycle.OnExecutionTimeout(false));
        var budget = new ExecutionBudget(OperationMethod.SyncApply);
        Assert.Equal(TimeSpan.FromSeconds(300), budget.ExecutionTimeout);
        Assert.Equal(TimeSpan.FromSeconds(300), budget.QueueTimeout);
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExecutionBudget(OperationMethod.SyncApply, 1801));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExecutionBudget(OperationMethod.SyncApply, 0));
    }
}
