using Microsoft.Data.Sqlite;
using System.IO;
using JoJot.Models;

namespace JoJot.Services
{
    /// <summary>
    /// Manages the single SQLite connection for the JoJot process lifetime.
    /// WAL mode and NORMAL synchronous are enabled on every open.
    /// All writes are serialized through a SemaphoreSlim(1,1) to satisfy DATA-02.
    /// </summary>
    public static class DatabaseService
    {
        private static SqliteConnection? _connection;
        private static readonly SemaphoreSlim _writeLock = new(1, 1);
        private static string _dbPath = string.Empty;

        /// <summary>
        /// Opens (or creates) the SQLite database at the specified path.
        /// Creates the directory if it does not exist.
        /// Enables WAL journal mode, NORMAL synchronous, and foreign keys.
        /// </summary>
        /// <param name="dbPath">Full path to the .db file (e.g. AppData\Local\JoJot\jojot.db).</param>
        public static async Task OpenAsync(string dbPath)
        {
            _dbPath = dbPath;
            Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

            var connStr = new SqliteConnectionStringBuilder
            {
                DataSource = dbPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                // Do NOT set Cache = Shared when using WAL mode (official docs warning)
            }.ToString();

            _connection = new SqliteConnection(connStr);
            await _connection.OpenAsync();

            // WAL mode persists once set; safe to set every open (idempotent)
            await ExecuteNonQueryAsync("PRAGMA journal_mode=WAL;");
            await ExecuteNonQueryAsync("PRAGMA synchronous=NORMAL;");
            await ExecuteNonQueryAsync("PRAGMA foreign_keys=ON;");

            LogService.Info($"Database opened: {dbPath}");
        }

        /// <summary>
        /// Creates all four schema tables if they do not already exist.
        /// Idempotent — safe to call on every launch (uses CREATE TABLE IF NOT EXISTS).
        /// </summary>
        public static async Task EnsureSchemaAsync()
        {
            await ExecuteNonQueryAsync("""
                CREATE TABLE IF NOT EXISTS notes (
                    id                   INTEGER PRIMARY KEY AUTOINCREMENT,
                    desktop_guid         TEXT    NOT NULL,
                    name                 TEXT,
                    content              TEXT    NOT NULL DEFAULT '',
                    pinned               INTEGER NOT NULL DEFAULT 0,
                    created_at           TEXT    NOT NULL DEFAULT (datetime('now')),
                    updated_at           TEXT    NOT NULL DEFAULT (datetime('now')),
                    sort_order           INTEGER NOT NULL DEFAULT 0,
                    editor_scroll_offset INTEGER NOT NULL DEFAULT 0,
                    cursor_position      INTEGER NOT NULL DEFAULT 0
                );
                """);

            await ExecuteNonQueryAsync("""
                CREATE TABLE IF NOT EXISTS app_state (
                    id            INTEGER PRIMARY KEY AUTOINCREMENT,
                    desktop_guid  TEXT    NOT NULL UNIQUE,
                    desktop_name  TEXT,
                    desktop_index INTEGER,
                    window_left   REAL,
                    window_top    REAL,
                    window_width  REAL,
                    window_height REAL,
                    active_tab_id INTEGER,
                    scroll_offset REAL
                );
                """);

            await ExecuteNonQueryAsync("""
                CREATE TABLE IF NOT EXISTS pending_moves (
                    id           INTEGER PRIMARY KEY AUTOINCREMENT,
                    window_id    TEXT    NOT NULL,
                    from_desktop TEXT    NOT NULL,
                    to_desktop   TEXT,
                    detected_at  TEXT    NOT NULL DEFAULT (datetime('now'))
                );
                """);

            await ExecuteNonQueryAsync("""
                CREATE TABLE IF NOT EXISTS preferences (
                    key   TEXT PRIMARY KEY,
                    value TEXT NOT NULL
                );
                """);

            LogService.Info("Schema ensured (all four tables).");
        }

