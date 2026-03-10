using Microsoft.Data.Sqlite;
using System.IO;
using JoJot.Data;

namespace JoJot.Services;

/// <summary>
/// Database infrastructure: connection lifecycle, schema, migrations, write lock.
/// Domain operations live in NoteStore, SessionStore, PreferenceStore, PendingMoveStore.
/// </summary>
public static class DatabaseCore
{
    private static SqliteConnection? _rawConnection;
    private static readonly SemaphoreSlim _writeLock = new(1, 1);
    private static readonly TimeSpan WriteLockTimeout = TimeSpan.FromSeconds(10);
    private static string _dbPath = string.Empty;
    private static string _connectionString = string.Empty;
    private static IClock _clock = SystemClock.Instance;

    /// <summary>Replaces the clock used for timestamps. For testing only.</summary>
    internal static void SetClock(IClock clock) => _clock = clock;

    internal static IClock Clock => _clock;

    internal static async Task AcquireWriteLockAsync(CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(WriteLockTimeout);
        try
        {
            await _writeLock.WaitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Failed to acquire database write lock within {WriteLockTimeout.TotalSeconds}s");
        }
    }

    internal static void ReleaseWriteLock() => _writeLock.Release();

    internal static JoJotDbContext CreateContext() => new(_connectionString);

    /// <summary>The current connection string. Exposed for test verification only.</summary>
    internal static string ConnectionString => _connectionString;

    /// <summary>Creates a DbContext using the current connection string. For integration tests.</summary>
    internal static JoJotDbContext CreateTestContext() => CreateContext();

    /// <summary>
    /// Opens (or creates) the SQLite database at the specified path.
    /// Enables WAL journal mode, NORMAL synchronous, and foreign keys.
    /// </summary>
    public static async Task OpenAsync(string dbPath)
    {
        _dbPath = dbPath;
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        var connStrBuilder = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            DefaultTimeout = 5,
        };
        _connectionString = connStrBuilder.ToString();

        _rawConnection = new SqliteConnection(_connectionString);
        await _rawConnection.OpenAsync().ConfigureAwait(false);

        await ExecuteRawPragmaAsync("PRAGMA journal_mode=WAL;").ConfigureAwait(false);
        await ExecuteRawPragmaAsync("PRAGMA synchronous=NORMAL;").ConfigureAwait(false);
        await ExecuteRawPragmaAsync("PRAGMA foreign_keys=ON;").ConfigureAwait(false);

