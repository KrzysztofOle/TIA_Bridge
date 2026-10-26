using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using TiaBridge.Core.Identity;
using TiaBridge.Core.Operations;
using TiaBridge.Core.Transport.V1;
using TiaBridge.Core.Workspace;
using TiaBridge.Host.Api;
using TiaBridge.Host.Operations;
using TiaBridge.Host.Storage;
using TiaBridge.Ipc;

namespace TiaBridge.Host.Scheduling;

/// <summary>Operator-bound experimental read-only inspect; never a grant to mutate Siemens.</summary>
internal sealed class V19InspectionProfile(HostConfiguration configuration) : IExecutionProfile
{
    internal static void Validate(HostConfiguration c)
    {
        if (c.V19 is null && c.V19WorkerExe is null) return;
        if (c.V19 is null || c.V19WorkerExe is null || c.FakeWorkerDll is not null || c.FakeProfiles is { Length: > 0 } || c.EnableSyntheticMutations
            || Path.GetFileName(c.V19WorkerExe) != "TiaBridge.Worker.V19.exe" || !File.Exists(c.V19WorkerExe)
            || c.V19.Targets is null || c.V19.Targets.Length is < 1 or > 2 || c.V19.Apartment is not ("STA" or "MTA")) throw new ArgumentException("Invalid V19 read-only profile.");
        _ = StorageLocation.Validate(Path.GetDirectoryName(Path.GetFullPath(c.V19WorkerExe))!, c.RepositoryRoot);
        StorageLocation.CheckChain(c.V19WorkerExe);
        string root = WorkspacePath.ValidateRoot(c.V19.ProjectRoot).TrimEnd('\\') + "\\";
        if (!c.RegisteredProjects.Order(StringComparer.Ordinal).SequenceEqual(c.V19.Targets.Select(t => t.ProjectKey).Order(StringComparer.Ordinal))
            || c.V19.Targets.Select(t => t.Pid).Distinct().Count() != c.V19.Targets.Length) throw new ArgumentException("Exact registered V19 bindings required.");
        foreach (var target in c.V19.Targets)
        {
            _ = new ProjectKey(UuidIdentity.ParseValue(target.ProjectKey));
            string path = WorkspacePath.ValidateRoot(target.ProjectPath).TrimEnd('\\');
            if (target.Pid <= 0 || target.StartTicks <= 0 || !path.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !path.EndsWith(".ap19", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Unsafe V19 identity.");
            for (string? item = path; item is not null; item = Path.GetDirectoryName(item))
                if ((File.GetAttributes(item) & FileAttributes.ReparsePoint) != 0) throw new ArgumentException("Reparse point in V19 binding.");
        }
    }
    public ExecutionSpec? Resolve(StoredOperation operation)
    {
        if (operation.Report.Method != OperationMethod.ProjectInspect) return null;
        var request = ContractJson.ReadRequest(operation.RequestJson);
        // X-01 qualifies live bounded all_supported only. Filtered inventory and
        // cached reads must fail closed instead of silently changing semantics.
        if (request.Parameters is not ProjectInspectParameters { InventoryScope: ProjectInspectParameters_InventoryScope.all_supported, Fresh: not false }) return null;
        var target = configuration.V19!.Targets.SingleOrDefault(t => Guid.Parse(t.ProjectKey) == request.Identity.Project?.Value);
        // Automatic retry is deliberately unqualified in X-01, even though this executor contains reads only.
        return target is null ? null : new ExecutionSpec("v19:" + target.Pid + ":" + target.StartTicks, "v19.x01.inspect", false, null, V19Target: target);
    }
}

internal static class V19InspectionProjection
{
    internal static OperationReport Create(SqliteOperationStore store, OperationReport previous, V19Target target, string reportJson)
    {
        Protocol.CheckJson(Encoding.UTF8.GetBytes(reportJson));
        using var doc = JsonDocument.Parse(reportJson);
        var observed = Protocol.Read<V19Observation>(doc.RootElement);
        if (previous.Method != OperationMethod.ProjectInspect || observed.ProjectKey != target.ProjectKey || observed.TiaPid != target.Pid || observed.TiaStartTicks != target.StartTicks
            || !string.Equals(observed.ProjectPath, target.ProjectPath, StringComparison.OrdinalIgnoreCase) || observed.SessionEpoch == Guid.Empty || !observed.PortalAliveAfterDispose
            || observed.Devices is null || observed.Nodes is null || observed.Devices.Length > 64 || observed.Nodes.Length > 256 || observed.Apartment is not ("STA" or "MTA")
            || observed.Devices.Any(d => d is null || !Text(d.Name, 128) || d.Family is not ("PLC" or "HMI" or "UNKNOWN"))
            || observed.Nodes.Any(n => n is null || !Text(n.Locator, 4096) || !Text(n.SoftwareType, 512))
            || !Text(observed.ProjectName, 512) || !Text(observed.ApiBuild, 128) || observed.OwnerThreadId <= 0
            || observed.CallbackThreads is null || observed.CallbackThreads.Length > 64 || observed.CallbackThreads.Any(t => t <= 0)
            || new[] { observed.EnumerationMs, observed.AttachMs, observed.ReadMs, observed.DisposeMs }.Any(t => !double.IsFinite(t) || t < 0)) throw new ProtocolException();
        var proof = store.Artifacts.Put(previous.OperationId, new MemoryStream(Encoding.UTF8.GetBytes(reportJson)));
        var coverage = new Coverage([
            new(CoverageDimension.CONTENT, CoverageState.UNSUPPORTED, "No source export in X-01"),
            new(CoverageDimension.CONFIG, CoverageState.PARTIAL, "Basic project and hardware metadata only"),
            new(CoverageDimension.API, CoverageState.PARTIAL, "Public metadata only; native HMI API not qualified"),
            new(CoverageDimension.STRUCTURE, CoverageState.PARTIAL, "Bounded DeviceItems and software containers"),
            new(CoverageDimension.DEPENDENCIES, CoverageState.UNSUPPORTED, "Dependency analysis is outside X-01"),
            new(CoverageDimension.INVENTORY, CoverageState.PARTIAL, "No persistent object identity or full object inventory")]);
        var coverageProof = store.Artifacts.Put(previous.OperationId, new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(coverage, Protocol.Json)));
        var result = new ProjectInspectResult(new ProjectKey(UuidIdentity.ParseValue(target.ProjectKey)), new SessionEpoch(observed.SessionEpoch), observed.ApiBuild,
            observed.Devices.Select((d, i) => new DeviceSummary("session-" + observed.SessionEpoch + "-device-" + i, d.Family)).ToImmutableArray(), [],
            [coverageProof.ArtifactId]);
        var report = previous with
        {
            State = OperationState.SUCCEEDED,
            Outcome = OperationOutcome.CONFIRMED,
            Phase = "COMPLETED",
            Result = result,
            ArtifactRefs = [new ArtifactRef(proof.ArtifactId, proof.Sha256), new ArtifactRef(coverageProof.ArtifactId, coverageProof.Sha256)],
            SessionEpoch = new SessionEpoch(observed.SessionEpoch),
            CompletedAt = DateTimeOffset.UtcNow,
            PollAfterMs = 0
        };
        _ = ContractJson.WriteReport(report); return report;
    }
    private static bool Text(string? value, int maximum) => value is not null && value.EnumerateRunes().Count() is var count && count > 0 && count <= maximum;
}
