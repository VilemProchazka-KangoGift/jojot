using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using JoJot.Data;

namespace JoJot.Services;

/// <summary>
/// Database infrastructure: connection lifecycle, schema, migrations, write lock, and the
/// dedicated DB worker thread. Domain operations live in NoteStore, SessionStore,
/// PreferenceStore, PendingMoveStore.
/// </summary>
/// <remarks>
/// Microsoft.Data.Sqlite has no real async I/O: every "Async" call completes synchronously, so an
/// <c>await</c> never leaves the calling thread. To keep the WPF dispatcher free, every public DB
/// entry point hops onto a single long-lived background thread ("JoJot-DB") via
/// <see cref="RunOnDbThreadAsync(Func{Task})"/>. Items run strictly in FIFO order, one at a time.
/// </remarks>
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

    /// <summary>
    /// Optional UI-thread probe (set by App at startup; null in tests). When it returns true,
    /// DB entry points log a one-time warning per caller so the log shows which code paths
    /// still run database work on the WPF dispatcher thread.
    /// </summary>
    internal static Func<bool>? IsUiThread;

    private static readonly HashSet<string> _uiThreadCallersWarned = new(StringComparer.Ordinal);
    private static readonly Lock _uiThreadWarnLock = new();

    /// <summary>Logs a warning the first time <paramref name="caller"/> touches the DB on the UI thread.</summary>
    private static void WarnIfUiThread(string caller)
    {
        if (IsUiThread?.Invoke() != true) return;

        bool firstTime;
        lock (_uiThreadWarnLock)
        {
            firstTime = _uiThreadCallersWarned.Add(caller);
        }
        if (firstTime)
        {
            LogService.Warn("DB access on UI thread from {Caller}", caller);
        }
    }

    // ─── DB worker thread ───────────────────────────────────────────────

    /// <summary>Name of the dedicated database thread.</summary>
    internal const string DbThreadName = "JoJot-DB";

    private static readonly TimeSpan WorkerStopTimeout = TimeSpan.FromSeconds(10);
    private static readonly Lock _workerLock = new();
    private static DbWorker? _worker;

    /// <summary>True only on the dedicated DB worker thread.</summary>
    [ThreadStatic]
    private static bool _isDbThread;

    /// <summary>
    /// True inside DB work even after a (rare) real yield moves its continuation to the thread
    /// pool. Flows with the worker's ExecutionContext, never back to callers: they resume on their
    /// own captured context via a RunContinuationsAsynchronously completion source.
    /// </summary>
    private static readonly AsyncLocal<bool> _inDbWork = new();

    private sealed class DbWorker(Channel<Func<Task>> queue, Thread thread)
    {
        public Channel<Func<Task>> Queue { get; } = queue;
        public Thread Thread { get; } = thread;
    }

    /// <summary>True when the current thread is the dedicated "JoJot-DB" worker thread.</summary>
    internal static bool IsOnDbThread => _isDbThread;

    /// <summary>Nested hops from DB work run inline — enqueueing would self-deadlock the worker.</summary>
    private static bool ShouldRunInline => _isDbThread || _inDbWork.Value;

    /// <summary>
    /// Runs <paramref name="work"/> on the DB worker thread and returns a task that completes with
    /// its outcome (result, exception, or cancellation). Called from DB work itself, runs inline.
    /// </summary>
    internal static Task RunOnDbThreadAsync(Func<Task> work)
    {
        if (ShouldRunInline) return work();

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Enqueue(async () =>
        {
            Task task;
            try
            {
                task = work();
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
                return;
            }
            await task.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            tcs.TrySetFromTask(task);
        });
        return tcs.Task;
    }

    /// <inheritdoc cref="RunOnDbThreadAsync(Func{Task})"/>
    internal static Task<T> RunOnDbThreadAsync<T>(Func<Task<T>> work)
    {
        if (ShouldRunInline) return work();

        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Enqueue(async () =>
        {
            Task<T> task;
            try
            {
                task = work();
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
                return;
            }
            await ((Task)task).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            tcs.TrySetFromTask(task);
        });
        return tcs.Task;
    }

    private static void Enqueue(Func<Task> item)
    {
        while (true)
        {
            var worker = EnsureWorker();
            if (worker.Queue.Writer.TryWrite(item)) return;

            // A concurrent StopWorker completed this queue — drop it and start a fresh worker.
            lock (_workerLock)
            {
                if (ReferenceEquals(_worker, worker)) _worker = null;
            }
        }
    }

    /// <summary>Starts the DB worker thread if it is not already running.</summary>
    private static DbWorker EnsureWorker()
    {
        lock (_workerLock)
        {
            if (_worker is not null) return _worker;

            var queue = Channel.CreateUnbounded<Func<Task>>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
            });
            var thread = new Thread(WorkerLoop)
            {
                Name = DbThreadName,
                IsBackground = true,
            };
            _worker = new DbWorker(queue, thread);
            thread.Start(queue);
            return _worker;
        }
    }

    /// <summary>Completes the queue (already-queued items still drain) and joins the worker thread.</summary>
    private static void StopWorker()
    {
        DbWorker? worker;
        lock (_workerLock)
        {
            worker = _worker;
            _worker = null;
        }
        if (worker is null) return;

        worker.Queue.Writer.TryComplete();
        if (Thread.CurrentThread != worker.Thread && !worker.Thread.Join(WorkerStopTimeout))
        {
            LogService.Warn("DB worker thread did not stop within {TimeoutSeconds}s", WorkerStopTimeout.TotalSeconds);
        }
    }

    private static void WorkerLoop(object? state)
    {
        var reader = ((Channel<Func<Task>>)state!).Reader;
        _isDbThread = true;
        _inDbWork.Value = true;

        // Blocking waits are deliberate: this thread has no synchronization context, and each item
        // must finish before the next starts (FIFO, one at a time). Items capture their own outcome.
        while (reader.WaitToReadAsync().AsTask().GetAwaiter().GetResult())
        {
            while (reader.TryRead(out var item))
            {
                try
                {
                    item().GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    LogService.Error("DB worker item failed unexpectedly", ex);
                }
            }
        }
    }

    // ─── Write lock / contexts ──────────────────────────────────────────

    internal static async Task AcquireWriteLockAsync(CancellationToken ct = default, [CallerMemberName] string caller = "")
    {
        WarnIfUiThread(caller);

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

    internal static JoJotDbContext CreateContext([CallerMemberName] string caller = "")
    {
        WarnIfUiThread(caller);
        return _contextFactory?.CreateDbContext() ?? new JoJotDbContext(_connectionString);
    }

    /// <summary>The current connection string. Exposed for test verification only.</summary>
    internal static string ConnectionString => _connectionString;

    /// <summary>Creates a DbContext using the current connection string. For integration tests.</summary>
    internal static JoJotDbContext CreateTestContext() => CreateContext();

    // ─── Open / close ───────────────────────────────────────────────────

    /// <summary>
    /// Opens (or creates) the SQLite database at the specified path.
    /// Enables WAL journal mode, NORMAL synchronous, and foreign keys.
    /// Starts the DB worker thread first; the open itself runs on it.
    /// </summary>
    public static Task OpenAsync(string dbPath)
    {
        EnsureWorker();
        return RunOnDbThreadAsync(() => OpenCoreAsync(dbPath));
    }

    private static async Task OpenCoreAsync(string dbPath)
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

        await ExecuteRawPragmaAsync(
            "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA foreign_keys=ON;")
            .ConfigureAwait(false);

        InitializeContextPool();

        LogService.Info("Database opened: {DbPath}", dbPath);
    }

    /// <summary>
    /// Opens the database using a pre-built connection string (no file path processing).
    /// For integration tests with in-memory SQLite.
    /// </summary>
    internal static Task OpenWithConnectionStringAsync(string connectionString)
    {
        EnsureWorker();
        return RunOnDbThreadAsync(() => OpenWithConnectionStringCoreAsync(connectionString));
    }

    private static async Task OpenWithConnectionStringCoreAsync(string connectionString)
    {
        _dbPath = ":memory:";
        _connectionString = connectionString;

        _rawConnection = new SqliteConnection(_connectionString);
        await _rawConnection.OpenAsync().ConfigureAwait(false);

        await ExecuteRawPragmaAsync("PRAGMA foreign_keys=ON;").ConfigureAwait(false);

        InitializeContextPool();
    }

    /// <summary>
    /// Closes and disposes the database connection, then stops the DB worker thread (work queued
    /// before the close still runs first). Safe to call repeatedly, and safe to block on from the
    /// UI thread: nothing on the worker ever marshals back to the dispatcher.
    /// </summary>
    public static async Task CloseAsync()
    {
        await RunOnDbThreadAsync(CloseCoreAsync).ConfigureAwait(false);

        // Called from DB work itself, the worker cannot join itself; it keeps serving the queue.
        if (!ShouldRunInline)
        {
            StopWorker();
        }
    }

    private static async Task CloseCoreAsync()
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

    // ─── Schema ─────────────────────────────────────────────────────────

    /// <summary>
    /// Current schema version stamped into PRAGMA user_version after EnsureCreated + migrations.
    /// v1 = base tables + window_state + file_path columns + idx_notes_desktop index.
    /// Increment when adding any schema change, and guard the new migration on the old version.
    /// </summary>
    internal const int CurrentSchemaVersion = 1;

    /// <summary>Reads PRAGMA user_version (0 on a freshly created database).</summary>
    internal static Task<int> GetUserVersionAsync() => RunOnDbThreadAsync(GetUserVersionCoreAsync);

    private static async Task<int> GetUserVersionCoreAsync()
    {
        await using var cmd = _rawConnection!.CreateCommand();
        cmd.CommandText = "PRAGMA user_version;";
        var result = await cmd.ExecuteScalarAsync().ConfigureAwait(false);
        return Convert.ToInt32(result);
    }

    private static async Task SetUserVersionCoreAsync(int version)
    {
        // PRAGMA does not support parameters; version is a compile-time constant at call sites.
        await ExecuteRawPragmaAsync($"PRAGMA user_version = {version};").ConfigureAwait(false);
    }

    /// <summary>
    /// Creates all schema tables if they do not already exist. Returns false without touching
    /// EF when the schema stamp is current (the ~450ms EnsureCreated fast path).
    /// </summary>
    public static Task<bool> EnsureSchemaAsync() => RunOnDbThreadAsync(EnsureSchemaCoreAsync);

    private static async Task<bool> EnsureSchemaCoreAsync()
    {
        if (await GetUserVersionCoreAsync().ConfigureAwait(false) == CurrentSchemaVersion)
        {
            LogService.Info("Schema is current (v{Version}) — skipping EnsureCreated", CurrentSchemaVersion);
            return false;
        }

        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync().ConfigureAwait(false);
        LogService.Info("Schema ensured (all four tables).");
        return true;
    }

    /// <summary>
    /// Checks that all expected tables exist in sqlite_master.
    /// Returns true if the database is healthy; false if corrupt or tables are missing.
    /// </summary>
    public static Task<bool> VerifyIntegrityAsync(CancellationToken ct = default) =>
        RunOnDbThreadAsync(() => VerifyIntegrityCoreAsync(ct));

    private static async Task<bool> VerifyIntegrityCoreAsync(CancellationToken ct)
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
    public static Task HandleCorruptionAsync(string dbPath) =>
        RunOnDbThreadAsync(() => HandleCorruptionCoreAsync(dbPath));

    private static async Task HandleCorruptionCoreAsync(string dbPath)
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

        await OpenCoreAsync(dbPath).ConfigureAwait(false);
        await EnsureSchemaCoreAsync().ConfigureAwait(false);

        LogService.Info("Fresh database created after corruption recovery.");
    }

    /// <summary>Moves <paramref name="source"/> to <paramref name="dest"/> (replacing dest), if it exists.</summary>
    private static void MoveOrDeleteCorruptFile(string source, string dest)
    {
        if (!File.Exists(source)) return;
        if (File.Exists(dest)) File.Delete(dest);
        File.Move(source, dest);
    }

    // ─── Raw SQL helpers ────────────────────────────────────────────────

    /// <summary>
    /// Executes a non-query SQL command. Serialized through the write lock.
    /// </summary>
    public static Task ExecuteNonQueryAsync(string sql) =>
        RunOnDbThreadAsync(() => ExecuteNonQueryCoreAsync(sql, []));

    /// <summary>
    /// Executes a parameterized non-query SQL command. Serialized through the write lock.
    /// </summary>
    public static Task ExecuteNonQueryAsync(string sql, params (string name, object value)[] parameters) =>
        RunOnDbThreadAsync(() => ExecuteNonQueryCoreAsync(sql, parameters));

    private static async Task ExecuteNonQueryCoreAsync(string sql, (string name, object value)[] parameters)
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
    public static Task<T> ExecuteScalarAsync<T>(string sql) =>
        RunOnDbThreadAsync(() => ExecuteScalarCoreAsync<T>(sql));

    private static async Task<T> ExecuteScalarCoreAsync<T>(string sql)
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

    // ─── Migrations ─────────────────────────────────────────────────────

    /// <summary>
    /// Runs pending schema migrations. Idempotent — safe to run on every launch.
    /// </summary>
    public static Task RunPendingMigrationsAsync() => RunOnDbThreadAsync(RunPendingMigrationsCoreAsync);

    private static async Task RunPendingMigrationsCoreAsync()
    {
        if (await GetUserVersionCoreAsync().ConfigureAwait(false) == CurrentSchemaVersion)
        {
            LogService.Info("Schema stamp current (v{Version}) — skipping migration probes", CurrentSchemaVersion);
            return;
        }

        bool hasWindowState = await ColumnExistsCoreAsync("app_state", "window_state").ConfigureAwait(false);
        if (!hasWindowState)
        {
            await ExecuteNonQueryCoreAsync("ALTER TABLE app_state ADD COLUMN window_state TEXT;", []).ConfigureAwait(false);
            LogService.Info("Migration: added window_state column to app_state");
        }

        bool hasFilePath = await ColumnExistsCoreAsync("notes", "file_path").ConfigureAwait(false);
        if (!hasFilePath)
        {
            await ExecuteNonQueryCoreAsync("ALTER TABLE notes ADD COLUMN file_path TEXT;", []).ConfigureAwait(false);
            LogService.Info("Migration: added file_path column to notes");
        }

        // Index for the hot per-desktop note queries. EnsureCreated only adds indexes to a brand-new
        // database, so existing installs need this explicit, idempotent creation. Name matches the
        // model's HasDatabaseName so a fresh DB doesn't end up with two indexes on the same columns.
        await ExecuteNonQueryCoreAsync(
            "CREATE INDEX IF NOT EXISTS idx_notes_desktop ON notes(desktop_guid, pinned, sort_order);", [])
            .ConfigureAwait(false);

        if (hasWindowState && hasFilePath)
        {
            LogService.Info("No pending column migrations.");
        }

        await SetUserVersionCoreAsync(CurrentSchemaVersion).ConfigureAwait(false);
        LogService.Info("Schema stamped at v{Version}", CurrentSchemaVersion);
    }

    /// <summary>
    /// Creates the pooled context factory. Call after _connectionString is set.
    /// </summary>
    private static void InitializeContextPool()
    {
        var options = new DbContextOptionsBuilder<JoJotDbContext>()
            .UseSqlite(_connectionString)
            .UseModel(JoJot.Data.CompiledModels.JoJotDbContextModel.Instance)
            .AddInterceptors(new SqlitePragmaInterceptor())
            .Options;
        _contextFactory = new PooledDbContextFactory<JoJotDbContext>(options, poolSize: 4);
    }

    /// <summary>
    /// Warms up the EF Core model on the DB worker thread so the first real query is fast.
    /// Call from startup after <see cref="OpenAsync"/> but before the first query.
    /// </summary>
    public static Task WarmupModelAsync() => RunOnDbThreadAsync(() =>
    {
        // Creating and disposing a context triggers model compilation + caches it for the pool.
        using var ctx = _contextFactory?.CreateDbContext();
        return Task.CompletedTask;
    });

    // ─── Private helpers ────────────────────────────────────────────────

    private static async Task<bool> ColumnExistsCoreAsync(string table, string column)
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

    private static async Task ExecuteRawPragmaAsync(string sql, [CallerMemberName] string caller = "")
    {
        WarnIfUiThread(caller);
        await using var cmd = _rawConnection!.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }
}
