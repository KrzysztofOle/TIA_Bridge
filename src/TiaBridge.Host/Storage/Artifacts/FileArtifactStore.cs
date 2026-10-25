using System.Security.Cryptography;
using System.Text.RegularExpressions;
using TiaBridge.Core.Identity;
using TiaBridge.Core.Operations;
using TiaBridge.Host.Storage.SQLite;
using static TiaBridge.Host.Storage.SQLite.SqliteDatabase;

namespace TiaBridge.Host.Storage.Artifacts;

internal sealed class FileArtifactStore(SqliteDatabase database, TimeProvider clock) : IArtifactStore
{
    internal const long MaxArtifactBytes = 1024L * 1024 * 1024;
    private static readonly Regex IdPattern = new(@"\Aartifact-[0-9a-f]{32}\z", RegexOptions.CultureInvariant);

    public ArtifactDescriptor Put(OperationId operationId, Stream input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!input.CanRead) throw new ArgumentException("Readable artifact stream required.");
        _ = database.Read(c => Scalar(c, null, "SELECT operation_id FROM operations WHERE operation_id=$id", ("$id", operationId.ToString())) as string
            ?? throw new StorageException(ErrorCode.NOT_FOUND));
        string id = "artifact-" + Guid.NewGuid().ToString("N");
        string relative = "artifacts/" + id + ".blob";
        string temporary = database.Location.ManagedPath("spool/" + id + ".tmp");
        string published = database.Location.ManagedPath(relative);
        try
        {
            string hash; long size = 0;
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 64 * 1024, FileOptions.WriteThrough))
            {
                byte[] buffer = new byte[64 * 1024]; int read;
                while ((read = input.Read(buffer)) != 0)
                {
                    size += read;
                    if (size > MaxArtifactBytes) throw new StorageException(ErrorCode.LIMIT_EXCEEDED);
                    stream.Write(buffer, 0, read);
                }
                database.Faults.Checkpoint(StorageFaultPoint.ArtifactBeforeFlush);
                stream.Flush(true);
                database.Faults.Checkpoint(StorageFaultPoint.ArtifactFlushed);
                stream.Position = 0;
                hash = Convert.ToHexStringLower(SHA256.HashData(stream));
            }
            _ = database.Location.ManagedPath(relative);
            File.Move(temporary, published, overwrite: false);
            File.SetAttributes(published, FileAttributes.ReadOnly);
            database.Faults.Checkpoint(StorageFaultPoint.ArtifactPublished);
            // Hold a verified read handle, denying write/delete while committing the reference.
            using var content = OpenFile(published);
            if (content.Length != size || Convert.ToHexStringLower(SHA256.HashData(content)) != hash) throw new StorageException(ErrorCode.VERIFY_FAILED);
            database.Faults.Checkpoint(StorageFaultPoint.ArtifactBeforeReference);
            database.Write((c, t) =>
            {
                Exec(c, t, "INSERT INTO artifacts(artifact_id,sha256,size,relative_path,created_at) VALUES($id,$sha,$size,$path,$at)", ("$id", id), ("$sha", hash), ("$size", size), ("$path", relative), ("$at", clock.GetUtcNow().ToString("O")));
                Exec(c, t, "INSERT INTO operation_artifacts VALUES($op,$id)", ("$op", operationId.ToString()), ("$id", id));
                return 0;
            }, checkpoints: true);
            return new(id, hash, size, relative);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { throw new StorageException(StorageException.IoCode(e)); }
        // Failures retain temp or published orphan files. Never auto-adopt, overwrite or collect them.
    }

    public VerifiedArtifact OpenVerified(string artifactId)
    {
        ValidateId(artifactId);
        var descriptor = database.Read(c =>
        {
            using var command = Command(c, null, "SELECT sha256,size,relative_path FROM artifacts WHERE artifact_id=$id", ("$id", artifactId));
            using var reader = command.ExecuteReader();
            if (!reader.Read()) throw new StorageException(ErrorCode.NOT_FOUND);
            return new ArtifactDescriptor(artifactId, reader.GetString(0), reader.GetInt64(1), reader.GetString(2));
        });
        if (descriptor.RelativePath != "artifacts/" + artifactId + ".blob") throw new StorageException(ErrorCode.PATH_UNSAFE);
        try
        {
            var stream = OpenFile(database.Location.ManagedPath(descriptor.RelativePath));
            try
            {
                if (stream.Length != descriptor.Size || Convert.ToHexStringLower(SHA256.HashData(stream)) != descriptor.Sha256) throw new StorageException(ErrorCode.VERIFY_FAILED);
                stream.Position = 0;
                return new VerifiedArtifact(descriptor, stream);
            }
            catch { stream.Dispose(); throw; }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { throw new StorageException(ErrorCode.VERIFY_FAILED); }
    }

    public IReadOnlyList<RetainedOrphan> RetainedOrphans()
    {
        var registered = database.Read(c =>
        {
            using var command = Command(c, null, "SELECT relative_path FROM artifacts");
            using var reader = command.ExecuteReader(); var paths = new HashSet<string>(StringComparer.Ordinal);
            while (reader.Read()) paths.Add(reader.GetString(0));
            return paths;
        });
        var result = new List<RetainedOrphan>();
        foreach (string directory in new[] { "spool", "artifacts" })
        {
            string path = database.Location.ManagedPath(directory);
            foreach (string entry in Directory.EnumerateFileSystemEntries(path, "*", SearchOption.TopDirectoryOnly).Order(StringComparer.Ordinal))
            {
                StorageLocation.CheckChain(entry);
                string relative = directory + "/" + Path.GetFileName(entry);
                if (!registered.Contains(relative)) result.Add(new(relative, directory == "spool" ? "TEMPORARY_RETAINED" : "UNREFERENCED_RETAINED"));
            }
        }
        return result;
    }

    private static FileStream OpenFile(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    private static void ValidateId(string id)
    { if (id is null || !IdPattern.IsMatch(id)) throw new StorageException(ErrorCode.PATH_UNSAFE); }
}
