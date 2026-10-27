using System.Diagnostics;
using Microsoft.Data.Sqlite;
using TiaBridge.Core.Operations;
using TiaBridge.Host.Operations;
using TiaBridge.Host.Storage;
using Xunit;

namespace TiaBridge.Host.Tests;

public class MigrationAndPolicyTests
{
    [Theory]
    [InlineData("M:\\_Moje_Programy\\TIA_Bridge\\state")]
    [InlineData("\\\\server\\share\\state")]
    [InlineData("\\\\?\\C:\\state")]
    [InlineData("C:relative")]
    [InlineData("C:\\foreign")]
    public void Unsafe_unapproved_or_non_ntfs_locations_are_rejected_without_creation(string path)
    {
        using var f = new StoreFixture();
        Assert.Equal(ErrorCode.PATH_UNSAFE, Assert.Throws<StorageException>(() => StorageLocation.Open(path, f.Repository)).Code);
    }

    [Fact]
    public void Repository_overlap_foreign_directory_and_reparse_ancestor_are_rejected()
    {
        using var f = new StoreFixture();
        Assert.Throws<StorageException>(() => StorageLocation.Validate(f.Root, f.Root));
        Directory.CreateDirectory(f.Root); string foreign = Path.Combine(f.Root, "foreign.txt"); File.WriteAllText(foreign, "synthetic foreign file");
        Assert.Throws<StorageException>(() => f.Open()); Assert.Equal("synthetic foreign file", File.ReadAllText(foreign));
        // Junction is created only inside this test-owned root; no privilege or external target is needed.
        string target = Path.Combine(f.Root, "target"), link = Path.Combine(f.Root, "link"); Directory.CreateDirectory(target);
        var info = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string arg in new[] { "/d", "/c", "mklink", "/J", link, target }) info.ArgumentList.Add(arg);
        using var process = Process.Start(info)!; Assert.True(process.WaitForExit(10000)); Assert.Equal(0, process.ExitCode);
        Assert.Throws<StorageException>(() => StorageLocation.Validate(Path.Combine(link, "state"), f.Repository));
    }

    [Fact]
    public void Migrations_upgrade_repeatedly_preserve_rows_and_verify_sqlite_settings()
    {
        using var f = new StoreFixture();
        var first = new SqliteOperationStore(f.Location, NoStorageFaults.Instance, TimeProvider.System, 1);
        var operation = f.Accept(first); Assert.Equal(1, f.Count("schema_migrations"));
        var upgraded = f.Open(); Assert.Equal(4, f.Count("schema_migrations"));
        Assert.Equal(operation.Operation.Report.OperationId, f.Accept(upgraded).Operation.Report.OperationId);
        _ = f.Open(); Assert.Equal(4, f.Count("schema_migrations"));
        using var c = f.Connection(); using var command = c.CreateCommand();
        command.CommandText = "PRAGMA journal_mode"; Assert.Equal("wal", command.ExecuteScalar());
        command.CommandText = "PRAGMA foreign_keys"; Assert.Equal(1L, command.ExecuteScalar());
        // synchronous is per connection: inspect the factory's configured connection, not this test helper's defaults.
        var db = new TiaBridge.Host.Storage.SQLite.SqliteDatabase(f.Location, NoStorageFaults.Instance);
        using var own = db.Connect(); using var pragma = own.CreateCommand(); pragma.CommandText = "PRAGMA synchronous"; Assert.Equal(2L, pragma.ExecuteScalar());
        // B-02B must also upgrade the actual B-02A v2 baseline, retaining both immutable data and idempotency.
        using var legacy = new StoreFixture();
        var v2 = new SqliteOperationStore(legacy.Location, NoStorageFaults.Instance, TimeProvider.System, 2);
        var old = legacy.Accept(v2); Assert.Equal(2, legacy.Count("schema_migrations"));
        var v3 = legacy.Open(); Assert.Equal(4, legacy.Count("schema_migrations"));
        var repeated = legacy.Accept(v3); Assert.True(repeated.AlreadyAccepted);
        Assert.Equal(old.Operation.Report.OperationId, repeated.Operation.Report.OperationId);
        Assert.Equal(old.Operation.RequestJson, v3.Read(old.Operation.Report.OperationId).RequestJson);
    }

    [Fact]
    public void Failed_upgrade_rolls_back_schema_and_preserves_accepted_work()
    {
        using var f = new StoreFixture(); var v1 = new SqliteOperationStore(f.Location, NoStorageFaults.Instance, TimeProvider.System, 1);
        var accepted = f.Accept(v1);
        Assert.Throws<StorageException>(() => f.Open(new ThrowFault(StorageFaultPoint.MigrationBeforeCommit)));
        Assert.Equal(1, f.Count("schema_migrations")); Assert.Equal(1, f.Count("operations"));
        Assert.Equal(accepted.Operation.Report.OperationId, f.Open().Read(accepted.Operation.Report.OperationId).Report.OperationId);
    }

    [Theory]
    [InlineData("PRAGMA user_version=99")]
    [InlineData("UPDATE schema_migrations SET sha256='corrupt' WHERE version=1")]
    public void Future_or_tampered_schema_is_not_silently_repaired(string sql)
    {
        using var f = new StoreFixture(); var store = f.Open(); f.Accept(store); f.Sql(sql);
        Assert.Equal(ErrorCode.SCHEMA_VERSION, Assert.Throws<StorageException>(() => f.Open()).Code);
        Assert.Equal(1, f.Count("operations"));
    }

    [Fact]
    public void Foreign_database_is_not_migrated_and_sql_constraints_protect_history()
    {
        using var f = new StoreFixture(); _ = f.Location;
        using (var c = f.Connection()) { using var cmd = c.CreateCommand(); cmd.CommandText = "CREATE TABLE foreign_data(value TEXT); INSERT INTO foreign_data VALUES('synthetic');"; cmd.ExecuteNonQuery(); }
        Assert.Throws<StorageException>(() => f.Open());
        using var clean = new StoreFixture(); var store = clean.Open(); clean.Accept(store);
        foreach (string sql in new[] { "DELETE FROM operations", "DELETE FROM events", "UPDATE events SET event_sequence=20", "DELETE FROM idempotency_keys", "DELETE FROM outbox", "UPDATE operations SET state='SUCCEEDED'" })
            Assert.Throws<SqliteException>(() => clean.Sql(sql));
        Assert.Equal(1, clean.Count("operations")); Assert.Equal(1, clean.Count("events"));
    }

    [Fact]
    public void Corrupt_sqlite_file_is_rejected_without_reinitializing_it()
    {
        using var f = new StoreFixture(); var store = f.Open(); f.Accept(store);
        byte[] corrupt = "synthetic invalid sqlite bytes"u8.ToArray(); File.WriteAllBytes(f.Location.DatabasePath, corrupt);
        Assert.Throws<StorageException>(() => f.Open());
        Assert.Equal(corrupt, File.ReadAllBytes(f.Location.DatabasePath));
    }

    [Fact]
    public void Missing_previously_initialized_database_never_creates_a_fresh_empty_ledger()
    {
        using var f = new StoreFixture(); var store = f.Open(); var accepted = f.Accept(store);
        string path = f.Location.DatabasePath; File.Delete(path);
        Assert.Throws<StorageException>(() => f.Open());
        Assert.Throws<StorageException>(() => store.Read(accepted.Operation.Report.OperationId));
        Assert.False(File.Exists(path));
    }
}