        /// <summary>
        /// Checks that all four expected tables exist in sqlite_master.
        /// If any table is missing, also runs PRAGMA quick_check.
        /// Returns true if the database is healthy; false if corrupt or tables are missing.
        /// </summary>
        public static async Task<bool> VerifyIntegrityAsync()
        {
            var required = new[] { "notes", "app_state", "pending_moves", "preferences" };
            foreach (var table in required)
            {
                long count = await ExecuteScalarAsync<long>(
                    $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{table}';");
                if (count == 0)
                {
                    LogService.Warn($"Expected table '{table}' is missing — running PRAGMA quick_check");
                    return await RunQuickCheckAsync();
                }
            }
            return true;
        }

        /// <summary>
        /// Handles database corruption: closes the connection, renames the corrupt file to .corrupt,
        /// then recreates a fresh database with the full schema.
        /// </summary>
        /// <param name="dbPath">Path of the corrupt database file.</param>
        public static async Task HandleCorruptionAsync(string dbPath)
        {
            LogService.Error("Database corruption detected — backing up and recreating");

            // Close and dispose the existing connection
            _connection?.Dispose();
            _connection = null;

            string corruptPath = dbPath + ".corrupt";
            if (File.Exists(corruptPath))
                File.Delete(corruptPath);

            File.Move(dbPath, corruptPath);
            LogService.Warn($"Corrupt database backed up to: {corruptPath}");

            // Recreate a fresh database
            await OpenAsync(dbPath);
            await EnsureSchemaAsync();

            LogService.Info("Fresh database created after corruption recovery.");
        }

        /// <summary>
        /// Executes a non-query SQL command (INSERT, UPDATE, DELETE, CREATE, PRAGMA, etc.).
        /// All writes are serialized via a SemaphoreSlim(1,1).
        /// </summary>
        /// <param name="sql">The SQL command text.</param>
        public static async Task ExecuteNonQueryAsync(string sql)
        {
            await _writeLock.WaitAsync();
            try
            {
                var cmd = _connection!.CreateCommand();
                cmd.CommandText = sql;
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                LogService.Error($"ExecuteNonQueryAsync failed: {sql}", ex);
                throw;
            }
            finally
            {
                _writeLock.Release();
            }
        }

        /// <summary>
        /// Executes a scalar SQL query and returns the first column of the first row.
        /// Uses the write lock for consistency (all operations go through the same serialization).
        /// </summary>
        /// <typeparam name="T">Expected return type.</typeparam>
        /// <param name="sql">The SQL query text.</param>
        /// <returns>The scalar result cast to <typeparamref name="T"/>.</returns>
        public static async Task<T> ExecuteScalarAsync<T>(string sql)
        {
            await _writeLock.WaitAsync();
            try
            {
                var cmd = _connection!.CreateCommand();
                cmd.CommandText = sql;
                var result = await cmd.ExecuteScalarAsync();
                return (T)Convert.ChangeType(result!, typeof(T));
            }
            catch (Exception ex)
            {
                LogService.Error($"ExecuteScalarAsync failed: {sql}", ex);
                throw;
            }
            finally
            {
                _writeLock.Release();
            }
        }

        /// <summary>
        /// Executes a SQL query and passes the resulting SqliteDataReader to the provided handler.
        /// Uses the write lock so reads don't interleave with in-progress writes.
        /// </summary>
        /// <param name="sql">The SQL query text.</param>
        /// <param name="handler">Delegate invoked with the open reader.</param>
        public static async Task ExecuteReaderAsync(string sql, Action<SqliteDataReader> handler)
        {
            await _writeLock.WaitAsync();
            try
            {
                var cmd = _connection!.CreateCommand();
                cmd.CommandText = sql;
                using var reader = await cmd.ExecuteReaderAsync();
                handler(reader);
            }
            catch (Exception ex)
            {
                LogService.Error($"ExecuteReaderAsync failed: {sql}", ex);
                throw;
            }
            finally
            {
                _writeLock.Release();
            }
        }

        /// <summary>
        /// Runs pending schema migrations in the background.
        /// Migrations are idempotent — safe to run on every launch.
        /// </summary>
        public static async Task RunPendingMigrationsAsync()
        {
            // Migration 1 (Phase 3): add window_state column to app_state
            bool hasWindowState = await ColumnExistsAsync("app_state", "window_state");
            if (!hasWindowState)
            {
                await ExecuteNonQueryAsync("ALTER TABLE app_state ADD COLUMN window_state TEXT;");
                LogService.Info("Migration: added window_state column to app_state");
            }
            else
            {
                LogService.Info("No pending migrations.");
            }
        }

        /// <summary>
        /// Closes and disposes the database connection.
        /// Should be called on application shutdown.
        /// </summary>
        public static async Task CloseAsync()
        {
            if (_connection is not null)
            {
                await _connection.CloseAsync();
                _connection.Dispose();
                _connection = null;
                LogService.Info("Database connection closed.");
            }
        }

        // ─── Session CRUD (Phase 2: VDSK-03) ─────────────────────────────────────

        /// <summary>
        /// Returns all desktop sessions from app_state.
        /// Used by VirtualDesktopService.MatchSessionsAsync for three-tier matching.
        /// </summary>
        public static async Task<List<(string DesktopGuid, string? DesktopName, int? DesktopIndex)>> GetAllSessionsAsync()
        {
            var sessions = new List<(string, string?, int?)>();

            await _writeLock.WaitAsync();
            try
            {
                var cmd = _connection!.CreateCommand();
                cmd.CommandText = "SELECT desktop_guid, desktop_name, desktop_index FROM app_state;";
                using var reader = await cmd.ExecuteReaderAsync();
                while (reader.Read())
                {
                    string guid = reader.GetString(0);
                    string? name = reader.IsDBNull(1) ? null : reader.GetString(1);
                    int? index = reader.IsDBNull(2) ? null : reader.GetInt32(2);
                    sessions.Add((guid, name, index));
                }
            }
            catch (Exception ex)
            {
                LogService.Error("GetAllSessionsAsync failed", ex);
                throw;
            }
            finally
            {
                _writeLock.Release();
            }

            return sessions;
        }

        /// <summary>
        /// Updates a session's desktop identity after successful matching.
        /// Updates both app_state and notes tables to keep the FK consistent.
        /// </summary>
        public static async Task UpdateSessionAsync(string oldGuid, string newGuid, string? name, int? index)
        {
            await _writeLock.WaitAsync();
            try
            {
                // Update app_state
                var cmd = _connection!.CreateCommand();
                cmd.CommandText = "UPDATE app_state SET desktop_guid=@newGuid, desktop_name=@name, desktop_index=@index WHERE desktop_guid=@oldGuid;";
                cmd.Parameters.AddWithValue("@newGuid", newGuid);
                cmd.Parameters.AddWithValue("@name", (object?)name ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@index", (object?)index ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@oldGuid", oldGuid);
                await cmd.ExecuteNonQueryAsync();

                // Update notes FK to stay consistent
                if (oldGuid != newGuid)
                {
                    var notesCmd = _connection.CreateCommand();
                    notesCmd.CommandText = "UPDATE notes SET desktop_guid=@newGuid WHERE desktop_guid=@oldGuid;";
                    notesCmd.Parameters.AddWithValue("@newGuid", newGuid);
                    notesCmd.Parameters.AddWithValue("@oldGuid", oldGuid);
                    await notesCmd.ExecuteNonQueryAsync();
                }
            }
            catch (Exception ex)
            {
                LogService.Error($"UpdateSessionAsync failed (old={oldGuid}, new={newGuid})", ex);
                throw;
            }
            finally
            {
                _writeLock.Release();
            }
        }

        /// <summary>
        /// Creates a new desktop session in app_state.
        /// Uses INSERT OR IGNORE — silently skips if a session already exists for this GUID.
        /// </summary>
        public static async Task CreateSessionAsync(string desktopGuid, string? desktopName, int? desktopIndex)
        {
            await _writeLock.WaitAsync();
            try
            {
                var cmd = _connection!.CreateCommand();
                cmd.CommandText = "INSERT OR IGNORE INTO app_state (desktop_guid, desktop_name, desktop_index) VALUES (@guid, @name, @index);";
                cmd.Parameters.AddWithValue("@guid", desktopGuid);
                cmd.Parameters.AddWithValue("@name", (object?)desktopName ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@index", (object?)desktopIndex ?? DBNull.Value);
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                LogService.Error($"CreateSessionAsync failed (guid={desktopGuid})", ex);
                throw;
            }
            finally
            {
                _writeLock.Release();
            }
        }

        /// <summary>
        /// Updates the desktop_name for a session identified by its GUID.
        /// Used by notification callbacks when a desktop is renamed.
        /// </summary>
        public static async Task UpdateDesktopNameAsync(string desktopGuid, string newName)
        {
            await _writeLock.WaitAsync();
            try
            {
                var cmd = _connection!.CreateCommand();
                cmd.CommandText = "UPDATE app_state SET desktop_name=@name WHERE desktop_guid=@guid;";
                cmd.Parameters.AddWithValue("@name", newName);
                cmd.Parameters.AddWithValue("@guid", desktopGuid);
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                LogService.Error($"UpdateDesktopNameAsync failed (guid={desktopGuid})", ex);
                throw;
            }
            finally
            {
                _writeLock.Release();
            }
        }

        // ─── Window Geometry (Phase 3: TASK-04) ─────────────────────────────────────

        /// <summary>
        /// Reads the saved window geometry for a desktop session.
        /// Returns null if no geometry is saved (window_left is null) or the desktop has no app_state row.
        /// </summary>
        public static async Task<WindowGeometry?> GetWindowGeometryAsync(string desktopGuid)
        {
            await _writeLock.WaitAsync();
            try
            {
                var cmd = _connection!.CreateCommand();
                cmd.CommandText = "SELECT window_left, window_top, window_width, window_height, window_state FROM app_state WHERE desktop_guid = @guid;";
                cmd.Parameters.AddWithValue("@guid", desktopGuid);
                using var reader = await cmd.ExecuteReaderAsync();
                if (reader.Read() && !reader.IsDBNull(0))
                {
                    double left = reader.GetDouble(0);
                    double top = reader.GetDouble(1);
                    double width = reader.GetDouble(2);
                    double height = reader.GetDouble(3);
                    bool isMaximized = !reader.IsDBNull(4) && reader.GetString(4) == "Maximized";
                    return new WindowGeometry(left, top, width, height, isMaximized);
                }
                return null;
            }
            catch (Exception ex)
            {
                LogService.Error($"GetWindowGeometryAsync failed (guid={desktopGuid})", ex);
                throw;
            }
            finally
            {
                _writeLock.Release();
            }
        }

        /// <summary>
        /// Persists window geometry for a desktop session.
        /// Updates the existing app_state row — the row must already exist (created by session matching).
        /// </summary>
        public static async Task SaveWindowGeometryAsync(string desktopGuid, WindowGeometry geo)
        {
            await _writeLock.WaitAsync();
            try
            {
                var cmd = _connection!.CreateCommand();
                cmd.CommandText = "UPDATE app_state SET window_left=@l, window_top=@t, window_width=@w, window_height=@h, window_state=@s WHERE desktop_guid=@guid;";
                cmd.Parameters.AddWithValue("@l", geo.Left);
                cmd.Parameters.AddWithValue("@t", geo.Top);
                cmd.Parameters.AddWithValue("@w", geo.Width);
                cmd.Parameters.AddWithValue("@h", geo.Height);
                cmd.Parameters.AddWithValue("@s", geo.IsMaximized ? "Maximized" : "Normal");
                cmd.Parameters.AddWithValue("@guid", desktopGuid);
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                LogService.Error($"SaveWindowGeometryAsync failed (guid={desktopGuid})", ex);
                throw;
            }
            finally
            {
                _writeLock.Release();
            }
        }

        // ─── Notes CRUD (Phase 4: TABS-*) ──────────────────────────────────────────

        /// <summary>
        /// Loads all notes for a desktop, ordered by pinned DESC then sort_order ASC.
        /// Returns an empty list if no notes exist for the desktop.
        /// </summary>
        public static async Task<List<NoteTab>> GetNotesForDesktopAsync(string desktopGuid)
        {
            var notes = new List<NoteTab>();
            await _writeLock.WaitAsync();
            try
            {
                var cmd = _connection!.CreateCommand();
                cmd.CommandText = @"
                    SELECT id, desktop_guid, name, content, pinned, created_at, updated_at,
                           sort_order, editor_scroll_offset, cursor_position
                    FROM notes
                    WHERE desktop_guid = @guid
                    ORDER BY pinned DESC, sort_order ASC;";
                cmd.Parameters.AddWithValue("@guid", desktopGuid);
                using var reader = await cmd.ExecuteReaderAsync();
                while (reader.Read())
                {
                    notes.Add(new NoteTab
                    {
                        Id = reader.GetInt64(0),
                        DesktopGuid = reader.GetString(1),
                        Name = reader.IsDBNull(2) ? null : reader.GetString(2),
                        Content = reader.GetString(3),
                        Pinned = reader.GetInt32(4) != 0,
                        CreatedAt = DateTime.Parse(reader.GetString(5)),
                        UpdatedAt = DateTime.Parse(reader.GetString(6)),
                        SortOrder = reader.GetInt32(7),
                        EditorScrollOffset = reader.GetInt32(8),
                        CursorPosition = reader.GetInt32(9)
                    });
                }
            }
            catch (Exception ex)
            {
                LogService.Error($"GetNotesForDesktopAsync failed (guid={desktopGuid})", ex);
                throw;
            }
            finally
            {
                _writeLock.Release();
            }
            return notes;
        }

        /// <summary>
        /// Inserts a new note and returns its auto-generated ID.
        /// </summary>
        public static async Task<long> InsertNoteAsync(string desktopGuid, string? name, string content, bool pinned, int sortOrder)
        {
            await _writeLock.WaitAsync();
            try
            {
                var cmd = _connection!.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO notes (desktop_guid, name, content, pinned, sort_order)
                    VALUES (@guid, @name, @content, @pinned, @sortOrder);
                    SELECT last_insert_rowid();";
                cmd.Parameters.AddWithValue("@guid", desktopGuid);
                cmd.Parameters.AddWithValue("@name", (object?)name ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@content", content);
                cmd.Parameters.AddWithValue("@pinned", pinned ? 1 : 0);
                cmd.Parameters.AddWithValue("@sortOrder", sortOrder);
                var result = await cmd.ExecuteScalarAsync();
                return (long)result!;
            }
            catch (Exception ex)
            {
                LogService.Error($"InsertNoteAsync failed (guid={desktopGuid})", ex);
                throw;
            }
            finally
            {
                _writeLock.Release();
            }
        }

        /// <summary>
        /// Updates a note's content and sets updated_at to now.
        /// </summary>
        public static async Task UpdateNoteContentAsync(long noteId, string content)
        {
            await _writeLock.WaitAsync();
            try
            {
                var cmd = _connection!.CreateCommand();
                cmd.CommandText = "UPDATE notes SET content = @content, updated_at = datetime('now') WHERE id = @id;";
                cmd.Parameters.AddWithValue("@content", content);
                cmd.Parameters.AddWithValue("@id", noteId);
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                LogService.Error($"UpdateNoteContentAsync failed (id={noteId})", ex);
                throw;
            }
            finally
            {
                _writeLock.Release();
            }
        }

        /// <summary>
        /// Updates a note's custom name. Pass null to clear the name (reverts to content fallback).
        /// </summary>
        public static async Task UpdateNoteNameAsync(long noteId, string? name)
        {
            await _writeLock.WaitAsync();
            try
            {
                var cmd = _connection!.CreateCommand();
                cmd.CommandText = "UPDATE notes SET name = @name, updated_at = datetime('now') WHERE id = @id;";
                cmd.Parameters.AddWithValue("@name", (object?)name ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@id", noteId);
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                LogService.Error($"UpdateNoteNameAsync failed (id={noteId})", ex);
                throw;
            }
            finally
            {
                _writeLock.Release();
            }
        }

        /// <summary>
        /// Updates a note's pinned status.
        /// </summary>
        public static async Task UpdateNotePinnedAsync(long noteId, bool pinned)
        {
            await _writeLock.WaitAsync();
            try
            {
                var cmd = _connection!.CreateCommand();
                cmd.CommandText = "UPDATE notes SET pinned = @pinned, updated_at = datetime('now') WHERE id = @id;";
                cmd.Parameters.AddWithValue("@pinned", pinned ? 1 : 0);
                cmd.Parameters.AddWithValue("@id", noteId);
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                LogService.Error($"UpdateNotePinnedAsync failed (id={noteId})", ex);
                throw;
            }
            finally
            {
                _writeLock.Release();
            }
        }

        /// <summary>
        /// Batch-updates sort_order for multiple notes. All updates execute under a single lock acquisition.
        /// </summary>
        public static async Task UpdateNoteSortOrdersAsync(IEnumerable<(long Id, int SortOrder)> updates)
        {
            await _writeLock.WaitAsync();
            try
            {
                foreach (var (id, sortOrder) in updates)
                {
                    var cmd = _connection!.CreateCommand();
                    cmd.CommandText = "UPDATE notes SET sort_order = @sortOrder WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@sortOrder", sortOrder);
                    cmd.Parameters.AddWithValue("@id", id);
                    await cmd.ExecuteNonQueryAsync();
                }
            }
            catch (Exception ex)
            {
                LogService.Error("UpdateNoteSortOrdersAsync failed", ex);
                throw;
            }
            finally
            {
                _writeLock.Release();
            }
        }

        /// <summary>
        /// Deletes a note by ID.
        /// </summary>
        public static async Task DeleteNoteAsync(long noteId)
        {
            await _writeLock.WaitAsync();
            try
            {
                var cmd = _connection!.CreateCommand();
                cmd.CommandText = "DELETE FROM notes WHERE id = @id;";
                cmd.Parameters.AddWithValue("@id", noteId);
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                LogService.Error($"DeleteNoteAsync failed (id={noteId})", ex);
                throw;
            }
            finally
            {
                _writeLock.Release();
            }
        }

        /// <summary>
        /// Returns the maximum sort_order for notes in a desktop, or -1 if no notes exist.
        /// Callers use maxSort + 1 for new note insertion.
        /// </summary>
        public static async Task<int> GetMaxSortOrderAsync(string desktopGuid)
        {
            await _writeLock.WaitAsync();
            try
            {
                var cmd = _connection!.CreateCommand();
                cmd.CommandText = "SELECT COALESCE(MAX(sort_order), -1) FROM notes WHERE desktop_guid = @guid;";
                cmd.Parameters.AddWithValue("@guid", desktopGuid);
                var result = await cmd.ExecuteScalarAsync();
                return Convert.ToInt32(result);
            }
            catch (Exception ex)
            {
                LogService.Error($"GetMaxSortOrderAsync failed (guid={desktopGuid})", ex);
                throw;
            }
            finally
            {
                _writeLock.Release();
            }
        }

        // ─── Private helpers ────────────────────────────────────────────────────

        /// <summary>
        /// Runs PRAGMA quick_check and returns true only if the result is "ok".
        /// This is O(N) vs integrity_check O(NlogN) — sufficient for startup health verification.
        /// </summary>
        private static async Task<bool> RunQuickCheckAsync()
        {
            try
            {
                // NOTE: quick_check is a read — we still acquire the lock for consistency
                await _writeLock.WaitAsync();
                try
                {
                    var cmd = _connection!.CreateCommand();
                    cmd.CommandText = "PRAGMA quick_check;";
                    using var reader = await cmd.ExecuteReaderAsync();
                    if (reader.Read())
                    {
                        string result = reader.GetString(0);
                        bool ok = string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase);
                        if (!ok)
                            LogService.Error($"PRAGMA quick_check returned: {result}");
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
        // ─── Preferences (Phase 7: Theming) ────────────────────────────────

        /// <summary>
        /// Gets a preference value by key. Returns null if the key doesn't exist.
        /// </summary>
        public static async Task<string?> GetPreferenceAsync(string key)
        {
            await _writeLock.WaitAsync();
            try
            {
                var cmd = _connection!.CreateCommand();
                cmd.CommandText = "SELECT value FROM preferences WHERE key = @key;";
                cmd.Parameters.AddWithValue("@key", key);
                var result = await cmd.ExecuteScalarAsync();
                return result as string;
            }
            catch (Exception ex)
            {
                LogService.Error($"GetPreferenceAsync failed for key: {key}", ex);
                return null;
            }
            finally
            {
                _writeLock.Release();
            }
        }

        /// <summary>
        /// Upserts a preference value. Creates the key if it doesn't exist,
        /// updates the value if it does.
        /// </summary>
        public static async Task SetPreferenceAsync(string key, string value)
        {
            await _writeLock.WaitAsync();
            try
            {
                var cmd = _connection!.CreateCommand();
                cmd.CommandText = "INSERT INTO preferences (key, value) VALUES (@key, @value) ON CONFLICT(key) DO UPDATE SET value = @value;";
                cmd.Parameters.AddWithValue("@key", key);
                cmd.Parameters.AddWithValue("@value", value);
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                LogService.Error($"SetPreferenceAsync failed for key: {key}", ex);
                throw;
            }
            finally
            {
                _writeLock.Release();
            }
        }

        /// <summary>
        /// Checks whether a column exists in a table via PRAGMA table_info.
        /// SQLite does not support ALTER TABLE ADD COLUMN IF NOT EXISTS,
        /// so this check is required for idempotent migrations.
        /// </summary>
        private static async Task<bool> ColumnExistsAsync(string table, string column)
        {
            bool found = false;
            await _writeLock.WaitAsync();
            try
            {
                var cmd = _connection!.CreateCommand();
                cmd.CommandText = $"PRAGMA table_info({table});";
                using var reader = await cmd.ExecuteReaderAsync();
                while (reader.Read())
                {
                    if (reader.GetString(1) == column) { found = true; break; }
                }
            }
            finally
            {
                _writeLock.Release();
            }
            return found;
        }
    }
}
