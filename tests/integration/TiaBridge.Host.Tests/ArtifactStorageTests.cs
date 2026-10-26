using System.Collections.Immutable;
using TiaBridge.Core.Operations;
using TiaBridge.Core.Transport.V1;
using TiaBridge.Host.Storage;
using Xunit;

namespace TiaBridge.Host.Tests;

public class ArtifactStorageTests
{
    [Fact]
    public void Published_artifact_is_hashed_readonly_linked_and_verified_after_restart()
    {
        using var f = new StoreFixture(); var store = f.Open(); var id = f.Accept(store).Operation.Report.OperationId;
        var artifact = store.Artifacts.Put(id, new MemoryStream("abc"u8.ToArray()));
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", artifact.Sha256);
        Assert.Equal(3, artifact.Size); Assert.Equal(1, f.Count("artifacts")); Assert.Empty(store.Artifacts.RetainedOrphans());
        using var read = f.Open().Artifacts.OpenVerified(artifact.ArtifactId);
        Assert.False(read.Content.CanWrite); Assert.Equal('a', (char)read.Content.ReadByte());
        Assert.Equal(FileAttributes.ReadOnly, File.GetAttributes(Path.Combine(f.Root, artifact.RelativePath)) & FileAttributes.ReadOnly);
        var error = Record.Exception(() => File.WriteAllBytes(Path.Combine(f.Root, artifact.RelativePath), new byte[] { 0 }));
        Assert.True(error is IOException or UnauthorizedAccessException);
    }

