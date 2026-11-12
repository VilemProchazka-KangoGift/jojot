using System.Globalization;
using JoJot.Models;

namespace JoJot.Services;

/// <summary>
/// CRUD operations for the app_state table (sessions, window geometry, orphaned sessions).
/// </summary>
public static class SessionStore
{
    /// <summary>
    /// Returns all desktop sessions from app_state.
    /// </summary>
    public static Task<List<(string DesktopGuid, string? DesktopName, int? DesktopIndex)>> GetAllSessionsAsync(CancellationToken ct = default) =>
        DatabaseCore.RunOnDbThreadAsync(() => GetAllSessionsCoreAsync(ct));

    private static async Task<List<(string DesktopGuid, string? DesktopName, int? DesktopIndex)>> GetAllSessionsCoreAsync(CancellationToken ct = default)
    {
        try
        {
            await using var cmd = DatabaseCore.CreateCommand("SELECT desktop_guid, desktop_name, desktop_index FROM app_state;");
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            var rows = new List<(string, string?, int?)>();
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
                rows.Add((reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetInt32(2)));
            return rows;
        }
        catch (Exception ex)
        {
            LogService.Error("GetAllSessionsAsync failed", ex);
            throw;
        }
    }

    /// <summary>
    /// Updates a session's desktop identity after successful matching.
    /// Updates both app_state and notes tables to keep the FK consistent.
    /// </summary>
    public static Task UpdateSessionAsync(string oldGuid, string newGuid, string? name, int? index) =>
        DatabaseCore.RunOnDbThreadAsync(() => UpdateSessionCoreAsync(oldGuid, newGuid, name, index));

