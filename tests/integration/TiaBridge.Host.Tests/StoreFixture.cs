using System.Diagnostics;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using TiaBridge.Core.Identity;
using TiaBridge.Core.Operations;
using TiaBridge.Core.Transport.V1;
using TiaBridge.Host.Operations;
using TiaBridge.Host.Storage;
using Xunit;

namespace TiaBridge.Host.Tests;

internal sealed class StoreFixture : IDisposable
{
    internal string Repository { get; } = FindRepository();
    internal string Root { get; } = Path.Combine(StorageLocation.ApplicationRoot, "dev", "storage-tests", Guid.NewGuid().ToString("N"));
    internal StorageLocation Location => StorageLocation.Open(Root, Repository);
    internal SqliteOperationStore Open(IStorageFaults? faults = null) => new(Location, faults);
    internal static byte[] Wire(string method = "sync.apply") => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "request." + method + ".json"));
    internal Admission Accept(SqliteOperationStore store, string method = "sync.apply", string principal = "synthetic-principal")
    {
        var request = ContractJson.ReadRequest(Wire(method));
        if (request.Identity.Project is not null) store.RegisterProject(request.Identity.Project);
        return store.Accept(principal, Wire(method));
    }
    internal SqliteConnection Connection()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(Root, "operations.sqlite3"), Pooling = false, ForeignKeys = true }.ToString());
        connection.Open(); return connection;
    }
    internal long Count(string table)
    {
        Assert.Contains(table, new[] { "operations", "events", "outbox", "idempotency_keys", "artifacts", "operation_results", "quarantines", "schema_migrations" });
        using var c = Connection(); using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT count(*) FROM " + table; return (long)cmd.ExecuteScalar()!;
    }
    internal void Sql(string sql)
    { using var c = Connection(); using var cmd = c.CreateCommand(); cmd.CommandText = sql; cmd.ExecuteNonQuery(); }
    internal static StoredOperation Start(SqliteOperationStore store, StoredOperation accepted, out Guid claim)
    {
        var queued = store.Record(accepted.Report.OperationId, accepted.Revision, accepted.Report with { State = OperationState.QUEUED, Phase = "QUEUED" });
        claim = store.TryClaim(queued.Report.OperationId)!.Value;
        Assert.True(store.RecordDispatch(queued.Report.OperationId, claim));
        return store.Record(queued.Report.OperationId, queued.Revision, queued.Report with { State = OperationState.RUNNING, Phase = "RUNNING" }, claim);
    }
    internal (int Code, string Output) Probe(string mode = "admit", string checkpoint = "none")
    {
        string configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        string dll = Path.Combine(Repository, "tests", "integration", "TiaBridge.StorageProbe", "bin", configuration, "net10.0", "TiaBridge.StorageProbe.dll");
        var info = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string arg in new[] { dll, Root, Repository, mode, checkpoint }) info.ArgumentList.Add(arg);
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30000)) { process.Kill(entireProcessTree: true); throw new TimeoutException("Own storage test process exceeded its deadline."); }
        return (process.ExitCode, output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult());
    }
    private static string FindRepository()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TiaBridge.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root missing.");
    }
    public void Dispose()
    {
        // Remove only this test's unique NTFS directory, never state or any foreign path. No recursive reparse traversal.
        string allowed = Path.GetFullPath(Path.Combine(StorageLocation.ApplicationRoot, "dev", "storage-tests")) + Path.DirectorySeparatorChar;
        string root = Path.GetFullPath(Root);
        if (!root.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unsafe test cleanup.");
        if (!Directory.Exists(root)) return;
        void Delete(string directory)
        {
            foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
            {
                FileAttributes attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    if ((attributes & FileAttributes.ReparsePoint) == 0) Delete(entry);
                    else Directory.Delete(entry, recursive: false);
                }
                else
                {
                    if ((attributes & FileAttributes.ReparsePoint) == 0) File.SetAttributes(entry, FileAttributes.Normal);
                    File.Delete(entry);
                }
            }
            Directory.Delete(directory, recursive: false);
        }
        Delete(root);
    }
}

internal sealed class ThrowFault(StorageFaultPoint point, bool sqliteFull = false) : IStorageFaults
{
    public void Checkpoint(StorageFaultPoint actual)
    {
        if (actual != point) return;
        if (sqliteFull) throw new SqliteException("Synthetic disk full", 13);
        throw new IOException("Synthetic storage failure");
    }
}

internal sealed class DiskFullFault : IStorageFaults
{
    public void Checkpoint(StorageFaultPoint point)
    {
        if (point == StorageFaultPoint.ArtifactBeforeFlush) throw new IOException("Synthetic disk full", unchecked((int)0x80070070));
    }
}