    [Theory]
    [InlineData("truncated")]
    [InlineData("modified")]
    [InlineData("missing")]
    public void Missing_or_corrupt_content_never_becomes_a_valid_operation_result(string defect)
    {
        using var f = new StoreFixture(); var store = f.Open(); var accepted = f.Accept(store, "diagnostics.health").Operation;
        var artifact = store.Artifacts.Put(accepted.Report.OperationId, new MemoryStream("abc"u8.ToArray()));
        string path = Path.Combine(f.Root, artifact.RelativePath);
        File.SetAttributes(path, FileAttributes.Normal);
        if (defect == "missing") File.Delete(path);
        else File.WriteAllText(path, defect == "truncated" ? "a" : "xyz");
        Assert.Equal(ErrorCode.VERIFY_FAILED, Assert.Throws<StorageException>(() => store.Artifacts.OpenVerified(artifact.ArtifactId)).Code);
        var running = StoreFixture.Start(store, accepted, out _);
        var result = ContractJson.ReadReport(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "response.diagnostics.health.json")));
        Assert.Throws<StorageException>(() => store.Record(running.Report.OperationId, running.Revision, result with
        { OperationId = running.Report.OperationId, AcceptedAt = running.Report.AcceptedAt, ArtifactRefs = ImmutableArray.Create(new ArtifactRef(artifact.ArtifactId, artifact.Sha256)) }));
        Assert.Equal(0, f.Count("operation_results")); Assert.Equal(OperationState.RUNNING, store.Read(accepted.Report.OperationId).Report.State);
    }

    [Fact]
    public void Final_proof_and_nested_artifact_references_are_bound_to_real_managed_content()
    {
        using var f = new StoreFixture(); var store = f.Open(); var accepted = f.Accept(store, "diagnostics.health").Operation;
        var artifact = store.Artifacts.Put(accepted.Report.OperationId, new MemoryStream("abc"u8.ToArray()));
        var running = StoreFixture.Start(store, accepted, out _);
        var result = ContractJson.ReadReport(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "response.diagnostics.health.json")));
        var report = result with { OperationId = running.Report.OperationId, AcceptedAt = running.Report.AcceptedAt, ArtifactRefs = ImmutableArray.Create(new ArtifactRef(artifact.ArtifactId, artifact.Sha256)) };
        Assert.Throws<StorageException>(() => store.Record(running.Report.OperationId, running.Revision, report with { ArtifactRefs = ImmutableArray.Create(new ArtifactRef(artifact.ArtifactId, new string('f', 64))) }));
        store.Record(running.Report.OperationId, running.Revision, report);
        Assert.Equal(OperationState.SUCCEEDED, f.Open().ReadResult(running.Report.OperationId)!.State);
        string path = Path.Combine(f.Root, artifact.RelativePath); File.SetAttributes(path, FileAttributes.Normal); File.WriteAllText(path, "corrupt");
        Assert.Throws<StorageException>(() => f.Open().ReadResult(running.Report.OperationId));
        Assert.Equal(OperationStatusResult_State.SUCCEEDED, f.Open().ReadStatus(running.Report.OperationId).State); // historical metadata, no artifact IO in status
        Assert.Throws<StorageException>(() => f.Accept(f.Open(), "diagnostics.health"));
        Assert.Equal(4, store.Events(running.Report.OperationId).Count); // Historical evidence remains available; it is not a fresh integrity assertion.
    }

    [Fact]
    public void Native_nested_artifact_reference_is_checked_even_without_top_level_artifact_refs()
    {
        using var f = new StoreFixture(); var store = f.Open(); var accepted = f.Accept(store, "project.export").Operation;
        var running = StoreFixture.Start(store, accepted, out _);
        var result = ContractJson.ReadReport(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "response.project.export.json")));
        Assert.Throws<StorageException>(() => store.Record(running.Report.OperationId, running.Revision, result with { OperationId = running.Report.OperationId, AcceptedAt = running.Report.AcceptedAt }));
        Assert.Equal(0, f.Count("operation_results"));
    }

    [Theory]
    [InlineData("invalid-hash")]
    [InlineData("other-file-hash")]
    [InlineData("other-file-id")]
    public void Completion_rejects_a_reference_whose_hash_is_not_bound_to_its_exact_file(string defect)
    {
        using var f = new StoreFixture(); var store = f.Open();
        var accepted = f.Accept(store, "diagnostics.health").Operation;
        var first = store.Artifacts.Put(accepted.Report.OperationId, new MemoryStream("abc"u8.ToArray()));
        var second = store.Artifacts.Put(accepted.Report.OperationId, new MemoryStream("xyz"u8.ToArray()));
        using (var verified = store.Artifacts.OpenVerified(first.ArtifactId)) Assert.Equal(first, verified.Descriptor);
        var running = StoreFixture.Start(store, accepted, out _);
        var template = ContractJson.ReadReport(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "response.diagnostics.health.json")));
        var reference = defect switch
        {
            "invalid-hash" => new ArtifactRef(first.ArtifactId, new string('f', 64)),
            "other-file-hash" => new ArtifactRef(first.ArtifactId, second.Sha256),
            _ => new ArtifactRef(second.ArtifactId, first.Sha256)
        };
        var completion = template with { OperationId = running.Report.OperationId, AcceptedAt = running.Report.AcceptedAt, ArtifactRefs = [reference] };
        Assert.Equal(ErrorCode.VERIFY_FAILED, Assert.Throws<StorageException>(() => store.Record(running.Report.OperationId, running.Revision, completion)).Code);
        Assert.Equal(0, f.Count("operation_results"));
        Assert.Equal(OperationState.RUNNING, store.Read(accepted.Report.OperationId).Report.State);
        // A rejected reference does not poison valid content or silently synthesize a result.
        store.Record(running.Report.OperationId, running.Revision, completion with { ArtifactRefs = [new(first.ArtifactId, first.Sha256)] });
        Assert.Equal(OperationState.SUCCEEDED, f.Open().ReadResult(accepted.Report.OperationId)!.State);
    }

    [Theory]
    [InlineData(StorageFaultPoint.ArtifactFlushed, "TEMPORARY_RETAINED")]
    [InlineData(StorageFaultPoint.ArtifactPublished, "UNREFERENCED_RETAINED")]
    public void Artifact_crash_boundaries_retain_orphans_without_adoption_or_deletion(StorageFaultPoint point, string reason)
    {
        using var f = new StoreFixture(); var process = f.Probe("artifact", point.ToString()); Assert.Equal(73, process.Code);
        var store = f.Open(); Assert.Equal(0, f.Count("artifacts")); Assert.Equal(1, f.Count("operations"));
        var orphan = Assert.Single(store.Artifacts.RetainedOrphans()); Assert.Equal(reason, orphan.Reason);
        Assert.True(File.Exists(Path.Combine(f.Root, orphan.RelativePath)));
        Assert.Single(f.Open().Artifacts.RetainedOrphans());
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("C:\\outside")]
    [InlineData("artifact-00000000000000000000000000000000:stream")]
    public void Artifact_ids_cannot_address_arbitrary_paths(string id)
    {
        using var f = new StoreFixture(); var store = f.Open();
        Assert.Equal(ErrorCode.PATH_UNSAFE, Assert.Throws<StorageException>(() => store.Artifacts.OpenVerified(id)).Code);
    }

    [Fact]
    public void Incomplete_input_stream_keeps_a_temporary_orphan_without_a_database_reference()
    {
        using var f = new StoreFixture(); var store = f.Open(); var id = f.Accept(store).Operation.Report.OperationId;
        using var input = new IncompleteStream();
        Assert.Throws<StorageException>(() => store.Artifacts.Put(id, input));
        Assert.Equal(0, f.Count("artifacts"));
        Assert.Equal("TEMPORARY_RETAINED", Assert.Single(f.Open().Artifacts.RetainedOrphans()).Reason);
        Assert.Null(store.ReadResult(id));
    }

    private sealed class IncompleteStream : MemoryStream
    {
        private bool read;
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (read) throw new IOException("Synthetic interrupted source stream");
            read = true; buffer[offset] = 42; return 1;
        }
    }
}