    private static async Task UpdateSessionCoreAsync(string oldGuid, string newGuid, string? name, int? index)
    {
        await DatabaseCore.AcquireWriteLockAsync().ConfigureAwait(false);
        try
        {
            await using var cmd = DatabaseCore.CreateCommand(
                "UPDATE app_state SET desktop_guid=@new, desktop_name=@name, desktop_index=@index WHERE desktop_guid=@old;",
                ("@new", newGuid), ("@name", (object?)name ?? DBNull.Value), ("@index", (object?)index ?? DBNull.Value), ("@old", oldGuid));
            using var transaction = cmd.Connection!.BeginTransaction();
            cmd.Transaction = transaction;
            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            if (oldGuid != newGuid)
            {
                await using var notes = DatabaseCore.CreateCommand("UPDATE notes SET desktop_guid=@new WHERE desktop_guid=@old;", ("@new", newGuid), ("@old", oldGuid));
                notes.Transaction = transaction;
                await notes.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
            transaction.Commit();
        }
        catch (Exception ex)
        {
            LogService.Error("UpdateSessionAsync failed (old={OldGuid}, new={NewGuid})", oldGuid, newGuid, ex);
            throw;
        }
        finally
        {
            DatabaseCore.ReleaseWriteLock();
        }
    }

    /// <summary>
    /// Creates a desktop session in app_state if none exists for the GUID. An existing row is left
    /// untouched (name, index and geometry are not overwritten). One INSERT OR IGNORE statement
    /// against the UNIQUE desktop_guid index — no read-then-write round trip.
    /// </summary>
    public static Task CreateSessionAsync(string desktopGuid, string? desktopName, int? desktopIndex) =>
        DatabaseCore.ExecuteNonQueryAsync(
            "INSERT OR IGNORE INTO app_state (desktop_guid, desktop_name, desktop_index) VALUES (@guid, @name, @index);",
            ("@guid", desktopGuid),
            ("@name", (object?)desktopName ?? DBNull.Value),
            ("@index", (object?)desktopIndex ?? DBNull.Value));

    /// <summary>
    /// Returns true when an app_state row exists for the given desktop GUID (case-insensitive, like
    /// session matching's GUID tier). Read-only — does not take the write lock.
    /// </summary>
    public static Task<bool> SessionExistsAsync(string desktopGuid) =>
        DatabaseCore.RunOnDbThreadAsync(() => SessionExistsCoreAsync(desktopGuid));

    private static async Task<bool> SessionExistsCoreAsync(string desktopGuid)
    {
        string lowered = desktopGuid.ToLowerInvariant();
        try
        {
            await using var cmd = DatabaseCore.CreateCommand("SELECT EXISTS(SELECT 1 FROM app_state WHERE lower(desktop_guid)=@guid);", ("@guid", lowered));
            return Convert.ToInt64(await cmd.ExecuteScalarAsync().ConfigureAwait(false), CultureInfo.InvariantCulture) != 0;
        }
        catch (Exception ex)
        {
            LogService.Error("SessionExistsAsync failed (guid={DesktopGuid})", desktopGuid, ex);
            throw;
        }
    }

    /// <summary>
    /// Updates the desktop_name for a session identified by its GUID.
    /// </summary>
    public static Task UpdateDesktopNameAsync(string desktopGuid, string newName) =>
        DatabaseCore.RunOnDbThreadAsync(() => UpdateDesktopNameCoreAsync(desktopGuid, newName));

    private static async Task UpdateDesktopNameCoreAsync(string desktopGuid, string newName)
    {
        await DatabaseCore.AcquireWriteLockAsync().ConfigureAwait(false);
        try
        {
            await using var cmd = DatabaseCore.CreateCommand("UPDATE app_state SET desktop_name=@name WHERE desktop_guid=@guid;", ("@name", newName), ("@guid", desktopGuid));
            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogService.Error("UpdateDesktopNameAsync failed (guid={DesktopGuid})", desktopGuid, ex);
            throw;
        }
        finally
        {
            DatabaseCore.ReleaseWriteLock();
        }
    }

    /// <summary>
    /// Returns the desktop name for a given GUID from the app_state table.
    /// </summary>
    public static Task<string?> GetDesktopNameAsync(string desktopGuid, CancellationToken ct = default) =>
        DatabaseCore.RunOnDbThreadAsync(() => GetDesktopNameCoreAsync(desktopGuid, ct));

    private static async Task<string?> GetDesktopNameCoreAsync(string desktopGuid, CancellationToken ct = default)
    {
        try
        {
            await using var cmd = DatabaseCore.CreateCommand("SELECT desktop_name FROM app_state WHERE desktop_guid=@guid LIMIT 1;", ("@guid", desktopGuid));
            var value = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            return value is null or DBNull ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
        }
        catch (Exception ex)
        {
            LogService.Error("GetDesktopNameAsync failed (guid={DesktopGuid})", desktopGuid, ex);
            return null;
        }
    }

    /// <summary>
    /// Updates a session's desktop identity in app_state.
    /// Deletes any existing session for newGuid first to avoid UNIQUE constraint violations.
    /// </summary>
    public static Task UpdateSessionDesktopAsync(string oldGuid, string newGuid, string? name, int? index) =>
        DatabaseCore.RunOnDbThreadAsync(() => UpdateSessionDesktopCoreAsync(oldGuid, newGuid, name, index));

    private static async Task UpdateSessionDesktopCoreAsync(string oldGuid, string newGuid, string? name, int? index)
    {
        await DatabaseCore.AcquireWriteLockAsync().ConfigureAwait(false);
        try
        {
            await using var cmd = DatabaseCore.CreateCommand("DELETE FROM app_state WHERE desktop_guid=@new;");
            using var transaction = cmd.Connection!.BeginTransaction();
            cmd.Transaction = transaction;
            cmd.Parameters.AddWithValue("@new", newGuid);
            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            await using var update = DatabaseCore.CreateCommand("UPDATE app_state SET desktop_guid=@new, desktop_name=@name, desktop_index=@index WHERE desktop_guid=@old;",
                ("@new", newGuid), ("@name", (object?)name ?? DBNull.Value), ("@index", (object?)index ?? DBNull.Value), ("@old", oldGuid));
            update.Transaction = transaction;
            await update.ExecuteNonQueryAsync().ConfigureAwait(false);
            transaction.Commit();
            LogService.Info("Updated session desktop: {OldGuid} -> {NewGuid} (name={Name}, index={Index})", oldGuid, newGuid, name, index);
        }
        catch (Exception ex)
        {
            LogService.Error("UpdateSessionDesktopAsync failed (old={OldGuid}, new={NewGuid})", oldGuid, newGuid, ex);
            throw;
        }
        finally
        {
            DatabaseCore.ReleaseWriteLock();
        }
    }

    // ─── Window Geometry ────────────────────────────────────────────────

    /// <summary>
    /// Reads the saved window geometry for a desktop session.
    /// Returns null if no geometry is saved.
    /// </summary>
    public static Task<WindowGeometry?> GetWindowGeometryAsync(string desktopGuid, CancellationToken ct = default) =>
        DatabaseCore.RunOnDbThreadAsync(() => GetWindowGeometryCoreAsync(desktopGuid, ct));

    private static async Task<WindowGeometry?> GetWindowGeometryCoreAsync(string desktopGuid, CancellationToken ct = default)
    {
        try
        {
            await using var cmd = DatabaseCore.CreateCommand("SELECT window_left, window_top, window_width, window_height, window_state FROM app_state WHERE desktop_guid=@guid AND window_left IS NOT NULL LIMIT 1;", ("@guid", desktopGuid));
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await reader.ReadAsync(ct).ConfigureAwait(false)) return null;
            return new WindowGeometry(reader.GetDouble(0), reader.GetDouble(1), reader.GetDouble(2), reader.GetDouble(3),
                !reader.IsDBNull(4) && reader.GetString(4) == "Maximized");
        }
        catch (Exception ex)
        {
            LogService.Error("GetWindowGeometryAsync failed (guid={DesktopGuid})", desktopGuid, ex);
            throw;
        }
    }

    /// <summary>
    /// Persists window geometry for a desktop session.
    /// </summary>
    public static Task SaveWindowGeometryAsync(string desktopGuid, WindowGeometry geo) =>
        DatabaseCore.RunOnDbThreadAsync(() => SaveWindowGeometryCoreAsync(desktopGuid, geo));

