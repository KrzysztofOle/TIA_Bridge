namespace TiaBridge.Core.Operations;

/// <summary>Pure lifecycle rules. Persistent ownership and recovery belong to B-02.</summary>
public static class OperationLifecycle
{
    public static bool IsTerminal(OperationState state) => state is OperationState.SUCCEEDED or OperationState.FAILED
        or OperationState.CANCELLED or OperationState.RECONCILIATION_REQUIRED;

    public static void Validate(OperationState state, OperationOutcome outcome)
    {
        if (!Enum.IsDefined(state) || !Enum.IsDefined(outcome)
            || (state is OperationState.ACCEPTED or OperationState.QUEUED && outcome != OperationOutcome.NOT_STARTED)
            || (state == OperationState.SUCCEEDED && outcome != OperationOutcome.CONFIRMED)
            || (state == OperationState.RECONCILIATION_REQUIRED && outcome != OperationOutcome.UNKNOWN)
            || (outcome == OperationOutcome.UNKNOWN && state is not (OperationState.RUNNING or OperationState.RECONCILIATION_REQUIRED)))
            throw new ArgumentException("Invalid lifecycle and outcome combination.");
    }

    public static void ValidateTransition(OperationState current, OperationState next, OperationOutcome outcome)
    {
        Validate(next, outcome);
        if (current is OperationState.ACCEPTED or OperationState.QUEUED
            && next is OperationState.FAILED or OperationState.CANCELLED && outcome != OperationOutcome.NOT_STARTED)
            throw new ArgumentException("An undispatched operation cannot claim execution effects.");
        bool allowed = (current, next) switch
        {
            (OperationState.ACCEPTED, OperationState.QUEUED or OperationState.FAILED or OperationState.CANCELLED) => true,
            (OperationState.QUEUED, OperationState.RUNNING or OperationState.FAILED or OperationState.CANCELLED) => true,
            (OperationState.RUNNING, OperationState.SUCCEEDED or OperationState.FAILED or OperationState.CANCELLED or OperationState.RECONCILIATION_REQUIRED) => true,
            _ => false
        };
        if (!allowed) throw new InvalidOperationException("Lifecycle transition is not permitted.");
    }

    public static (OperationState State, OperationOutcome Outcome) OnExecutionTimeout(bool mutationMayHaveStarted)
        => mutationMayHaveStarted
            ? (OperationState.RECONCILIATION_REQUIRED, OperationOutcome.UNKNOWN)
            : (OperationState.FAILED, OperationOutcome.NOT_STARTED);
}
