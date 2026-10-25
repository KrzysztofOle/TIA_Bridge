using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using TiaBridge.Core.Operations;

namespace TiaBridge.Host.Storage.SQLite;

internal sealed class SqliteDatabase
{
    internal const int ApplicationId = 0x54494232;
    internal const int SchemaVersion = 3;
    private static readonly ConcurrentDictionary<string, object> Gates = new(StringComparer.OrdinalIgnoreCase);
    internal StorageLocation Location { get; }
    internal IStorageFaults Faults { get; }
    private readonly object writerGate;

    internal SqliteDatabase(StorageLocation location, IStorageFaults faults, int targetVersion = SchemaVersion)
    {
        Location = location; Faults = faults; writerGate = Gates.GetOrAdd(location.RootPath, _ => new object());
        Initialize(targetVersion);
    }

    internal SqliteConnection Connect(bool allowCreate = false)
    {
        Location.Check();
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Location.DatabasePath,
            Mode = allowCreate ? SqliteOpenMode.ReadWriteCreate : SqliteOpenMode.ReadWrite,
            Cache = SqliteCacheMode.Private,
            ForeignKeys = true,
            Pooling = false,
            DefaultTimeout = 5
        }.ToString());
        try { connection.Open(); Exec(connection, null, "PRAGMA synchronous=FULL;"); return connection; }
        catch { connection.Dispose(); throw; }
    }

    private void Initialize(int targetVersion)
    {
        Write((connection, transaction) =>
        {
            long app = ScalarLong(connection, transaction, "PRAGMA application_id;");
            long version = ScalarLong(connection, transaction, "PRAGMA user_version;");
            if (version > targetVersion || app != ApplicationId && (app != 0 || version != 0
                || ScalarLong(connection, transaction, "SELECT count(*) FROM sqlite_master WHERE name NOT LIKE 'sqlite_%';") != 0))
                throw new StorageException(ErrorCode.SCHEMA_VERSION);
            for (int i = 1; i <= version; i++)
            {
                string? hash = Scalar(connection, transaction, "SELECT sha256 FROM schema_migrations WHERE version=$v", ("$v", i)) as string;
                if (hash != MigrationHash(i)) throw new StorageException(ErrorCode.SCHEMA_VERSION);
            }
            if (version > 0 && ScalarLong(connection, transaction, "SELECT count(*) FROM schema_migrations;") != version)
                throw new StorageException(ErrorCode.SCHEMA_VERSION);
            for (int i = (int)version + 1; i <= targetVersion; i++)
            {
                Exec(connection, transaction, Migration(i));
                Exec(connection, transaction, "INSERT INTO schema_migrations VALUES($v,$h,$at);", ("$v", i), ("$h", MigrationHash(i)), ("$at", DateTimeOffset.UtcNow.ToString("O")));
                Exec(connection, transaction, $"PRAGMA user_version={i}; PRAGMA application_id={ApplicationId};");
                Faults.Checkpoint(StorageFaultPoint.MigrationBeforeCommit);
            }
            return 0;
        }, allowCreate: true);
        Read(verify =>
        {
            if ((string?)Scalar(verify, null, "PRAGMA integrity_check;") != "ok") throw new StorageException(ErrorCode.STORE_UNAVAILABLE);
            using var foreign = Command(verify, null, "PRAGMA foreign_key_check;");
            using var reader = foreign.ExecuteReader();
            if (reader.Read()) throw new StorageException(ErrorCode.STORE_UNAVAILABLE);
            return 0;
        });
        Location.SealDatabase();
    }

    internal T Write<T>(Func<SqliteConnection, SqliteTransaction, T> action, bool checkpoints = false, bool allowCreate = false)
    {
        lock (writerGate)
        {
            bool commitAttempted = false;
            try
            {
                using var connection = Connect(allowCreate);
                // Check foreign databases before changing their journal mode.
                long app = ScalarLong(connection, null, "PRAGMA application_id;");
                if (app != ApplicationId && (app != 0 || ScalarLong(connection, null, "SELECT count(*) FROM sqlite_master WHERE name NOT LIKE 'sqlite_%';") != 0))
                    throw new StorageException(ErrorCode.SCHEMA_VERSION);
                if ((string?)Scalar(connection, null, "PRAGMA journal_mode=WAL;") != "wal") throw new StorageException(ErrorCode.STORE_UNAVAILABLE);
                using var transaction = connection.BeginTransaction(deferred: false);
                T value = action(connection, transaction);
                if (checkpoints) Faults.Checkpoint(StorageFaultPoint.BeforeCommit);
                commitAttempted = true;
                transaction.Commit();
                if (checkpoints) Faults.Checkpoint(StorageFaultPoint.AfterCommit);
                return value;
            }
            catch (SqliteException e) { throw new StorageException(e.SqliteErrorCode is 5 or 6 ? ErrorCode.STORE_UNAVAILABLE : e.SqliteErrorCode == 13 ? ErrorCode.STORAGE_FULL : ErrorCode.STORE_UNAVAILABLE, commitAttempted); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { throw new StorageException(StorageException.IoCode(e), commitAttempted); }
        }
    }

    internal T Read<T>(Func<SqliteConnection, T> action)
    {
        try { using var connection = Connect(); return action(connection); }
        catch (SqliteException) { throw new StorageException(ErrorCode.STORE_UNAVAILABLE); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { throw new StorageException(ErrorCode.STORE_UNAVAILABLE); }
    }

    internal static SqliteCommand Command(SqliteConnection connection, SqliteTransaction? transaction, string sql, params (string, object?)[] values)
    {
        var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = sql;
        foreach (var (name, value) in values) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }
    internal static void Exec(SqliteConnection connection, SqliteTransaction? transaction, string sql, params (string, object?)[] values)
    { using var command = Command(connection, transaction, sql, values); command.ExecuteNonQuery(); }
    internal static object? Scalar(SqliteConnection connection, SqliteTransaction? transaction, string sql, params (string, object?)[] values)
    { using var command = Command(connection, transaction, sql, values); return command.ExecuteScalar(); }
    internal static long ScalarLong(SqliteConnection connection, SqliteTransaction? transaction, string sql, params (string, object?)[] values)
        => Convert.ToInt64(Scalar(connection, transaction, sql, values), CultureInfo.InvariantCulture);
    internal static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static string Migration(int version)
    {
        string name = typeof(SqliteDatabase).Assembly.GetManifestResourceNames().Single(n => n.Contains($".{version:000}_", StringComparison.Ordinal));
        using var stream = typeof(SqliteDatabase).Assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal);
    }
    private static string MigrationHash(int version) => Hash(Encoding.UTF8.GetBytes(Migration(version)));
}
