using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
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
    private static PooledDbContextFactory<JoJotDbContext>? _contextFactory;

    /// <summary>Replaces the clock used for timestamps. For testing only.</summary>
    internal static void SetClock(IClock clock) => _clock = clock;

    internal static IClock Clock => _clock;

    internal static async Task AcquireWriteLockAsync(CancellationToken ct = default)
    {
        // Fast path: no external cancellation token — skip linked CTS allocation
        if (ct == default)
        {
            if (!await _writeLock.WaitAsync(WriteLockTimeout).ConfigureAwait(false))
                throw new TimeoutException(
                    $"Failed to acquire database write lock within {WriteLockTimeout.TotalSeconds}s");
            return;
        }

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

    internal static JoJotDbContext CreateContext() =>
        _contextFactory?.CreateDbContext() ?? new JoJotDbContext(_connectionString);

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

        InitializeContextPool();

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

        InitializeContextPool();
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
        await AcquireWriteLockAsync(ct).ConfigureAwait(false);
        try
        {
            // Page-level integrity first: quick_check catches on-disk corruption that a
            // table-existence check cannot. Run it unconditionally — it is fast for small DBs.
            await using (var checkCmd = _rawConnection!.CreateCommand())
            {
                checkCmd.CommandText = "PRAGMA quick_check;";
                await using var reader = await checkCmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                if (!await reader.ReadAsync(ct).ConfigureAwait(false))
                    return false;
                string quickCheck = reader.GetString(0);
                if (!string.Equals(quickCheck, "ok", StringComparison.OrdinalIgnoreCase))
                {
                    LogService.Error("PRAGMA quick_check returned: {Result}", quickCheck);
                    return false;
                }
            }

            // Schema completeness: every expected table must exist.
            foreach (var table in required)
            {
                await using var cmd = _rawConnection!.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@name;";
                cmd.Parameters.AddWithValue("@name", table);
                var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
                if (Convert.ToInt64(result) == 0)
                {
                    LogService.Warn("Expected table {Table} is missing", table);
                    return false;
                }
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            // Cancellation is not corruption — never let it trigger destructive recovery.
            throw;
        }
        catch (Exception ex)
        {
            // Only DEFINITE corruption signals above (missing table / quick_check != ok) return false,
            // because the caller responds to false by moving the DB aside and creating an empty one.
            // An unexpected/transient error here (IO hiccup, etc.) must NOT nuke the user's database —
            // assume healthy and let the first real query surface any genuine problem.
            LogService.Error("VerifyIntegrityAsync failed unexpectedly — assuming healthy to avoid destructive recovery", ex);
            return true;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Handles database corruption: closes the connection, renames the corrupt file to .corrupt,
    /// then recreates a fresh database with the full schema.
    /// </summary>
    public static async Task HandleCorruptionAsync(string dbPath)
    {
        LogService.Error("Database corruption detected — backing up and recreating");

        // Release ALL open handles to the file before moving it. The raw connection AND the pooled
        // EF connections (opened by EnsureSchemaAsync before the integrity check) hold the file open.
        // Microsoft.Data.Sqlite pools physical connections, so nulling the factory is not enough —
        // the pool must be cleared, or File.Move throws a sharing violation and recovery silently fails.
        _contextFactory = null;
        if (_rawConnection is not null)
        {
            await _rawConnection.DisposeAsync().ConfigureAwait(false);
            _rawConnection = null;
        }
        SqliteConnection.ClearAllPools();

        string corruptPath = dbPath + ".corrupt";
        MoveOrDeleteCorruptFile(dbPath, corruptPath);
        // The -wal and -shm sidecars belong to the corrupt DB. A stale WAL left next to the fresh
        // file would be replayed into it and re-corrupt it, so move them out of the way too.
        MoveOrDeleteCorruptFile(dbPath + "-wal", corruptPath + "-wal");
        MoveOrDeleteCorruptFile(dbPath + "-shm", corruptPath + "-shm");
        LogService.Warn("Corrupt database backed up to: {CorruptPath}", corruptPath);

        await OpenAsync(dbPath).ConfigureAwait(false);
        await EnsureSchemaAsync().ConfigureAwait(false);

        LogService.Info("Fresh database created after corruption recovery.");
    }

    /// <summary>Moves <paramref name="source"/> to <paramref name="dest"/> (replacing dest), if it exists.</summary>
    private static void MoveOrDeleteCorruptFile(string source, string dest)
    {
        if (!File.Exists(source)) return;
        if (File.Exists(dest)) File.Delete(dest);
        File.Move(source, dest);
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
    /// Executes a parameterized non-query SQL command. Serialized through the write lock.
    /// </summary>
    public static async Task ExecuteNonQueryAsync(string sql, params (string name, object value)[] parameters)
    {
        await AcquireWriteLockAsync().ConfigureAwait(false);
        try
        {
            await using var cmd = _rawConnection!.CreateCommand();
            cmd.CommandText = sql;
            foreach (var (name, value) in parameters)
                cmd.Parameters.AddWithValue(name, value);
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

        bool hasFilePath = await ColumnExistsAsync("notes", "file_path").ConfigureAwait(false);
        if (!hasFilePath)
        {
            await ExecuteNonQueryAsync("ALTER TABLE notes ADD COLUMN file_path TEXT;").ConfigureAwait(false);
            LogService.Info("Migration: added file_path column to notes");
        }

        // Index for the hot per-desktop note queries. EnsureCreated only adds indexes to a brand-new
        // database, so existing installs need this explicit, idempotent creation. Name matches the
        // model's HasDatabaseName so a fresh DB doesn't end up with two indexes on the same columns.
        await ExecuteNonQueryAsync(
            "CREATE INDEX IF NOT EXISTS idx_notes_desktop ON notes(desktop_guid, pinned, sort_order);")
            .ConfigureAwait(false);

        if (hasWindowState && hasFilePath)
        {
            LogService.Info("No pending column migrations.");
        }
    }

    /// <summary>
    /// Closes and disposes the database connection.
    /// </summary>
    public static async Task CloseAsync()
    {
        if (_contextFactory is not null)
        {
            // IDisposable not available on PooledDbContextFactory; null the reference
            _contextFactory = null;
        }

        if (_rawConnection is not null)
        {
            await _rawConnection.CloseAsync().ConfigureAwait(false);
            _rawConnection.Dispose();
            _rawConnection = null;
            LogService.Info("Database connection closed.");
        }
    }

    /// <summary>
    /// Creates the pooled context factory. Call after _connectionString is set.
    /// </summary>
    private static void InitializeContextPool()
    {
        var options = new DbContextOptionsBuilder<JoJotDbContext>()
            .UseSqlite(_connectionString)
            .AddInterceptors(new SqlitePragmaInterceptor())
            .Options;
        _contextFactory = new PooledDbContextFactory<JoJotDbContext>(options, poolSize: 4);
    }

    /// <summary>
    /// Warms up the EF Core model on a background thread so the first real query is fast.
    /// Call from startup after <see cref="OpenAsync"/> but before the first query.
    /// </summary>
    public static Task WarmupModelAsync()
    {
        if (_contextFactory is null) return Task.CompletedTask;
        return Task.Run(() =>
        {
            // Creating and disposing a context triggers model compilation + caches it for the pool.
            using var ctx = _contextFactory.CreateDbContext();
        });
    }

    // ─── Private helpers ────────────────────────────────────────────────

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
