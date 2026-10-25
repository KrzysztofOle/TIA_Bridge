using System.Text.Json;
using System.Text.Json.Serialization;
using TiaBridge.Core.Identity;
using TiaBridge.Core.Operations;
using TiaBridge.Core.Transport.V1;
using TiaBridge.Host.Operations;
using TiaBridge.Host.Scheduling;
using TiaBridge.Host.Storage;
using TiaBridge.Ipc;

namespace TiaBridge.Host.Api;

public sealed record FakeApiProfile(string Method, FakeScenario Scenario, int DelayMs, bool QualifiedEffectFreeRead, JsonElement SuccessReport);
public sealed record HostConfiguration(string SchemaVersion, string StatePath, string RepositoryRoot, string[] RegisteredProjects,
    string? FakeWorkerDll = null, FakeApiProfile[]? FakeProfiles = null, bool EnableSyntheticMutations = false,
    int RequestsPerSecond = 2048, int SubmitsPerSecond = 256, int MaxInFlight = 64)
{
    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        MaxDepth = 32,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };
    public static HostConfiguration Read(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length > 256 * 1024) throw new ArgumentException("Host configuration exceeds limit.");
        byte[] bytes = new byte[checked((int)file.Length)]; file.ReadExactly(bytes); Protocol.CheckJson(bytes);
        var config = JsonSerializer.Deserialize<HostConfiguration>(bytes, Json) ?? throw new ArgumentException("Invalid Host configuration.");
        if (config.SchemaVersion != "1.0" || config.RegisteredProjects is null || config.RegisteredProjects.Length > 1000 || config.RequestsPerSecond is < 1 or > 2048 || config.SubmitsPerSecond is < 1 or > 256 || config.MaxInFlight is < 1 or > 64) throw new ArgumentException("Invalid Host configuration.");
        config = config with { StatePath = Environment.ExpandEnvironmentVariables(config.StatePath), RepositoryRoot = Path.GetFullPath(config.RepositoryRoot), FakeWorkerDll = config.FakeWorkerDll is null ? null : Path.GetFullPath(config.FakeWorkerDll) };
        _ = StorageLocation.Validate(config.StatePath, config.RepositoryRoot);
        foreach (string project in config.RegisteredProjects) _ = new ProjectKey(UuidIdentity.ParseValue(project));
        var methods = new HashSet<OperationMethod>();
        foreach (var fake in config.FakeProfiles ?? [])
        {
            if (fake is null) throw new ArgumentException("Invalid synthetic profile.");
            var method = ParseMethod(fake.Method);
            if (method is not (OperationMethod.ProjectInspect or OperationMethod.SyncApply) || !methods.Add(method) || fake.DelayMs is < 0 or > 600000 || !Enum.IsDefined(fake.Scenario)
                || RetryPolicy.IsMutation(method) && !config.EnableSyntheticMutations || config.FakeWorkerDll is null) throw new ArgumentException("Unsupported or unauthorized synthetic profile.");
            var report = ContractJson.ReadReport(System.Text.Encoding.UTF8.GetBytes(fake.SuccessReport.GetRawText()));
            if (report.Method != method || report.State != OperationState.SUCCEEDED || report.ArtifactRefs.Length != 0 || System.Text.Encoding.UTF8.GetByteCount(fake.SuccessReport.GetRawText()) > Protocol.MaxReceiptBytes) throw new ArgumentException("Invalid synthetic success report.");
        }
        if (config.FakeWorkerDll is not null && (Path.GetFileName(config.FakeWorkerDll) != "TiaBridge.FakeWorker.dll" || !File.Exists(config.FakeWorkerDll))) throw new ArgumentException("Built Fake Worker required.");
        return config;
    }
    public static OperationMethod ParseMethod(string name) => Enum.GetValues<OperationMethod>().SingleOrDefault(m => OperationCatalog.Name(m) == name, (OperationMethod)(-1)) is var method && Enum.IsDefined(method) ? method : throw new ArgumentException("Unknown method.");
}

internal sealed class ConfiguredFakeProfile(HostConfiguration config, SqliteOperationStore store) : IExecutionProfile
{
    private readonly Dictionary<OperationMethod, FakeApiProfile> profiles = (config.FakeProfiles ?? []).ToDictionary(p => HostConfiguration.ParseMethod(p.Method));
    private readonly Dictionary<Guid, ExecutionSpec> prepared = new();
    internal bool Supports(OperationMethod method) => profiles.ContainsKey(method);
    public ExecutionSpec? Resolve(StoredOperation operation)
    {
        if (!profiles.TryGetValue(operation.Report.Method, out var profile)) return null;
        if (prepared.TryGetValue(operation.Report.OperationId.Value, out var found)) return found;
        // Scheduler thread only: HTTP admission never hashes or captures an artifact.
        var artifact = store.Artifacts.Put(operation.Report.OperationId, new MemoryStream(System.Text.Encoding.UTF8.GetBytes(profile.SuccessReport.GetRawText())));
        var spec = new ExecutionSpec("synthetic.local", "fake.api.1." + profile.Method, profile.QualifiedEffectFreeRead, artifact, profile.Scenario, profile.DelayMs, ErrorCode.WORKER_UNAVAILABLE);
        if (prepared.Count >= 1000) prepared.Clear();
        prepared[operation.Report.OperationId.Value] = spec; return spec;
    }
}
