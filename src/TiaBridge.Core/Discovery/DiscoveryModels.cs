using TiaBridge.Core.Identity;
using TiaBridge.Core.Workspace;

namespace TiaBridge.Core.Discovery;

/// <summary>Observation-local structural inventory. Ordinals are relation keys, never durable identities.</summary>
public sealed record DiscoveryDevice(int ObservationIndex, string OriginalName, string Family, string HmiKind,
    string IdentityState, string? PublicId, string? PublicIdValidityScope);

/// <summary>Publicly observed node. ObjectId is intentionally absent when Siemens exposes no qualified stable ID.</summary>
public sealed record DiscoveryNode(int ObservationIndex, int? ParentObservationIndex, int DeviceObservationIndex,
    string OriginalName, string ObjectKind, string SoftwareState, string? SoftwareType, string HmiKind,
    string IdentityState, string? PublicId, string? PublicIdValidityScope);

public sealed record DiscoveryResourceSample(int Handles, long PrivateBytes, long WorkingSetBytes);

public sealed record DiscoverySnapshot(string SchemaVersion, string ProfileId, ProjectKey ProjectKey,
    SessionEpoch SessionEpoch, int ProcessId, long ProcessStartTicks, string ProjectPath, string ProjectName,
    string ProjectIdentityState, string? PublicProjectId, string? PublicProjectIdValidityScope,
    string TiaBuild, string WorkerBuild, DiscoveryResourceSample WorkerResourcesBefore, DiscoveryResourceSample WorkerResourcesAfter,
    bool IsModifiedBefore, bool IsModifiedAfter, IReadOnlyList<DiscoveryDevice> Devices, IReadOnlyList<DiscoveryNode> Nodes, Coverage Coverage);
