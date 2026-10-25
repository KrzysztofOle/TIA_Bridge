using System.Security.Cryptography;
using System.Text;
using TiaBridge.Core.Operations;
using TiaBridge.Core.Workspace;

namespace TiaBridge.Host.Storage;

/// <summary>Local Windows NTFS policy; only explicitly owned application directories are initialized.</summary>
public sealed class StorageLocation
{
    private const string MarkerName = ".tia-bridge2-storage-v1";
    private const string Marker = "TIA_BRIDGE2_STORAGE_V1\n";
    private const string DatabaseMarker = ".tia-bridge2-database-initialized";
    public string RootPath { get; }
    public string DatabasePath => Path.Combine(RootPath, "operations.sqlite3");
    public static string ApplicationRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TIA_Bridge2");
    public static string DefaultPath => Path.Combine(ApplicationRoot, "state");

    private StorageLocation(string root) { RootPath = root; }

    public static StorageLocation Open(string root, string repositoryRoot)
    {
        string full = Validate(root, repositoryRoot);
        // Serializes initialization across processes. The SQLite writer is separately serialized by BEGIN IMMEDIATE.
        string key = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(full.ToUpperInvariant())));
        using var mutex = new Mutex(false, "Local\\TIA_Bridge2.StorageInit." + key);
        bool owns = false;
        try
        {
            try { owns = mutex.WaitOne(TimeSpan.FromSeconds(10)); }
            catch (AbandonedMutexException) { owns = true; }
            if (!owns) throw new StorageException(ErrorCode.STORE_UNAVAILABLE);
            CheckChain(full);
            if (Directory.Exists(full) && Directory.EnumerateFileSystemEntries(full).Any())
            {
                string marker = Path.Combine(full, MarkerName);
                CheckChain(marker);
                if (!File.Exists(marker) || File.ReadAllText(marker) != Marker) throw new StorageException(ErrorCode.PATH_UNSAFE);
            }
            else
            {
                Directory.CreateDirectory(full);
                using var marker = new FileStream(Path.Combine(full, MarkerName), FileMode.CreateNew, FileAccess.Write, FileShare.None);
                marker.Write(Encoding.UTF8.GetBytes(Marker)); marker.Flush(true);
            }
            CheckChain(full);
            foreach (string directory in new[] { "artifacts", "spool" })
            {
                string path = Path.Combine(full, directory); CheckChain(path); Directory.CreateDirectory(path);
            }
            return new StorageLocation(full);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { throw new StorageException(ErrorCode.STORE_UNAVAILABLE); }
        finally { if (owns) mutex.ReleaseMutex(); }
    }

    public static string Validate(string root, string repositoryRoot)
    {
        try
        {
            if (!OperatingSystem.IsWindows()) throw new StorageException(ErrorCode.PATH_UNSAFE);
            string full = WorkspacePath.ValidateRoot(root).TrimEnd('\\');
            string repo = Path.GetFullPath(repositoryRoot).TrimEnd('\\');
            string app = Path.GetFullPath(ApplicationRoot).TrimEnd('\\');
            if (!full.StartsWith(app + "\\", StringComparison.OrdinalIgnoreCase)
                || full.Equals(repo, StringComparison.OrdinalIgnoreCase) || full.StartsWith(repo + "\\", StringComparison.OrdinalIgnoreCase)
                || repo.StartsWith(full + "\\", StringComparison.OrdinalIgnoreCase)) throw new StorageException(ErrorCode.PATH_UNSAFE);
            var drive = new DriveInfo(Path.GetPathRoot(full)!);
            if (drive.DriveType != DriveType.Fixed || !drive.IsReady || drive.DriveFormat != "NTFS") throw new StorageException(ErrorCode.PATH_UNSAFE);
            CheckChain(full);
            return full;
        }
        catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException) { throw new StorageException(ErrorCode.PATH_UNSAFE); }
    }

    internal string ManagedPath(string relative)
    {
        WorkspacePath.ValidateRelative(relative);
        string result = WorkspacePath.Resolve(RootPath, relative);
        CheckChain(result);
        return result;
    }

    internal void Check()
    {
        CheckChain(RootPath); CheckChain(DatabasePath); CheckChain(DatabasePath + "-wal"); CheckChain(DatabasePath + "-shm");
        string marker = Path.Combine(RootPath, DatabaseMarker); CheckChain(marker);
        if (File.Exists(marker) && (!File.Exists(DatabasePath) || File.ReadAllText(marker) != Marker)) throw new StorageException(ErrorCode.STORE_UNAVAILABLE);
    }

    internal void SealDatabase()
    {
        string key = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(RootPath.ToUpperInvariant())));
        using var mutex = new Mutex(false, "Local\\TIA_Bridge2.StorageInit." + key); bool owns = false;
        try
        {
            try { owns = mutex.WaitOne(TimeSpan.FromSeconds(10)); }
            catch (AbandonedMutexException) { owns = true; }
            if (!owns) throw new StorageException(ErrorCode.STORE_UNAVAILABLE);
            Check(); string path = Path.Combine(RootPath, DatabaseMarker);
            if (!File.Exists(path))
            {
                string temporary = ManagedPath("spool/database-seal-" + Guid.NewGuid().ToString("N") + ".tmp");
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { stream.Write(Encoding.UTF8.GetBytes(Marker)); stream.Flush(true); }
                File.Move(temporary, path, overwrite: false);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { throw new StorageException(ErrorCode.STORE_UNAVAILABLE); }
        finally { if (owns) mutex.ReleaseMutex(); }
    }

    internal static void CheckChain(string path)
    {
        string? current = Path.GetFullPath(path);
        while (current is not null)
        {
            try
            {
                var attributes = File.GetAttributes(current);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new StorageException(ErrorCode.PATH_UNSAFE);
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            current = Path.GetDirectoryName(current);
        }
    }
}
