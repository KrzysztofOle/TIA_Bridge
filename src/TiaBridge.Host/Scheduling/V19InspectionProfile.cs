using System.Collections.Immutable;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using TiaBridge.Core.Capabilities;
using TiaBridge.Core.Discovery;
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
internal sealed class V19InspectionProfile : IExecutionProfile
{
    private const string AdapterId = "tia-bridge.worker.v19";
    private readonly HostConfiguration configuration;
    private readonly CapabilityRegistry registry;
    private readonly IReadOnlySet<string>? verifiedQualifications;
    private readonly Func<string?> tiaBuild;
    private readonly Func<string?> workerBuild;

    internal V19InspectionProfile(HostConfiguration configuration)
        : this(configuration, LoadRegistry(configuration.StatePath), null, GetInstalledTiaBuild,
        () => GetWorkerBuild(configuration.V19WorkerExe))
    { }

    internal V19InspectionProfile(HostConfiguration configuration, CapabilityRegistry registry,
        IReadOnlySet<string>? verifiedQualifications, Func<string?> tiaBuild, Func<string?> workerBuild)
    {
        this.configuration = configuration;
        this.registry = registry;
        this.verifiedQualifications = verifiedQualifications;
        this.tiaBuild = tiaBuild;
        this.workerBuild = workerBuild;
    }

    internal static IReadOnlyList<string> DiscoveryReadScope =>
    [
        "TiaPortal.GetProcesses", "TiaPortalProcess.Attach", "TiaPortal.Projects", "Project.Path", "Project.Name",
        "Project.IsModified", "Project.Devices", "Device.Name", "Device.DeviceItems", "DeviceItem.Name",
        "DeviceItem.DeviceItems", "DeviceItem.GetService<SoftwareContainer>", "SoftwareContainer.Software"
    ];

    private static CapabilityRegistry LoadRegistry(string statePath)
    {
        string path = Path.Combine(Path.GetFullPath(statePath), "capability-registry.v2.json");
        if (!File.Exists(path)) return CapabilityRegistry.DenyAllV2;
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new ArgumentException("Capability registry cannot be a reparse point.");
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length > 256 * 1024) throw new ArgumentException("Capability registry exceeds limit.");
        byte[] bytes = new byte[checked((int)file.Length)]; file.ReadExactly(bytes);
        return new CapabilityRegistry(CapabilityJson.ReadV2(bytes));
    }

    private static string? GetInstalledTiaBuild()
    {
        if (!OperatingSystem.IsWindows()) return null;
        string api = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Siemens", "Automation", "Portal V19", "PublicAPI", "V19", "Siemens.Engineering.dll");
        if (!File.Exists(api)) return null;
        for (string? item = api; item is not null; item = Path.GetDirectoryName(item))
            if ((File.GetAttributes(item) & FileAttributes.ReparsePoint) != 0) return null;
        return FileVersionInfo.GetVersionInfo(api).FileVersion;
    }

    private static string? GetWorkerBuild(string? path)
    {
        if (path is null || !File.Exists(path)) return null;
        using var image = File.OpenRead(path);
        return "1.0+" + Convert.ToHexStringLower(SHA256.HashData(image));
    }

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
        if (target is null) return null;
        string tia = tiaBuild() ?? "unknown";
        string worker = workerBuild() ?? "unknown";
        var key = new CapabilityKeyV2(OperationScope.PROJECT, tia, AdapterId, worker, null, "project", V19DiscoveryProfile.Id, null, OperationMethod.ProjectInspect);
        var query = new CapabilityQueryV2(key, registry.RegistryVersion, "1.0", V19DiscoveryProfile.Id,
            DiscoveryReadScope, [], [CoverageDimension.STRUCTURE]);
        if (!registry.Assess(query, verifiedQualifications).CapabilityConfirmed) return null;
        var binding = new CapabilityBinding(OperationScope.PROJECT, registry.RegistryVersion, registry.CapabilitySetHash, V19DiscoveryProfile.Id);
        return new ExecutionSpec("v19:" + target.Pid + ":" + target.StartTicks, "v19.project.discovery.v1", false, null,
            V19Target: target, CapabilityBinding: binding);
    }
}

