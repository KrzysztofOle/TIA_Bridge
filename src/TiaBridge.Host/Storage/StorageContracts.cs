using TiaBridge.Core.Identity;
using TiaBridge.Core.Operations;
using TiaBridge.Core.Transport.V1;

namespace TiaBridge.Host.Storage;

public sealed class StorageException : Exception
{
    public ErrorCode Code { get; }
    public bool CommitMayHaveSucceeded { get; }
    public StorageException(ErrorCode code, bool commitMayHaveSucceeded = false) : base("Durable storage operation failed: " + code)
    { Code = code; CommitMayHaveSucceeded = commitMayHaveSucceeded; }
    internal static ErrorCode IoCode(Exception exception) => exception is IOException io && (io.HResult & 0xffff) is 0x27 or 0x70 ? ErrorCode.STORAGE_FULL : ErrorCode.STORE_UNAVAILABLE;
}

public enum StorageFaultPoint
{
    MigrationBeforeCommit, OperationInserted, EventInserted, OutboxInserted, BeforeCommit, AfterCommit,
    ArtifactBeforeFlush, ArtifactFlushed, ArtifactPublished, ArtifactBeforeReference, WorkerReceiptCommitted
}
/// <summary>Test seam; production uses NoStorageFaults. A checkpoint never schedules or retries work.</summary>
public interface IStorageFaults { void Checkpoint(StorageFaultPoint point); }
public sealed class NoStorageFaults : IStorageFaults
{
    public static NoStorageFaults Instance { get; } = new();
    private NoStorageFaults() { }
    public void Checkpoint(StorageFaultPoint point) { }
}

public sealed record StoredOperation(OperationReport Report, long Revision, byte[] RequestJson, string BodySha256);
public sealed record Admission(StoredOperation Operation, bool AlreadyAccepted);
public sealed record JournalEvent(OperationId OperationId, long Sequence, DateTimeOffset OccurredAt, OperationReport Report);
public enum OutboxState { PENDING, CLAIMED, DISPATCH_RECORDED, CLOSED }
public sealed record OutboxEntry(OperationId OperationId, OutboxState State, Guid? ClaimId);
public sealed record ArtifactDescriptor(string ArtifactId, string Sha256, long Size, string RelativePath);
public sealed record RetainedOrphan(string RelativePath, string Reason);

public interface IOperationRepository
{
    void RegisterProject(ProjectKey project);
    Admission Accept(string authenticatedPrincipal, ReadOnlyMemory<byte> requestJson);
    StoredOperation Read(OperationId operationId);
    OperationStatusResult ReadStatus(OperationId operationId);
    OperationReport? ReadResult(OperationId operationId);
    StoredOperation Record(OperationId operationId, long expectedRevision, OperationReport report, Guid? claimId = null);
    IReadOnlyList<JournalEvent> Events(OperationId operationId, long afterSequence = 0, int limit = 100);
}
public interface IOutboxRepository
{
    IReadOnlyList<OutboxEntry> Pending(int limit = 100);
    Guid? TryClaim(OperationId operationId);
    bool RecordDispatch(OperationId operationId, Guid claimId);
}
public interface IArtifactStore
{
    ArtifactDescriptor Put(OperationId operationId, Stream input);
    VerifiedArtifact OpenVerified(string artifactId);
    IReadOnlyList<RetainedOrphan> RetainedOrphans();
}
public sealed class VerifiedArtifact : IDisposable
{
    public ArtifactDescriptor Descriptor { get; }
    public Stream Content { get; }
    internal VerifiedArtifact(ArtifactDescriptor descriptor, Stream content) { Descriptor = descriptor; Content = content; }
    public void Dispose() => Content.Dispose();
}
