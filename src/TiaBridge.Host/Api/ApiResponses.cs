using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using TiaBridge.Core.Identity;
using TiaBridge.Core.Operations;
using TiaBridge.Core.Transport.V1;
using TiaBridge.Host.Operations;
using TiaBridge.Host.Scheduling;

namespace TiaBridge.Host.Api;

internal static class ApiResponses
{
    internal static IResult Bytes(byte[] bytes, int status = 200) => new JsonBytes(bytes, status);
    private sealed record JsonBytes(byte[] Bytes, int Status) : IResult
    {
        public async Task ExecuteAsync(HttpContext context)
        {
            context.Response.StatusCode = Status; context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.ContentLength = Bytes.Length; await context.Response.Body.WriteAsync(Bytes, context.RequestAborted);
        }
    }
    internal static IResult Error(ErrorCode code, int status) => Bytes(ContractJson.WriteError(new(code, Category(code), "Request could not be completed.", false, OperationOutcome.NOT_STARTED)), status);
    private static ErrorCategory Category(ErrorCode code) => code switch
    {
        ErrorCode.UNAUTHENTICATED or ErrorCode.ACCESS_DENIED => ErrorCategory.AUTHORIZATION,
        ErrorCode.IDENTITY_MISMATCH => ErrorCategory.IDENTITY,
        ErrorCode.CONFLICT or ErrorCode.IDEMPOTENCY_MISMATCH or ErrorCode.PROJECT_QUARANTINED => ErrorCategory.CONFLICT,
        ErrorCode.STORE_UNAVAILABLE or ErrorCode.STORAGE_FULL or ErrorCode.VERIFY_FAILED => ErrorCategory.STORAGE,
        ErrorCode.UNSUPPORTED => ErrorCategory.CAPABILITY,
        _ => ErrorCategory.VALIDATION
    };
    internal static int Status(ErrorCode code) => code switch
    {
        ErrorCode.UNAUTHENTICATED => 401,
        ErrorCode.ACCESS_DENIED => 403,
        ErrorCode.NOT_FOUND => 404,
        ErrorCode.CONFLICT or ErrorCode.IDEMPOTENCY_MISMATCH or ErrorCode.PROJECT_QUARANTINED or ErrorCode.IDENTITY_MISMATCH => 409,
        ErrorCode.LIMIT_EXCEEDED => 413,
        ErrorCode.UNSUPPORTED or ErrorCode.VALIDATION_FAILED or ErrorCode.VERIFY_FAILED => 422,
        ErrorCode.QUEUE_FULL => 429,
        ErrorCode.STORE_UNAVAILABLE or ErrorCode.WORKER_UNAVAILABLE or ErrorCode.STORAGE_FULL or ErrorCode.NOT_OWNER => 503,
        _ => 400
    };
    internal static OperationReport QueryReport(OperationMethod method, OperationResult result, OperationId? id = null) => new("1.0", id ?? new(Guid.NewGuid()), method,
        OperationState.SUCCEEDED, "METADATA_READ", OperationOutcome.CONFIRMED, false, 0, ImmutableArray<ArtifactRef>.Empty, Result: result);
    internal static byte[] StatusReport(OperationMetadata metadata, WorkerObservation observation)
    {
        var root = JsonNode.Parse(metadata.ReportJson)!.AsObject();
        if (observation.WorkerHeartbeat is { } heartbeat && root["session_epoch"]?.GetValue<string>() == observation.WorkerEpoch?.ToString())
            root["worker_heartbeat_age_ms"] = Math.Max(0, (long)(DateTimeOffset.UtcNow - heartbeat).TotalMilliseconds);
        if (root["started_at"] is not null && root["completed_at"] is null)
            root["elapsed_ms"] = Math.Max(0, (long)(DateTimeOffset.UtcNow - DateTimeOffset.Parse(root["started_at"]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture)).TotalMilliseconds);
        return JsonSerializer.SerializeToUtf8Bytes(root);
    }
    internal static DiagnosticsHealthResult Health(SqliteOperationStore store, OperationScheduler scheduler, ProjectKey? project)
    {
        var now = DateTimeOffset.UtcNow; var o = scheduler.Observation;
        long? Age(DateTimeOffset? at) => at is null ? null : Math.Max(0, (long)(now - at.Value).TotalMilliseconds);
        return new([
            new("host", ComponentHealth_Status.HEALTHY, Age(o.HostHeartbeat)),
            new("store", ComponentHealth_Status.HEALTHY, 0),
            new("queue", store.ActiveCount >= SqliteOperationStore.QueueCapacity ? ComponentHealth_Status.DEGRADED : ComponentHealth_Status.HEALTHY, 0),
            new("scheduler", scheduler.LastError is null ? ComponentHealth_Status.HEALTHY : ComponentHealth_Status.DEGRADED, Age(o.HostHeartbeat)),
            new("worker", o.WorkerHeartbeat is null ? ComponentHealth_Status.UNKNOWN : Age(o.WorkerHeartbeat) >= 6000 ? ComponentHealth_Status.DEGRADED : ComponentHealth_Status.HEALTHY, Age(o.WorkerHeartbeat)),
            new("worker_progress", ComponentHealth_Status.UNKNOWN, Age(o.LastProgressAt)),
            new("tia_probe", ComponentHealth_Status.UNKNOWN, null),
            new("quarantine", store.IsQuarantined(project) ? ComponentHealth_Status.DEGRADED : ComponentHealth_Status.HEALTHY, null)
        ], now);
    }
}
