using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.IO;
using JoJot.Data;
using JoJot.Models;

namespace JoJot.Services;

/// <summary>
/// Manages the JoJot SQLite database via EF Core.
/// A raw SqliteConnection is kept for PRAGMAs and integrity checks.
/// All writes are serialized through a SemaphoreSlim(1,1).
/// </summary>
public static class DatabaseService
{
    private static SqliteConnection? _rawConnection;
    private static readonly SemaphoreSlim _writeLock = new(1, 1);
    private static string _dbPath = string.Empty;
    private static string _connectionString = string.Empty;

    /// <summary>
    /// Creates a new DbContext instance for a unit of work.
    /// </summary>
    private static JoJotDbContext CreateContext() => new(_connectionString);

    /// <summary>
    /// Opens (or creates) the SQLite database at the specified path.
    /// Creates the directory if it does not exist.
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
        };
        _connectionString = connStrBuilder.ToString();

        // Keep a raw connection for PRAGMAs and integrity checks
        _rawConnection = new SqliteConnection(_connectionString);
        await _rawConnection.OpenAsync().ConfigureAwait(false);

        // WAL mode persists once set; safe to set every open (idempotent)
        await ExecuteRawPragmaAsync("PRAGMA journal_mode=WAL;").ConfigureAwait(false);
        await ExecuteRawPragmaAsync("PRAGMA synchronous=NORMAL;").ConfigureAwait(false);
        await ExecuteRawPragmaAsync("PRAGMA foreign_keys=ON;").ConfigureAwait(false);

        LogService.Info($"Database opened: {dbPath}");
    }

    /// <summary>
    /// Creates all schema tables if they do not already exist.
    /// Uses EF Core's EnsureCreated — idempotent, safe to call on every launch.
    /// </summary>
    public static async Task EnsureSchemaAsync()
    {
        using var context = CreateContext();
        await context.Database.EnsureCreatedAsync().ConfigureAwait(false);
        LogService.Info("Schema ensured (all four tables).");
    }

    /// <summary>
    /// Checks that all expected tables exist in sqlite_master.
    /// If any table is missing, also runs PRAGMA quick_check.
    /// Returns true if the database is healthy; false if corrupt or tables are missing.
    /// </summary>
    public static async Task<bool> VerifyIntegrityAsync(CancellationToken ct = default)
    {
        var required = new[] { "notes", "app_state", "pending_moves", "preferences" };
        foreach (var table in required)
        {
            long count = await ExecuteScalarAsync<long>(
                $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{table}';").ConfigureAwait(false);
            if (count == 0)
            {
                LogService.Warn($"Expected table '{table}' is missing — running PRAGMA quick_check");
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
        LogService.Warn($"Corrupt database backed up to: {corruptPath}");

        await OpenAsync(dbPath).ConfigureAwait(false);
        await EnsureSchemaAsync().ConfigureAwait(false);

        LogService.Info("Fresh database created after corruption recovery.");
    }

    /// <summary>
    /// Executes a non-query SQL command (INSERT, UPDATE, DELETE, CREATE, PRAGMA, etc.).
    /// Serialized through the write lock.
    /// </summary>
    public static async Task ExecuteNonQueryAsync(string sql)
    {
        await _writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var cmd = _rawConnection!.CreateCommand();
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
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
    /// Serialized through the write lock for consistency.
    /// </summary>
    public static async Task<T> ExecuteScalarAsync<T>(string sql)
    {
        await _writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var cmd = _rawConnection!.CreateCommand();
            cmd.CommandText = sql;
            var result = await cmd.ExecuteScalarAsync().ConfigureAwait(false);
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
    /// Runs pending schema migrations in the background.
    /// Migrations are idempotent — safe to run on every launch.
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
    /// Should be called on application shutdown.
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

    // ─── Session CRUD ────────────────────────────────────────────────────────

    /// <summary>
    /// Returns all desktop sessions from app_state.
    /// </summary>
    public static async Task<List<(string DesktopGuid, string? DesktopName, int? DesktopIndex)>> GetAllSessionsAsync(CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var context = CreateContext();
            return await context.AppStates
                .Select(a => new { a.DesktopGuid, a.DesktopName, a.DesktopIndex })
                .AsNoTracking()
                .Select(a => ValueTuple.Create(a.DesktopGuid, a.DesktopName, a.DesktopIndex))
                .ToListAsync(ct).ConfigureAwait(false);
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
    }

    /// <summary>
    /// Updates a session's desktop identity after successful matching.
    /// Updates both app_state and notes tables to keep the FK consistent.
    /// </summary>
    public static async Task UpdateSessionAsync(string oldGuid, string newGuid, string? name, int? index)
    {
        await _writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            using var context = CreateContext();
            await context.AppStates
                .Where(a => a.DesktopGuid == oldGuid)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(a => a.DesktopGuid, newGuid)
                    .SetProperty(a => a.DesktopName, name)
                    .SetProperty(a => a.DesktopIndex, index)).ConfigureAwait(false);

            if (oldGuid != newGuid)
            {
                await context.Notes
                    .Where(n => n.DesktopGuid == oldGuid)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(n => n.DesktopGuid, newGuid)).ConfigureAwait(false);
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
    /// Check-then-add under write lock prevents races.
    /// </summary>
    public static async Task CreateSessionAsync(string desktopGuid, string? desktopName, int? desktopIndex)
    {
        await _writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            using var context = CreateContext();
            bool exists = await context.AppStates.AnyAsync(a => a.DesktopGuid == desktopGuid).ConfigureAwait(false);
            if (!exists)
            {
                context.AppStates.Add(new AppState
                {
                    DesktopGuid = desktopGuid,
                    DesktopName = desktopName,
                    DesktopIndex = desktopIndex
                });
                await context.SaveChangesAsync().ConfigureAwait(false);
            }
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
    /// </summary>
    public static async Task UpdateDesktopNameAsync(string desktopGuid, string newName)
    {
        await _writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            using var context = CreateContext();
            await context.AppStates
                .Where(a => a.DesktopGuid == desktopGuid)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(a => a.DesktopName, newName)).ConfigureAwait(false);
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

    // ─── Window Geometry ─────────────────────────────────────────────────────

    /// <summary>
    /// Reads the saved window geometry for a desktop session.
    /// Returns null if no geometry is saved or the desktop has no app_state row.
    /// </summary>
    public static async Task<WindowGeometry?> GetWindowGeometryAsync(string desktopGuid, CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var context = CreateContext();
            var state = await context.AppStates
                .AsNoTracking()
                .Where(a => a.DesktopGuid == desktopGuid && a.WindowLeft != null)
                .Select(a => new { a.WindowLeft, a.WindowTop, a.WindowWidth, a.WindowHeight, a.WindowState })
                .FirstOrDefaultAsync(ct).ConfigureAwait(false);

            if (state == null) return null;

            return new WindowGeometry(
                state.WindowLeft!.Value,
                state.WindowTop!.Value,
                state.WindowWidth!.Value,
                state.WindowHeight!.Value,
                state.WindowState == "Maximized");
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
    /// </summary>
    public static async Task SaveWindowGeometryAsync(string desktopGuid, WindowGeometry geo)
    {
        await _writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            using var context = CreateContext();
            await context.AppStates
                .Where(a => a.DesktopGuid == desktopGuid)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(a => a.WindowLeft, geo.Left)
                    .SetProperty(a => a.WindowTop, geo.Top)
                    .SetProperty(a => a.WindowWidth, geo.Width)
                    .SetProperty(a => a.WindowHeight, geo.Height)
                    .SetProperty(a => a.WindowState, geo.IsMaximized ? "Maximized" : "Normal")).ConfigureAwait(false);
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

    // ─── Notes CRUD ──────────────────────────────────────────────────────────

    /// <summary>
    /// Loads all notes for a desktop, ordered by pinned DESC then sort_order ASC.
    /// </summary>
    public static async Task<List<NoteTab>> GetNotesForDesktopAsync(string desktopGuid, CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var context = CreateContext();
            return await context.Notes
                .AsNoTracking()
                .Where(n => n.DesktopGuid == desktopGuid)
                .OrderByDescending(n => n.Pinned)
                .ThenBy(n => n.SortOrder)
                .ToListAsync(ct).ConfigureAwait(false);
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
    }

    /// <summary>
    /// Inserts a new note and returns its auto-generated ID.
    /// </summary>
    public static async Task<long> InsertNoteAsync(string desktopGuid, string? name, string content, bool pinned, int sortOrder)
    {
        await _writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            using var context = CreateContext();
            var now = DateTime.UtcNow;
            var note = new NoteTab
            {
                DesktopGuid = desktopGuid,
                Name = name,
                Content = content,
                Pinned = pinned,
                SortOrder = sortOrder,
                CreatedAt = now,
                UpdatedAt = now
            };
            context.Notes.Add(note);
            await context.SaveChangesAsync().ConfigureAwait(false);
            return note.Id;
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
        await _writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            using var context = CreateContext();
            await context.Notes
                .Where(n => n.Id == noteId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(n => n.Content, content)
                    .SetProperty(n => n.UpdatedAt, DateTime.UtcNow)).ConfigureAwait(false);
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
    /// Updates a note's custom name. Pass null to clear the name.
    /// </summary>
    public static async Task UpdateNoteNameAsync(long noteId, string? name)
    {
        await _writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            using var context = CreateContext();
            await context.Notes
                .Where(n => n.Id == noteId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(n => n.Name, name)
                    .SetProperty(n => n.UpdatedAt, DateTime.UtcNow)).ConfigureAwait(false);
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
        await _writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            using var context = CreateContext();
            await context.Notes
                .Where(n => n.Id == noteId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(n => n.Pinned, pinned)
                    .SetProperty(n => n.UpdatedAt, DateTime.UtcNow)).ConfigureAwait(false);
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
    /// Batch-updates sort_order for multiple notes.
    /// </summary>
    public static async Task UpdateNoteSortOrdersAsync(IEnumerable<(long Id, int SortOrder)> updates)
    {
        await _writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            using var context = CreateContext();
            foreach (var (id, sortOrder) in updates)
            {
                await context.Notes
                    .Where(n => n.Id == id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(n => n.SortOrder, sortOrder)).ConfigureAwait(false);
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
        await _writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            using var context = CreateContext();
            await context.Notes
                .Where(n => n.Id == noteId)
                .ExecuteDeleteAsync().ConfigureAwait(false);
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
    /// Deletes all notes with empty or whitespace-only content for a given desktop.
    /// Pinned notes are preserved regardless of content.
    /// Returns the number of deleted notes.
    /// </summary>
    public static async Task<int> DeleteEmptyNotesAsync(string desktopGuid)
    {
        await _writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            using var context = CreateContext();
            int deleted = await context.Notes
                .Where(n => n.DesktopGuid == desktopGuid
                    && (n.Content == null || n.Content.Trim() == "")
                    && !n.Pinned)
                .ExecuteDeleteAsync().ConfigureAwait(false);
            if (deleted > 0)
                LogService.Info($"Startup cleanup: deleted {deleted} empty note(s) for desktop {desktopGuid}");
            return deleted;
        }
        catch (Exception ex)
        {
            LogService.Error($"DeleteEmptyNotesAsync failed (guid={desktopGuid})", ex);
            return 0; // Non-fatal — don't crash on startup cleanup failure
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Returns the maximum sort_order for notes in a desktop, or -1 if no notes exist.
    /// </summary>
    public static async Task<int> GetMaxSortOrderAsync(string desktopGuid, CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var context = CreateContext();
            var maxOrder = await context.Notes
                .Where(n => n.DesktopGuid == desktopGuid)
                .Select(n => (int?)n.SortOrder)
                .MaxAsync(ct).ConfigureAwait(false);
            return maxOrder ?? -1;
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

    /// <summary>
    /// Returns the first N note names for a desktop, ordered by sort_order.
    /// Uses raw SQL for COALESCE(NULLIF(...), SUBSTR(...)) logic.
    /// </summary>
    public static async Task<List<string>> GetNoteNamesForDesktopAsync(string desktopGuid, int limit = 5, CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var context = CreateContext();
            var names = await context.Database
                .SqlQueryRaw<string>(
                    "SELECT COALESCE(NULLIF(name, ''), SUBSTR(content, 1, 30)) AS [Value] FROM notes WHERE desktop_guid = {0} ORDER BY sort_order ASC LIMIT {1}",
                    desktopGuid, limit)
                .ToListAsync(ct).ConfigureAwait(false);

            return names.Select(n => n ?? "Empty note").ToList();
        }
        catch (Exception ex)
        {
            LogService.Error($"GetNoteNamesForDesktopAsync failed (guid={desktopGuid})", ex);
            return [];
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Returns tab previews (name + content excerpt) for the recovery panel.
    /// </summary>
    public static async Task<List<(string? Name, string Excerpt, DateTime CreatedAt, DateTime UpdatedAt)>> GetNotePreviewsForDesktopAsync(string desktopGuid, int limit = 5, CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var context = CreateContext();
            var previews = await context.Notes
                .AsNoTracking()
                .Where(n => n.DesktopGuid == desktopGuid)
                .OrderBy(n => n.SortOrder)
                .Take(limit)
                .Select(n => new { n.Name, n.Content, n.CreatedAt, n.UpdatedAt })
                .ToListAsync(ct).ConfigureAwait(false);

            return previews.Select(p =>
            {
                string? name = string.IsNullOrWhiteSpace(p.Name) ? null : p.Name;
                string excerpt = p.Content.Length > 60 ? p.Content[..60] : p.Content;
                return (name, excerpt, p.CreatedAt, p.UpdatedAt);
            }).ToList();
        }
        catch (Exception ex)
        {
            LogService.Error($"GetNotePreviewsForDesktopAsync failed (guid={desktopGuid})", ex);
            return [];
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Returns total note count for a desktop GUID.
    /// </summary>
    public static async Task<int> GetNoteCountForDesktopAsync(string desktopGuid, CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var context = CreateContext();
            return await context.Notes.CountAsync(n => n.DesktopGuid == desktopGuid, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogService.Error($"GetNoteCountForDesktopAsync failed (guid={desktopGuid})", ex);
            return 0;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Returns the desktop name for a given GUID from the app_state table.
    /// </summary>
    public static async Task<string?> GetDesktopNameAsync(string desktopGuid, CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var context = CreateContext();
            return await context.AppStates
                .AsNoTracking()
                .Where(a => a.DesktopGuid == desktopGuid)
                .Select(a => a.DesktopName)
                .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogService.Error($"GetDesktopNameAsync failed (guid={desktopGuid})", ex);
            return null;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    // ─── Orphaned Session Queries ────────────────────────────────────────────

    /// <summary>
    /// Returns info for each orphaned session: desktop GUID, desktop name, tab count, and last updated date.
    /// </summary>
    public static async Task<List<(string DesktopGuid, string? DesktopName, int TabCount, DateTime LastUpdated)>> GetOrphanedSessionInfoAsync(
        IReadOnlyList<string> orphanGuids, CancellationToken ct = default)
    {
        List<(string, string?, int, DateTime)> results = [];
        if (orphanGuids.Count == 0) return results;

        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var context = CreateContext();
            foreach (var guid in orphanGuids)
            {
                var desktopName = await context.AppStates
                    .AsNoTracking()
                    .Where(a => a.DesktopGuid == guid)
                    .Select(a => a.DesktopName)
                    .FirstOrDefaultAsync(ct).ConfigureAwait(false);

                var noteStats = await context.Notes
                    .AsNoTracking()
                    .Where(n => n.DesktopGuid == guid)
                    .GroupBy(n => 1)
                    .Select(g => new { Count = g.Count(), MaxUpdated = g.Max(n => n.UpdatedAt) })
                    .FirstOrDefaultAsync(ct).ConfigureAwait(false);

                int tabCount = noteStats?.Count ?? 0;
                DateTime lastUpdated = noteStats?.MaxUpdated ?? DateTime.Parse("2000-01-01");
                results.Add((guid, desktopName, tabCount, lastUpdated));
            }
        }
        catch (Exception ex)
        {
            LogService.Error("GetOrphanedSessionInfoAsync failed", ex);
            throw;
        }
        finally
        {
            _writeLock.Release();
        }

        return results;
    }

    /// <summary>
    /// Migrates all notes from sourceGuid to targetGuid.
    /// Reassigns sort_order starting after the max existing sort_order on the target desktop.
    /// Pinned tabs from the source are un-pinned during migration.
    /// </summary>
    public static async Task MigrateTabsAsync(string sourceGuid, string targetGuid)
    {
        await _writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            using var context = CreateContext();
            var maxSortOrder = await context.Notes
                .Where(n => n.DesktopGuid == targetGuid)
                .Select(n => (int?)n.SortOrder)
                .MaxAsync().ConfigureAwait(false) ?? 0;

            int baseOrder = maxSortOrder + 1;

            // EF Core ExecuteUpdateAsync doesn't support n.SortOrder in the expression,
            // so we load, modify, and save for the sort_order offset.
            var sourceTabs = await context.Notes
                .Where(n => n.DesktopGuid == sourceGuid)
                .ToListAsync().ConfigureAwait(false);

            foreach (var tab in sourceTabs)
            {
                tab.DesktopGuid = targetGuid;
                tab.Pinned = false;
                tab.SortOrder = baseOrder + tab.SortOrder;
            }
            await context.SaveChangesAsync().ConfigureAwait(false);

            LogService.Info($"Migrated tabs from {sourceGuid} to {targetGuid} (base sort_order: {baseOrder})");
        }
        catch (Exception ex)
        {
            LogService.Error($"MigrateTabsAsync failed (source={sourceGuid}, target={targetGuid})", ex);
            throw;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Permanently deletes an orphaned session and all its notes.
    /// </summary>
    public static async Task DeleteSessionAndNotesAsync(string desktopGuid)
    {
        await _writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            using var context = CreateContext();
            await context.Notes
                .Where(n => n.DesktopGuid == desktopGuid)
                .ExecuteDeleteAsync().ConfigureAwait(false);
            await context.AppStates
                .Where(a => a.DesktopGuid == desktopGuid)
                .ExecuteDeleteAsync().ConfigureAwait(false);

            LogService.Info($"Deleted orphaned session and notes for {desktopGuid}");
        }
        catch (Exception ex)
        {
            LogService.Error($"DeleteSessionAndNotesAsync failed (guid={desktopGuid})", ex);
            throw;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    // ─── Preferences ─────────────────────────────────────────────────────────

    /// <summary>
    /// Gets a preference value by key. Returns null if the key doesn't exist.
    /// </summary>
    public static async Task<string?> GetPreferenceAsync(string key, CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var context = CreateContext();
            return await context.Preferences
                .AsNoTracking()
                .Where(p => p.Key == key)
                .Select(p => p.Value)
                .FirstOrDefaultAsync(ct).ConfigureAwait(false);
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
    /// Upserts a preference value. Uses raw SQL for ON CONFLICT upsert.
    /// </summary>
    public static async Task SetPreferenceAsync(string key, string value)
    {
        await _writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            using var context = CreateContext();
            await context.Database.ExecuteSqlRawAsync(
                "INSERT INTO preferences (key, value) VALUES ({0}, {1}) ON CONFLICT(key) DO UPDATE SET value = {1}",
                key, value).ConfigureAwait(false);
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

    // ─── Pending Moves ───────────────────────────────────────────────────────

    /// <summary>
    /// Inserts a pending_moves row when a window drag is detected.
    /// </summary>
    public static async Task<long> InsertPendingMoveAsync(string windowId, string fromDesktop, string? toDesktop)
    {
        await _writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            using var context = CreateContext();
            var move = new PendingMove(0, windowId, fromDesktop, toDesktop, DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));
            context.PendingMoves.Add(move);
            await context.SaveChangesAsync().ConfigureAwait(false);

            LogService.Info($"InsertPendingMove: id={move.Id}, window={windowId}, from={fromDesktop}, to={toDesktop}");
            return move.Id;
        }
        catch (Exception ex)
        {
            LogService.Error($"InsertPendingMoveAsync failed (window={windowId})", ex);
            throw;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Deletes the pending_moves row for a window after drag resolution.
    /// </summary>
    public static async Task DeletePendingMoveAsync(string windowId)
    {
        await _writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            using var context = CreateContext();
            int deleted = await context.PendingMoves
                .Where(p => p.WindowId == windowId)
                .ExecuteDeleteAsync().ConfigureAwait(false);
            LogService.Info($"DeletePendingMove: window={windowId}, rows={deleted}");
        }
        catch (Exception ex)
        {
            LogService.Error($"DeletePendingMoveAsync failed (window={windowId})", ex);
            throw;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Reads all pending_moves rows for crash recovery.
    /// </summary>
    public static async Task<List<PendingMove>> GetPendingMovesAsync(CancellationToken ct = default)
    {
        using var context = CreateContext();
        return await context.PendingMoves
            .AsNoTracking()
            .ToListAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes all pending_moves rows after crash recovery resolves them.
    /// </summary>
    public static async Task DeleteAllPendingMovesAsync()
    {
        await _writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            using var context = CreateContext();
            int deleted = await context.PendingMoves.ExecuteDeleteAsync().ConfigureAwait(false);
            LogService.Info($"DeleteAllPendingMoves: rows={deleted}");
        }
        catch (Exception ex)
        {
            LogService.Error($"DeleteAllPendingMovesAsync failed", ex);
            throw;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Updates all notes from one desktop_guid to another, preserving sort_order and pin state.
    /// </summary>
    public static async Task MigrateNotesDesktopGuidAsync(string fromGuid, string toGuid)
    {
        await _writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            using var context = CreateContext();
            int affected = await context.Notes
                .Where(n => n.DesktopGuid == fromGuid)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(n => n.DesktopGuid, toGuid)).ConfigureAwait(false);
            LogService.Info($"Reparented {affected} notes from {fromGuid} to {toGuid}");
        }
        catch (Exception ex)
        {
            LogService.Error($"MigrateNotesDesktopGuidAsync failed (from={fromGuid}, to={toGuid})", ex);
            throw;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Migrates tabs from source to target desktop, preserving pin state.
    /// </summary>
    public static async Task MigrateTabsPreservePinsAsync(string sourceGuid, string targetGuid)
    {
        await _writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            using var context = CreateContext();
            var maxSortOrder = await context.Notes
                .Where(n => n.DesktopGuid == targetGuid)
                .Select(n => (int?)n.SortOrder)
                .MaxAsync().ConfigureAwait(false) ?? 0;

            int baseOrder = maxSortOrder + 1;

            var sourceTabs = await context.Notes
                .Where(n => n.DesktopGuid == sourceGuid)
                .ToListAsync().ConfigureAwait(false);

            foreach (var tab in sourceTabs)
            {
                tab.DesktopGuid = targetGuid;
                tab.SortOrder = baseOrder + tab.SortOrder;
            }
            await context.SaveChangesAsync().ConfigureAwait(false);

            LogService.Info($"Migrated tabs (preserving pins) from {sourceGuid} to {targetGuid} (base sort_order: {baseOrder})");
        }
        catch (Exception ex)
        {
            LogService.Error($"MigrateTabsPreservePinsAsync failed (source={sourceGuid}, target={targetGuid})", ex);
            throw;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Updates a session's desktop identity in app_state.
    /// Deletes any existing session for newGuid first to avoid UNIQUE constraint violations.
    /// </summary>
    public static async Task UpdateSessionDesktopAsync(string oldGuid, string newGuid, string? name, int? index)
    {
        await _writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            using var context = CreateContext();

            // Remove any existing session for the target desktop to avoid UNIQUE constraint
            await context.AppStates
                .Where(a => a.DesktopGuid == newGuid)
                .ExecuteDeleteAsync().ConfigureAwait(false);

            await context.AppStates
                .Where(a => a.DesktopGuid == oldGuid)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(a => a.DesktopGuid, newGuid)
                    .SetProperty(a => a.DesktopName, name)
                    .SetProperty(a => a.DesktopIndex, index)).ConfigureAwait(false);

            LogService.Info($"Updated session desktop: {oldGuid} -> {newGuid} (name={name}, index={index})");
        }
        catch (Exception ex)
        {
            LogService.Error($"UpdateSessionDesktopAsync failed (old={oldGuid}, new={newGuid})", ex);
            throw;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    // ─── Private helpers ─────────────────────────────────────────────────────

    /// <summary>
    /// Runs PRAGMA quick_check and returns true only if the result is "ok".
    /// </summary>
    private static async Task<bool> RunQuickCheckAsync()
    {
        try
        {
            await _writeLock.WaitAsync().ConfigureAwait(false);
            try
            {
                var cmd = _rawConnection!.CreateCommand();
                cmd.CommandText = "PRAGMA quick_check;";
                using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
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

    /// <summary>
    /// Checks whether a column exists in a table via PRAGMA table_info.
    /// </summary>
    private static async Task<bool> ColumnExistsAsync(string table, string column)
    {
        bool found = false;
        await _writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var cmd = _rawConnection!.CreateCommand();
            cmd.CommandText = $"PRAGMA table_info({table});";
            using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
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

    /// <summary>
    /// Executes a PRAGMA on the raw connection (not through write lock).
    /// Used during initialization before the lock is needed.
    /// </summary>
    private static async Task ExecuteRawPragmaAsync(string sql)
    {
        var cmd = _rawConnection!.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }
}