        LogService.Info("Database opened: {DbPath}", dbPath);
    }

    /// <summary>
    /// Opens the database using a pre-built connection string (no file path processing).
    /// For integration tests with in-memory SQLite.
    /// </summary>
    internal static async Task OpenWithConnectionStringAsync(string connectionString)
    {
        _dbPath = ":memory:";
        _connectionString = connectionString;

        _rawConnection = new SqliteConnection(_connectionString);
        await _rawConnection.OpenAsync().ConfigureAwait(false);

        await ExecuteRawPragmaAsync("PRAGMA foreign_keys=ON;").ConfigureAwait(false);
    }

    /// <summary>
    /// Creates all schema tables if they do not already exist.
    /// </summary>
    public static async Task EnsureSchemaAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync().ConfigureAwait(false);
        LogService.Info("Schema ensured (all four tables).");
    }

    /// <summary>
    /// Checks that all expected tables exist in sqlite_master.
    /// Returns true if the database is healthy; false if corrupt or tables are missing.
    /// </summary>
    public static async Task<bool> VerifyIntegrityAsync(CancellationToken ct = default)
    {
        string[] required = ["notes", "app_state", "pending_moves", "preferences"];
        foreach (var table in required)
        {
            long count = await ExecuteScalarAsync<long>(
                $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{table}';").ConfigureAwait(false);
            if (count == 0)
            {
                LogService.Warn("Expected table {Table} is missing — running PRAGMA quick_check", table);
                return await RunQuickCheckAsync().ConfigureAwait(false);
            }
        }
        return true;
    }

    /// <summary>
    /// Handles database corruption: closes the connection, renames the corrupt file to .corrupt,
    /// then recreates a fresh database with the full schema.
    /// </summary>
    public static async Task HandleCorruptionAsync(string dbPath)
    {
        LogService.Error("Database corruption detected — backing up and recreating");

        _rawConnection?.Dispose();
        _rawConnection = null;

        string corruptPath = dbPath + ".corrupt";
        if (File.Exists(corruptPath))
            File.Delete(corruptPath);

        File.Move(dbPath, corruptPath);
        LogService.Warn("Corrupt database backed up to: {CorruptPath}", corruptPath);

        await OpenAsync(dbPath).ConfigureAwait(false);
        await EnsureSchemaAsync().ConfigureAwait(false);

        LogService.Info("Fresh database created after corruption recovery.");
    }

    /// <summary>
    /// Executes a non-query SQL command. Serialized through the write lock.
    /// </summary>
    public static async Task ExecuteNonQueryAsync(string sql)
    {
        await AcquireWriteLockAsync().ConfigureAwait(false);
        try
        {
            await using var cmd = _rawConnection!.CreateCommand();
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogService.Error("ExecuteNonQueryAsync failed: {Sql}", sql, ex);
            throw;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Executes a scalar SQL query. Serialized through the write lock.
    /// </summary>
    public static async Task<T> ExecuteScalarAsync<T>(string sql)
    {
        await AcquireWriteLockAsync().ConfigureAwait(false);
        try
        {
            await using var cmd = _rawConnection!.CreateCommand();
            cmd.CommandText = sql;
            var result = await cmd.ExecuteScalarAsync().ConfigureAwait(false);
            return (T)Convert.ChangeType(result!, typeof(T));
        }
        catch (Exception ex)
        {
            LogService.Error("ExecuteScalarAsync failed: {Sql}", sql, ex);
            throw;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Runs pending schema migrations. Idempotent — safe to run on every launch.
    /// </summary>
    public static async Task RunPendingMigrationsAsync()
    {
        bool hasWindowState = await ColumnExistsAsync("app_state", "window_state").ConfigureAwait(false);
        if (!hasWindowState)
        {
            await ExecuteNonQueryAsync("ALTER TABLE app_state ADD COLUMN window_state TEXT;").ConfigureAwait(false);
            LogService.Info("Migration: added window_state column to app_state");
        }
        else
        {
            LogService.Info("No pending migrations.");
        }
    }

    /// <summary>
    /// Closes and disposes the database connection.
    /// </summary>
    public static async Task CloseAsync()
    {
        if (_rawConnection is not null)
        {
            await _rawConnection.CloseAsync().ConfigureAwait(false);
            _rawConnection.Dispose();
            _rawConnection = null;
            LogService.Info("Database connection closed.");
        }
    }

    // ─── Private helpers ─────────────────────────────────────────────────────

    private static async Task<bool> RunQuickCheckAsync()
    {
        try
        {
            await AcquireWriteLockAsync().ConfigureAwait(false);
            try
            {
                await using var cmd = _rawConnection!.CreateCommand();
                cmd.CommandText = "PRAGMA quick_check;";
                await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
                if (reader.Read())
                {
                    string result = reader.GetString(0);
                    bool ok = string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase);
                    if (!ok)
                        LogService.Error("PRAGMA quick_check returned: {Result}", result);
                    return ok;
                }
                return false;
            }
            finally
            {
                _writeLock.Release();
            }
        }
        catch (Exception ex)
        {
            LogService.Error("PRAGMA quick_check failed with exception", ex);
            return false;
        }
    }

    private static async Task<bool> ColumnExistsAsync(string table, string column)
    {
        bool found = false;
        await AcquireWriteLockAsync().ConfigureAwait(false);
        try
        {
            await using var cmd = _rawConnection!.CreateCommand();
            cmd.CommandText = $"PRAGMA table_info({table});";
            await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
            while (reader.Read())
            {
                if (reader.GetString(1) == column)
                {
                    found = true;
                    break;
                }
            }
        }
        finally
        {
            _writeLock.Release();
        }
        return found;
    }

    private static async Task ExecuteRawPragmaAsync(string sql)
    {
        await using var cmd = _rawConnection!.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }
}
