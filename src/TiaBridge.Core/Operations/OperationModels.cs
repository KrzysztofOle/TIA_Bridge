using TiaBridge.Core.Identity;

namespace TiaBridge.Core.Operations;

public enum OperationState { ACCEPTED, QUEUED, RUNNING, SUCCEEDED, FAILED, CANCELLED, RECONCILIATION_REQUIRED }
public enum OperationOutcome { NOT_STARTED, CONFIRMED, UNKNOWN }
public enum ErrorCategory { VALIDATION, AUTHORIZATION, IDENTITY, CONFLICT, CAPABILITY, STORAGE, TRANSPORT, EXECUTION, INTERNAL }
public enum SynchronizationStatus { SYNCED, MODIFIED, CONFLICT, UNSUPPORTED, ERROR, UNKNOWN }
public enum CompilationStatus { PASS, FAIL, ERROR, NOT_RUN }
public enum SimulationState { INSTALLED, STARTING, RUNNING, PROGRAM_LOADED, READY, NOT_READY, STOPPED, ERROR }
public enum RuntimeTestStatus { PASS, FAIL, ERROR, BLOCKED, NOT_RUN, SKIP }

public sealed record OperationPhase
{
    public string Value { get; }
    public OperationPhase(string value)
    {
        if (value is null || value.Length > 128 || !System.Text.RegularExpressions.Regex.IsMatch(value, "\\A[A-Za-z0-9][A-Za-z0-9_.:-]*\\z", System.Text.RegularExpressions.RegexOptions.CultureInvariant))
            throw new ArgumentException("Invalid operation phase.");
        Value = value;
    }
    public override string ToString() => Value;
}

public sealed record IdempotencyKey
{
    public string Value { get; }
    public IdempotencyKey(string value)
    {
        if (string.IsNullOrEmpty(value) || value.EnumerateRunes().Count() > 128)
            throw new ArgumentException("Invalid idempotency key.");
        Value = value;
    }
    // Deliberately exclude the potentially sensitive caller-supplied value.
    public override string ToString() => "IdempotencyKey";
}

public sealed class OperationRequest
{
    public ContractVersion Version { get; }
    public OperationMethod Method { get; }
    public ProjectKey? Project { get; }
    public IdempotencyKey IdempotencyKey { get; }
    private readonly byte[] canonicalBody;
    public string BodySha256 { get; }

    internal OperationRequest(ContractVersion version, OperationMethod method, ProjectKey? project,
        IdempotencyKey key, byte[] canonicalBody)
    {
        Version = version;
        Method = method;
        Project = project;
        IdempotencyKey = key;
        this.canonicalBody = canonicalBody.ToArray();
        BodySha256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(canonicalBody));
    }

    public byte[] GetCanonicalBody() => canonicalBody.ToArray();
}

/// <summary>No ledger, dispatch, retry or authorization is performed here.</summary>
public sealed record RequestIdentity
{
    public string Principal { get; }
    public ProjectKey? Project { get; }
    public IdempotencyKey Key { get; }
    public string BodySha256 { get; }

    public RequestIdentity(string authenticatedPrincipal, OperationRequest request)
    {
        if (string.IsNullOrWhiteSpace(authenticatedPrincipal)) throw new ArgumentException("Authenticated principal required.");
        ArgumentNullException.ThrowIfNull(request);
        Principal = authenticatedPrincipal;
        Project = request.Project;
        Key = request.IdempotencyKey;
        BodySha256 = request.BodySha256;
    }

    public IdempotencyComparison Compare(RequestIdentity previous)
    {
        ArgumentNullException.ThrowIfNull(previous);
        if (Principal != previous.Principal || Project != previous.Project || Key != previous.Key)
            return IdempotencyComparison.DifferentScope;
        return BodySha256 == previous.BodySha256 ? IdempotencyComparison.SameRequest : IdempotencyComparison.Mismatch;
    }

    public override string ToString() => "RequestIdentity";
}

public enum IdempotencyComparison { SameRequest, Mismatch, DifferentScope }

public sealed record ExecutionBudget
{
    public TimeSpan QueueTimeout { get; }
    public TimeSpan ExecutionTimeout { get; }

    public ExecutionBudget(OperationMethod method, int? timeoutSeconds = null, int queueTimeoutSeconds = 300)
    {
        var limits = OperationCatalog.Timeouts(method);
        int timeout = timeoutSeconds ?? limits.Default;
        if (timeout <= 0 || timeout > limits.Maximum || queueTimeoutSeconds <= 0 || queueTimeoutSeconds > 3600)
            throw new ArgumentOutOfRangeException(nameof(timeoutSeconds), "Invalid operation budget.");
        QueueTimeout = TimeSpan.FromSeconds(queueTimeoutSeconds);
        ExecutionTimeout = TimeSpan.FromSeconds(timeout);
    }

    public DateTimeOffset ExecutionDeadline(DateTimeOffset startedAt) => startedAt + ExecutionTimeout;
}

public sealed record ExecutionMetadata(OperationId OperationId, DateTimeOffset AcceptedAt,
    DateTimeOffset? StartedAt, DateTimeOffset? DeadlineAt, SessionEpoch? SessionEpoch);
