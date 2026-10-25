using System.Collections.Immutable;
using TiaBridge.Core.Identity;
using TiaBridge.Core.Operations;

namespace TiaBridge.Core.Transport.V1;

public abstract record WireValue;
public abstract record OperationParameters : WireValue;
public abstract record OperationResult : WireValue;

public sealed record OperationError(ErrorCode Code, ErrorCategory Category, string Message, bool Retryable,
    OperationOutcome Outcome, string? Phase = null, ImmutableArray<string>? EvidenceRefs = null, string? RecoveryHint = null);

public sealed record OperationReport(
    string SchemaVersion, OperationId OperationId, OperationMethod Method,
    OperationState State, string Phase, OperationOutcome Outcome, bool CancelRequested,
    long PollAfterMs, ImmutableArray<ArtifactRef> ArtifactRefs,
    OperationError? Error = null, OperationResult? Result = null, bool? WorkspaceAdvanced = null,
    DateTimeOffset? AcceptedAt = null, DateTimeOffset? StartedAt = null, DateTimeOffset? CompletedAt = null,
    DateTimeOffset? DeadlineAt = null, long? ElapsedMs = null, long? CompletedUnits = null, long? TotalUnits = null,
    DateTimeOffset? LastProgressAt = null, long? WorkerHeartbeatAgeMs = null, SessionEpoch? SessionEpoch = null,
    string? StatusUrl = null, string? ResultUrl = null);

public sealed record Acceptance(OperationId OperationId, OperationState State, long PollAfterMs,
    string StatusUrl, string ResultUrl, DateTimeOffset AcceptedAt);

public sealed class ValidatedRequest
{
    public OperationRequest Identity { get; }
    public OperationParameters Parameters { get; }

    internal ValidatedRequest(OperationRequest identity, OperationParameters parameters)
    {
        Identity = identity;
        Parameters = parameters;
    }
}

public sealed class ContractValidationException : Exception
{
    public ErrorCode Code { get; }
    public ErrorCategory Category => ErrorCategory.VALIDATION;

    internal ContractValidationException(ErrorCode code) : base(code switch
    {
        ErrorCode.LIMIT_EXCEEDED => "Message exceeds a contract limit.",
        ErrorCode.SCHEMA_VERSION => "Unsupported or missing schema version.",
        ErrorCode.VALIDATION_FAILED => "Message does not match the versioned contract.",
        _ => "Invalid JSON message."
    }) => Code = code;
}
