using TiaBridge.Core.Identity;
using TiaBridge.Core.Capabilities;
using TiaBridge.Core.Operations;
using TiaBridge.Host.Storage;
using TiaBridge.Ipc;

namespace TiaBridge.Host.Scheduling;

public sealed record OwnerToken(Guid Epoch, long Fence);
public sealed record AttemptBinding(OperationId OperationId, int Attempt, Guid WorkerEpoch, long Fence, string InputHash, string? PlanHash, bool Mutation, DateTimeOffset Deadline);
public sealed record ExecutionSpec(string PortalKey, string Capability, bool QualifiedEffectFreeRead, ArtifactDescriptor? SuccessReport, FakeScenario Scenario = FakeScenario.Success, int DelayMs = 0, ErrorCode FailureCode = ErrorCode.TIA_UNAVAILABLE, V19Target? V19Target = null, CapabilityBinding? CapabilityBinding = null, string? QualificationDecisionHash = null)
{
    internal Qualification.BootstrapPermit? Bootstrap { get; init; }
}
/// <summary>Explicit synthetic qualification. This does not grant any Siemens capability or session permission.</summary>
public interface IExecutionProfile
{
    void BeginSession(OwnerToken owner) { }
    void EndSession(bool released) { }
    void WorkerLost() { }
    ExecutionSpec? Resolve(StoredOperation operation);
    bool Revalidate(StoredOperation operation, ExecutionSpec admitted)
    {
        var current = Resolve(operation);
        return current is not null && current.PortalKey == admitted.PortalKey && current.Capability == admitted.Capability
            && current.V19Target == admitted.V19Target && current.CapabilityBinding == admitted.CapabilityBinding
            && current.QualificationDecisionHash == admitted.QualificationDecisionHash && current.Bootstrap == admitted.Bootstrap;
    }
}
public interface IJitter { double NextUnit(); }
public sealed class RandomJitter : IJitter { public double NextUnit() => Random.Shared.NextDouble(); }
public static class RetryPolicy
{
    public static bool IsMutation(OperationMethod method) => method is not (OperationMethod.ProjectInspect or OperationMethod.ProjectExport or OperationMethod.ProjectStatus or OperationMethod.FilesChanges or OperationMethod.DiagnosticsHealth or OperationMethod.OperationStatus or OperationMethod.OperationResult or OperationMethod.OperationEvents);
    public static bool CanRetry(bool qualifiedRead, bool mutation, bool quiescent, int attempt, ErrorCode error)
        => qualifiedRead && !mutation && quiescent && attempt < 3 && error is ErrorCode.TIA_UNAVAILABLE or ErrorCode.WORKER_UNAVAILABLE or ErrorCode.CONNECTION_LOST;
    public static TimeSpan Delay(int attempt, IJitter jitter)
    {
        if (attempt is < 1 or > 2) throw new ArgumentOutOfRangeException(nameof(attempt));
        double sample = jitter.NextUnit(); if (sample is < 0 or > 1 || double.IsNaN(sample)) throw new ArgumentException("Invalid jitter.");
        return TimeSpan.FromSeconds((1 << (attempt - 1)) * (1 + .25 * sample));
    }
}
public sealed record WorkerObservation(DateTimeOffset HostHeartbeat, DateTimeOffset? WorkerHeartbeat, DateTimeOffset? LastProgressAt, DateTimeOffset? TiaProbeAt, string State, int? Pid, Guid? WorkerEpoch)
{
    public TimeSpan? ProgressAge(DateTimeOffset now) => LastProgressAt is { } at ? now - at : null;
}
public static class Watchdog
{
    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan SuspectAfter = TimeSpan.FromSeconds(6);
    public static readonly TimeSpan Lease = TimeSpan.FromSeconds(15);
    public static ErrorCode? Evaluate(DateTimeOffset now, DateTimeOffset deadline, DateTimeOffset lastHeartbeat, bool connected, bool exited)
        => exited || !connected ? ErrorCode.WORKER_UNAVAILABLE : now >= deadline ? ErrorCode.TIMEOUT : now - lastHeartbeat >= SuspectAfter ? ErrorCode.WORKER_UNAVAILABLE : null;
}