internal static class V19InspectionProjection
{
    internal static OperationReport Create(SqliteOperationStore store, OperationReport previous, V19Target target,
        string reportJson, string workerBuild, CapabilityBinding capability, DiscoveryResourceSample resourcesBefore, DiscoveryResourceSample resourcesAfter)
    {
        Protocol.CheckJson(Encoding.UTF8.GetBytes(reportJson));
        using var doc = JsonDocument.Parse(reportJson);
        var observed = Protocol.Read<V19Observation>(doc.RootElement);
        if (previous.Method != OperationMethod.ProjectInspect || observed.ProjectKey != target.ProjectKey || observed.TiaPid != target.Pid || observed.TiaStartTicks != target.StartTicks
            || !string.Equals(observed.ProjectPath, target.ProjectPath, StringComparison.OrdinalIgnoreCase) || observed.SessionEpoch == Guid.Empty || !observed.PortalAliveAfterDispose
            || observed.Devices is null || observed.Nodes is null || observed.Devices.Length > V19DiscoveryProfile.MaxDevices || observed.Nodes.Length > V19DiscoveryProfile.MaxNodes || observed.Apartment is not ("STA" or "MTA")
            || observed.Devices.Any(d => d is null || !Text(d.Name, 32768) || d.Family is not ("PLC" or "HMI" or "UNKNOWN"))
            || observed.Nodes.Any(n => n is null || !Text(n.Locator, 4096) || !Text(n.SoftwareType, 512))
            || !Text(observed.ProjectName, 512) || !Text(observed.ApiBuild, 128) || observed.IsModifiedBefore != observed.IsModifiedAfter || observed.OwnerThreadId <= 0
            || workerBuild is null || !Regex.IsMatch(workerBuild, "\\A1\\.0\\+[0-9a-f]{64}\\z", RegexOptions.CultureInvariant)
            || capability is null || capability.Scope != OperationScope.PROJECT || capability.Profile != V19DiscoveryProfile.Id
            || string.IsNullOrWhiteSpace(capability.RegistryVersion) || string.IsNullOrEmpty(capability.CapabilitySetHash)
            || !Regex.IsMatch(capability.CapabilitySetHash, "\\A[0-9a-f]{64}\\z", RegexOptions.CultureInvariant)
            || resourcesBefore is null || resourcesAfter is null
            || new[] { resourcesBefore, resourcesAfter }.Any(r => r.Handles < 0 || r.PrivateBytes < 0 || r.WorkingSetBytes < 0)
            || observed.CallbackThreads is null || observed.CallbackThreads.Length > 64 || observed.CallbackThreads.Any(t => t <= 0)
            || new[] { observed.EnumerationMs, observed.AttachMs, observed.ReadMs, observed.DisposeMs }.Any(t => !double.IsFinite(t) || t < 0)) throw new ProtocolException();

        var nodes = DecodeNodes(observed);
        ValidateRelations(observed, nodes);
        var devices = ProjectDevices(observed, nodes);
        var rawProof = store.Artifacts.Put(previous.OperationId, new MemoryStream(Encoding.UTF8.GetBytes(reportJson)));
        var coverage = new Coverage([
            new(CoverageDimension.CONTENT, CoverageState.UNSUPPORTED, "B-03A does not read source content"),
            new(CoverageDimension.CONFIG, CoverageState.PARTIAL, "Project and public device metadata only"),
            new(CoverageDimension.API, CoverageState.PARTIAL, "A null SoftwareContainer service cannot distinguish absence from unavailable metadata"),
            new(CoverageDimension.STRUCTURE, CoverageState.COMPLETE, "Complete only within the bounded Device/DeviceItem DiscoveryV1 profile"),
            new(CoverageDimension.DEPENDENCIES, CoverageState.UNSUPPORTED, "Dependency discovery is outside B-03A"),
            new(CoverageDimension.INVENTORY, CoverageState.PARTIAL, "No qualified persistent Siemens IDs or ObjectIds")]);
        var snapshot = new DiscoverySnapshot("1.0", V19DiscoveryProfile.Id,
            new ProjectKey(UuidIdentity.ParseValue(target.ProjectKey)), new SessionEpoch(observed.SessionEpoch), observed.TiaPid,
            observed.TiaStartTicks, observed.ProjectPath, observed.ProjectName, "EXACT_REGISTERED_BINDING", null, null,
            observed.ApiBuild, workerBuild, capability, resourcesBefore, resourcesAfter, observed.IsModifiedBefore, observed.IsModifiedAfter,
            devices, nodes, coverage);
        var discoveryProof = store.Artifacts.Put(previous.OperationId, new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(snapshot, Protocol.Json)));
        var result = new ProjectInspectResult(new ProjectKey(UuidIdentity.ParseValue(target.ProjectKey)), new SessionEpoch(observed.SessionEpoch), observed.ApiBuild,
            observed.Devices.Select((d, i) => new DeviceSummary("session-" + observed.SessionEpoch + "-device-observation-" + i, d.Family)).ToImmutableArray(), [],
            [discoveryProof.ArtifactId]);
        var report = previous with
        {
            State = OperationState.SUCCEEDED,
            Outcome = OperationOutcome.CONFIRMED,
            Phase = "COMPLETED",
            Result = result,
            ArtifactRefs = [new ArtifactRef(rawProof.ArtifactId, rawProof.Sha256), new ArtifactRef(discoveryProof.ArtifactId, discoveryProof.Sha256)],
            SessionEpoch = new SessionEpoch(observed.SessionEpoch),
            CompletedAt = DateTimeOffset.UtcNow,
            PollAfterMs = 0
        };
        _ = ContractJson.WriteReport(report); return report;
    }

    private static DiscoveryNode[] DecodeNodes(V19Observation observed)
    {
        var result = new DiscoveryNode[observed.Nodes.Length];
        for (int i = 0; i < observed.Nodes.Length; i++)
        {
            var wire = observed.Nodes[i];
            if (!V19NodeLocatorCodec.TryDecode(wire.Locator, out var locator)
                || locator.ObservationIndex < 0 || locator.DeviceObservationIndex < 0 || locator.ParentObservationIndex is < 0
                || !Text(locator.Name, 32768) || !Text(locator.ObjectKind, 512)
                || locator.SoftwareState is not ("READ" or "UNKNOWN" or "NOT_APPLICABLE")
                || locator.HmiKind is not ("UNIFIED" or "CLASSIC" or "UNKNOWN") || locator.IdentityState != "UNAVAILABLE"
                || locator.PublicId is not null || locator.PublicIdValidityScope is not null
                || wire.SoftwareType == "unknown" && locator.SoftwareState == "READ"
                || wire.SoftwareType != "unknown" && locator.SoftwareState != "READ") throw new ProtocolException();
            result[i] = new DiscoveryNode(locator.ObservationIndex, locator.ParentObservationIndex, locator.DeviceObservationIndex,
                locator.Name, locator.ObjectKind, locator.SoftwareState, wire.SoftwareType == "unknown" ? null : wire.SoftwareType,
                locator.HmiKind, locator.IdentityState, locator.PublicId, locator.PublicIdValidityScope);
        }
        if (result.Select(n => n.ObservationIndex).Distinct().Count() != result.Length
            || result.Any(n => n.DeviceObservationIndex >= observed.Devices.Length
                || n.ParentObservationIndex is int parent && !result.Any(p => p.ObservationIndex == parent && p.DeviceObservationIndex == n.DeviceObservationIndex))) throw new ProtocolException();
        return result;
    }

    private static DiscoveryDevice[] ProjectDevices(V19Observation observed, DiscoveryNode[] nodes)
    {
        var result = new DiscoveryDevice[observed.Devices.Length];
        for (int i = 0; i < observed.Devices.Length; i++)
        {
            var roots = nodes.Where(n => n.DeviceObservationIndex == i && n.ParentObservationIndex is null).ToArray();
            if (roots.Length != 1 || roots[0].OriginalName != observed.Devices[i].Name) throw new ProtocolException();
            var types = nodes.Where(n => n.DeviceObservationIndex == i && n.SoftwareType is not null).Select(n => n.SoftwareType).ToArray();
            bool plc = types.Contains("Siemens.Engineering.SW.PlcSoftware", StringComparer.Ordinal);
            bool unified = types.Contains("Siemens.Engineering.HmiUnified.HmiSoftware", StringComparer.Ordinal);
            bool classic = types.Contains("Siemens.Engineering.Hmi.HmiSoftware", StringComparer.Ordinal);
            string family = plc && !unified && !classic ? "PLC" : !plc && unified && !classic ? "HMI_UNIFIED" : !plc && !unified && classic ? "HMI_CLASSIC" : "UNKNOWN";
            string expected = family == "PLC" ? "PLC" : family.StartsWith("HMI_", StringComparison.Ordinal) ? "HMI" : "UNKNOWN";
            if (observed.Devices[i].Family != expected) throw new ProtocolException();
            string hmiKind = family == "HMI_UNIFIED" ? "UNIFIED" : family == "HMI_CLASSIC" ? "CLASSIC" : "UNKNOWN";
            result[i] = new DiscoveryDevice(i, observed.Devices[i].Name, family, hmiKind, "UNAVAILABLE", null, null);
        }
        return result;
    }

    private static void ValidateRelations(V19Observation observed, DiscoveryNode[] discoveryNodes)
    {
        var nodes = discoveryNodes.ToDictionary(n => n.ObservationIndex);
        for (int i = 0; i < observed.Devices.Length; i++)
        {
            var roots = nodes.Values.Where(n => n.DeviceObservationIndex == i && n.ParentObservationIndex is null).ToArray();
            if (roots.Length != 1 || roots[0].OriginalName != observed.Devices[i].Name) throw new ProtocolException();
        }
        foreach (var node in discoveryNodes)
        {
            var visited = new HashSet<int>(); var current = node; int depth = 0;
            while (current.ParentObservationIndex is int parent)
            {
                if (!visited.Add(current.ObservationIndex) || ++depth > V19DiscoveryProfile.MaxDepth + 1 || !nodes.TryGetValue(parent, out var parentNode)) throw new ProtocolException();
                current = parentNode;
            }
            if (current.DeviceObservationIndex != node.DeviceObservationIndex) throw new ProtocolException();
        }
    }

    private static bool Text(string? value, int maximum) => value is not null && value.EnumerateRunes().Count() is var count && count > 0 && count <= maximum;
}