    private static async Task SaveWindowGeometryCoreAsync(string desktopGuid, WindowGeometry geo)
    {
        await DatabaseCore.AcquireWriteLockAsync().ConfigureAwait(false);
        try
        {
            await using var cmd = DatabaseCore.CreateCommand("UPDATE app_state SET window_left=@left, window_top=@top, window_width=@width, window_height=@height, window_state=@state WHERE desktop_guid=@guid;",
                ("@left", geo.Left), ("@top", geo.Top), ("@width", geo.Width), ("@height", geo.Height), ("@state", geo.IsMaximized ? "Maximized" : "Normal"), ("@guid", desktopGuid));
            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogService.Error("SaveWindowGeometryAsync failed (guid={DesktopGuid})", desktopGuid, ex);
            throw;
        }
        finally
        {
            DatabaseCore.ReleaseWriteLock();
        }
    }

    // ─── Orphaned Session Operations ────────────────────────────────────

    private static readonly DateTime OrphanFallbackDate = new(2000, 1, 1);

    /// <summary>
    /// Returns info for each orphaned session: desktop GUID, desktop name, tab count, and last updated date.
    /// </summary>
    public static Task<List<(string DesktopGuid, string? DesktopName, int TabCount, DateTime LastUpdated)>> GetOrphanedSessionInfoAsync(
        IReadOnlyList<string> orphanGuids, CancellationToken ct = default)
    {
        // Snapshot on the caller's thread — the query runs later on the DB thread.
        string[] snapshot = [.. orphanGuids];
        return DatabaseCore.RunOnDbThreadAsync(() => GetOrphanedSessionInfoCoreAsync(snapshot, ct));
    }

    private static async Task<List<(string DesktopGuid, string? DesktopName, int TabCount, DateTime LastUpdated)>> GetOrphanedSessionInfoCoreAsync(
        IReadOnlyList<string> orphanGuids, CancellationToken ct)
    {
        if (orphanGuids.Count == 0) return [];
        try
        {
            var names = new Dictionary<string, string?>();
            var stats = new Dictionary<string, (int Count, DateTime MaxUpdated)>();
            // Stay below SQLite's variable limit even on builds with the older 999 limit.
            foreach (var chunk in orphanGuids.Distinct().Chunk(500))
            {
                string placeholders = string.Join(", ", Enumerable.Range(0, chunk.Length).Select(i => $"@g{i}"));
                var parameters = chunk.Select((g, i) => ($"@g{i}", (object?)g)).ToArray();
                await using (var cmd = DatabaseCore.CreateCommand($"SELECT desktop_guid, desktop_name FROM app_state WHERE desktop_guid IN ({placeholders});", parameters))
                await using (var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
                    while (await reader.ReadAsync(ct).ConfigureAwait(false))
                        names[reader.GetString(0)] = reader.IsDBNull(1) ? null : reader.GetString(1);
                await using (var cmd = DatabaseCore.CreateCommand($"SELECT desktop_guid, COUNT(*), MAX(updated_at) FROM notes WHERE desktop_guid IN ({placeholders}) GROUP BY desktop_guid;", parameters))
                await using (var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
                    while (await reader.ReadAsync(ct).ConfigureAwait(false))
                        stats[reader.GetString(0)] = (reader.GetInt32(1), reader.GetDateTime(2));
            }
            var result = new List<(string, string?, int, DateTime)>(orphanGuids.Count);
            foreach (string guid in orphanGuids)
            {
                names.TryGetValue(guid, out string? name);
                var stat = stats.TryGetValue(guid, out var found) ? found : (0, OrphanFallbackDate);
                result.Add((guid, name, stat.Item1, stat.Item2));
            }
            return result;
        }
        catch (Exception ex)
        {
            LogService.Error("GetOrphanedSessionInfoAsync failed", ex);
            throw;
        }
    }

    /// <summary>
    /// Permanently deletes an orphaned session and all its notes.
    /// </summary>
    public static Task DeleteSessionAndNotesAsync(string desktopGuid) =>
        DatabaseCore.RunOnDbThreadAsync(() => DeleteSessionAndNotesCoreAsync(desktopGuid));

    private static async Task DeleteSessionAndNotesCoreAsync(string desktopGuid)
    {
        await DatabaseCore.AcquireWriteLockAsync().ConfigureAwait(false);
        try
        {
            await using var cmd = DatabaseCore.CreateCommand("DELETE FROM notes WHERE desktop_guid=@guid;");
            using var transaction = cmd.Connection!.BeginTransaction();
            cmd.Transaction = transaction;
            cmd.Parameters.AddWithValue("@guid", desktopGuid);
            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            await using var state = DatabaseCore.CreateCommand("DELETE FROM app_state WHERE desktop_guid=@guid;", ("@guid", desktopGuid));
            state.Transaction = transaction;
            await state.ExecuteNonQueryAsync().ConfigureAwait(false);
            transaction.Commit();
            LogService.Info("Deleted orphaned session and notes for {DesktopGuid}", desktopGuid);
        }
        catch (Exception ex)
        {
            LogService.Error("DeleteSessionAndNotesAsync failed (guid={DesktopGuid})", desktopGuid, ex);
            throw;
        }
        finally
        {
            DatabaseCore.ReleaseWriteLock();
        }
    }
}
