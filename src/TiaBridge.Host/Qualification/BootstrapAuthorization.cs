using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using TiaBridge.Host.Api;
using TiaBridge.Host.Scheduling;
using TiaBridge.Ipc;

namespace TiaBridge.Host.Qualification;

public sealed record QualificationBootstrapConfiguration(string AuthorizationId, string OperatorSid, string HostSid);

/// <summary>Local operator claim, never a capability or an HTTP parameter.</summary>
internal sealed record BootstrapAuthorization(string SchemaVersion, string AuthorizationId, string OperatorSid,
    DateTimeOffset AuthorizedAtUtc, DateTimeOffset NotBeforeUtc, DateTimeOffset ExpiresAtUtc,
    string ProjectName, string ProjectKey, string ProjectPath, string ProjectRoot, int TiaPid, long TiaStartTicks,
    string ExecutablePath, string SessionSid, string TiaBuild, string WorkerBuild, string HostBuild,
    string OperationMethod, string OperationScope, string Profile, string[] ReadScope,
    string InventoryScope, bool Fresh, int TimeoutSeconds, int QueueTimeoutSeconds, int MaximumCycles,
    int MaximumDevices, int MaximumNodes, int MaximumDepth, string PlanId, string PlanSha256, string GitSha,
    string[] StopConditions, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Purpose = null)
{
    internal const string B1 = "T05_RecipeSim_V19_Testowy";
    internal const string HandleDiagnostics = "HANDLE_DIAGNOSTICS";
    [JsonIgnore] internal bool IsHandleDiagnostics => SchemaVersion == "1.1" && Purpose == HandleDiagnostics && PlanId == "E2-B1-D" && MaximumCycles == 3;
    [JsonIgnore]
    internal int CycleBudget => IsHandleDiagnostics ? 3
        : SchemaVersion == "1.0" && Purpose is null && PlanId == "E2-B1" && MaximumCycles == 20 ? 20
        : throw new IOException("BOOTSTRAP_EXPERIMENT_DENIED");
    [JsonIgnore] internal string PlanFile => IsHandleDiagnostics ? "E2-B1-D_TEST_PLAN.md" : "E2-B1_TEST_PLAN.md";
    internal static readonly string[] RequiredStops = ["IDENTITY_DRIFT", "BUILD_DRIFT", "DIRTY_PROJECT", "READ_FAILURE", "TIMEOUT", "WORKER_LOSS", "RESOURCE_REVIEW", "EVIDENCE_FAILURE", "RELEASE_UNCONFIRMED"];
    internal static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    internal static BootstrapAuthorization Read(byte[] bytes)
    {
        if (bytes.Length > 64 * 1024) throw new IOException("BOOTSTRAP_DOCUMENT_LIMIT");
        Protocol.CheckJson(bytes);
        using var document = JsonDocument.Parse(bytes);
        if (document.RootElement.GetProperty("schema_version").GetString() == "1.0" && document.RootElement.TryGetProperty("purpose", out _))
            throw new IOException("BOOTSTRAP_LEGACY_FIELDS_DENIED");
        return JsonSerializer.Deserialize<BootstrapAuthorization>(bytes, HostConfiguration.Json) ?? throw new IOException("BOOTSTRAP_DOCUMENT_REQUIRED");
    }
    internal void Validate(HostConfiguration config, DateTimeOffset now)
    {
        var policy = config.QualificationBootstrap ?? throw new IOException("BOOTSTRAP_DISABLED");
        var targets = config.V19?.Targets;
        _ = CycleBudget;
        if (!Guid.TryParseExact(AuthorizationId, "D", out var id) || id == Guid.Empty
            || AuthorizationId != policy.AuthorizationId || OperatorSid != policy.OperatorSid || SessionSid != policy.HostSid
            || OperatorSid != SessionSid || AuthorizedAtUtc > now || AuthorizedAtUtc > NotBeforeUtc || NotBeforeUtc > now || now >= ExpiresAtUtc
            || NotBeforeUtc >= ExpiresAtUtc || ProjectName != B1 || !Hex(PlanSha256, 64) || !Hex(GitSha, 40)
            || OperationMethod != "project.inspect" || OperationScope != "PROJECT" || Profile != V19DiscoveryProfile.Id
            || InventoryScope != "all_supported" || !Fresh || TimeoutSeconds != 120 || QueueTimeoutSeconds != 300
            || MaximumDevices != V19DiscoveryProfile.MaxDevices || MaximumNodes != V19DiscoveryProfile.MaxNodes || MaximumDepth != V19DiscoveryProfile.MaxDepth
            || ReadScope is null || ReadScope.Length != V19InspectionProfile.DiscoveryReadScope.Count
            || !ReadScope.ToHashSet(StringComparer.Ordinal).SetEquals(V19InspectionProfile.DiscoveryReadScope)
            || StopConditions is null || StopConditions.Length != RequiredStops.Length || !StopConditions.ToHashSet(StringComparer.Ordinal).SetEquals(RequiredStops)
            || targets is not { Length: 1 } || config.RegisteredProjects is not { Length: 1 } || config.RegisteredProjects[0] != ProjectKey
            || targets[0] != new V19Target(ProjectKey, TiaPid, TiaStartTicks, ProjectPath) || !Guid.TryParseExact(ProjectKey, "D", out var project) || project == Guid.Empty
            || !string.Equals(ProjectRoot, config.V19!.ProjectRoot, StringComparison.OrdinalIgnoreCase)
            || !Path.IsPathFullyQualified(ProjectPath) || !Path.IsPathFullyQualified(ProjectRoot) || !Path.IsPathFullyQualified(ExecutablePath)
            || !Path.GetFullPath(ProjectPath).StartsWith(Path.GetFullPath(ProjectRoot).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Path.GetFileNameWithoutExtension(ProjectPath), B1, StringComparison.Ordinal)
            || config.V19!.Apartment != "STA" || config.MaxInFlight != 1 || config.EnableSyntheticMutations || config.FakeWorkerDll is not null
            || !Build(WorkerBuild) || !Build(HostBuild) || string.IsNullOrWhiteSpace(TiaBuild) || TiaPid <= 0 || TiaStartTicks <= 0)
            throw new IOException("BOOTSTRAP_SCOPE_OR_WINDOW_DENIED");
    }
    private static bool Hex(string? value, int length) => value is not null && value.Length == length && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static bool Build(string? value) => value is not null && value.StartsWith("1.0+", StringComparison.Ordinal) && Hex(value[4..], 64);
}

internal sealed record BootstrapPermit(string AuthorizationId, string AuthorizationSha256);
