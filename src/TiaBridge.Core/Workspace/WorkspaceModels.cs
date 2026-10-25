using System.Text.Json;
using TiaBridge.Core.Identity;

namespace TiaBridge.Core.Workspace;

public enum DeviceFamily { PLC, HMI_UNIFIED, HMI_CLASSIC }
public enum InventoryStatus { REPRESENTED, UNSUPPORTED, ERROR, UNKNOWN }
public enum RepresentationRole { SOURCE, NATIVE, METADATA }
public enum CoverageState { COMPLETE, PARTIAL, UNSUPPORTED, UNKNOWN }
public enum CoverageDimension { CONTENT, CONFIG, API, STRUCTURE, DEPENDENCIES, INVENTORY }

/// <summary>Evidence about a covered projection; never a synchronization receipt.</summary>
public sealed record CoverageItem(CoverageDimension Dimension, CoverageState State, string? Reason = null);
public sealed record Coverage(IReadOnlyList<CoverageItem> Dimensions)
{
    public IReadOnlyList<CoverageDimension> Missing(IEnumerable<CoverageDimension> required) => required
        .Distinct().Where(d => !Dimensions.Any(x => x.Dimension == d && x.State == CoverageState.COMPLETE))
        .OrderBy(d => d).ToArray();
}

public sealed record WorkspaceDevice(ObjectId DeviceId, string OriginalName, DeviceFamily Family);
public sealed record WorkspaceGroup(ObjectId GroupId, ObjectId DeviceId, ObjectId? ParentGroupId, string OriginalName);
public sealed record VendorLocator(ObjectId DeviceId, IReadOnlyList<ObjectId> Groups, string Kind,
    string OriginalName, string? PublicId, string? PublicIdValidityScope, IReadOnlyList<string> IdentityEvidence);
public sealed record ObjectRelation(ObjectId TargetObjectId, string Relation);
public sealed record RepresentationRevision(string SourceRevision, string NativeRevision);
public sealed record FileReference(string RelativePath, RepresentationRole Role, string Format, string RawSha256);
public sealed record ExchangeUnit(string UnitId, IReadOnlyList<ObjectId> Members,
    IReadOnlyList<FileReference> Files, string UnitSha256, IReadOnlyDictionary<string, JsonElement> VendorFields);
public sealed record WorkspaceObject(ObjectId ObjectId, string Kind, string? Language, VendorLocator VendorLocator,
    InventoryStatus Status, string? Reason, string? ExchangeUnitId, IReadOnlyList<string> FilePaths,
    IReadOnlyList<ObjectRelation> Dependencies, Coverage Coverage, string NormalizerVersion,
    string ComparisonProfile, string? ModelSha256, RepresentationRevision Revisions,
    IReadOnlyDictionary<string, JsonElement> VendorFields);
public sealed record WorkspaceProject(ProjectKey ProjectKey, string OriginalName, string TiaVersion,
    IReadOnlyList<WorkspaceDevice> Devices, IReadOnlyList<WorkspaceGroup> Groups);
public sealed record WorkspaceManifest(string SchemaVersion, Guid WorkspaceId, ProjectKey ProjectKey,
    string AdapterId, string AdapterBuild, string CapabilitySetHash, string ExportRevision,
    WorkspaceProject Project, IReadOnlyList<WorkspaceObject> Inventory, IReadOnlyList<ExchangeUnit> ExchangeUnits,
    Coverage Coverage, string NormalizerVersion, string ComparisonProfile);

/// <summary>Pure configuration. Root is a Windows lexical path, not a verified directory.</summary>
public sealed record WorkspaceConfiguration(string SchemaVersion, Guid WorkspaceId, string RootPath,
    IReadOnlyList<ProjectKey> Projects);
