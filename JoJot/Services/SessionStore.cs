using Microsoft.EntityFrameworkCore;
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
    public static async Task<List<(string DesktopGuid, string? DesktopName, int? DesktopIndex)>> GetAllSessionsAsync(CancellationToken ct = default)
    {
        await DatabaseCore.AcquireWriteLockAsync(ct).ConfigureAwait(false);
        try
        {
            await using var context = DatabaseCore.CreateContext();
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
            DatabaseCore.ReleaseWriteLock();
        }
    }

    /// <summary>
    /// Updates a session's desktop identity after successful matching.
    /// Updates both app_state and notes tables to keep the FK consistent.
    /// </summary>
    public static async Task UpdateSessionAsync(string oldGuid, string newGuid, string? name, int? index)
    {
        await DatabaseCore.AcquireWriteLockAsync().ConfigureAwait(false);
        try
        {
            await using var context = DatabaseCore.CreateContext();
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
            LogService.Error("UpdateSessionAsync failed (old={OldGuid}, new={NewGuid})", oldGuid, newGuid, ex);
            throw;
        }
        finally
        {
            DatabaseCore.ReleaseWriteLock();
        }
    }

    /// <summary>
    /// Creates a new desktop session in app_state.
    /// </summary>
    public static async Task CreateSessionAsync(string desktopGuid, string? desktopName, int? desktopIndex)
    {
        await DatabaseCore.AcquireWriteLockAsync().ConfigureAwait(false);
        try
        {
            await using var context = DatabaseCore.CreateContext();
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
            LogService.Error("CreateSessionAsync failed (guid={DesktopGuid})", desktopGuid, ex);
            throw;
        }
        finally
        {
            DatabaseCore.ReleaseWriteLock();
        }
    }

    /// <summary>
    /// Updates the desktop_name for a session identified by its GUID.
    /// </summary>
    public static async Task UpdateDesktopNameAsync(string desktopGuid, string newName)
    {
        await DatabaseCore.AcquireWriteLockAsync().ConfigureAwait(false);
        try
        {
            await using var context = DatabaseCore.CreateContext();
            await context.AppStates
                .Where(a => a.DesktopGuid == desktopGuid)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(a => a.DesktopName, newName)).ConfigureAwait(false);
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
    public static async Task<string?> GetDesktopNameAsync(string desktopGuid, CancellationToken ct = default)
    {
        await DatabaseCore.AcquireWriteLockAsync(ct).ConfigureAwait(false);
        try
        {
            await using var context = DatabaseCore.CreateContext();
            return await context.AppStates
                .AsNoTracking()
                .Where(a => a.DesktopGuid == desktopGuid)
                .Select(a => a.DesktopName)
                .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogService.Error("GetDesktopNameAsync failed (guid={DesktopGuid})", desktopGuid, ex);
            return null;
        }
        finally
        {
            DatabaseCore.ReleaseWriteLock();
        }
    }

    /// <summary>
    /// Updates a session's desktop identity in app_state.
    /// Deletes any existing session for newGuid first to avoid UNIQUE constraint violations.
    /// </summary>
    public static async Task UpdateSessionDesktopAsync(string oldGuid, string newGuid, string? name, int? index)
    {
        await DatabaseCore.AcquireWriteLockAsync().ConfigureAwait(false);
        try
        {
            await using var context = DatabaseCore.CreateContext();

            await context.AppStates
                .Where(a => a.DesktopGuid == newGuid)
                .ExecuteDeleteAsync().ConfigureAwait(false);

            await context.AppStates
                .Where(a => a.DesktopGuid == oldGuid)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(a => a.DesktopGuid, newGuid)
                    .SetProperty(a => a.DesktopName, name)
                    .SetProperty(a => a.DesktopIndex, index)).ConfigureAwait(false);

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

    // ─── Window Geometry ─────────────────────────────────────────────────────

    /// <summary>
    /// Reads the saved window geometry for a desktop session.
    /// Returns null if no geometry is saved.
    /// </summary>
    public static async Task<WindowGeometry?> GetWindowGeometryAsync(string desktopGuid, CancellationToken ct = default)
    {
        await DatabaseCore.AcquireWriteLockAsync(ct).ConfigureAwait(false);
        try
        {
            await using var context = DatabaseCore.CreateContext();
            var state = await context.AppStates
                .AsNoTracking()
                .Where(a => a.DesktopGuid == desktopGuid && a.WindowLeft != null)
                .Select(a => new { a.WindowLeft, a.WindowTop, a.WindowWidth, a.WindowHeight, a.WindowState })
                .FirstOrDefaultAsync(ct).ConfigureAwait(false);

            if (state is null) return null;

            return new WindowGeometry(
                state.WindowLeft!.Value,
                state.WindowTop!.Value,
                state.WindowWidth!.Value,
                state.WindowHeight!.Value,
                state.WindowState == "Maximized");
        }
        catch (Exception ex)
        {
            LogService.Error("GetWindowGeometryAsync failed (guid={DesktopGuid})", desktopGuid, ex);
            throw;
        }
        finally
        {
            DatabaseCore.ReleaseWriteLock();
        }
    }

    /// <summary>
    /// Persists window geometry for a desktop session.
    /// </summary>
    public static async Task SaveWindowGeometryAsync(string desktopGuid, WindowGeometry geo)
    {
        await DatabaseCore.AcquireWriteLockAsync().ConfigureAwait(false);
        try
        {
            await using var context = DatabaseCore.CreateContext();
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
            LogService.Error("SaveWindowGeometryAsync failed (guid={DesktopGuid})", desktopGuid, ex);
            throw;
        }
        finally
        {
            DatabaseCore.ReleaseWriteLock();
        }
    }

    // ─── Orphaned Session Operations ─────────────────────────────────────────

    private static readonly DateTime OrphanFallbackDate = new(2000, 1, 1);

    /// <summary>
    /// Returns info for each orphaned session: desktop GUID, desktop name, tab count, and last updated date.
    /// </summary>
    public static async Task<List<(string DesktopGuid, string? DesktopName, int TabCount, DateTime LastUpdated)>> GetOrphanedSessionInfoAsync(
        IReadOnlyList<string> orphanGuids, CancellationToken ct = default)
    {
        if (orphanGuids.Count == 0) return [];

        await DatabaseCore.AcquireWriteLockAsync(ct).ConfigureAwait(false);
        try
        {
            await using var context = DatabaseCore.CreateContext();

            // Batch query 1: all desktop names for orphan GUIDs (single round-trip)
            var appStates = await context.AppStates
                .AsNoTracking()
                .Where(a => orphanGuids.Contains(a.DesktopGuid))
                .Select(a => new { a.DesktopGuid, a.DesktopName })
                .ToDictionaryAsync(a => a.DesktopGuid, a => a.DesktopName, ct).ConfigureAwait(false);

            // Batch query 2: note stats grouped by desktop GUID (single round-trip)
            var noteStats = await context.Notes
                .AsNoTracking()
                .Where(n => orphanGuids.Contains(n.DesktopGuid))
                .GroupBy(n => n.DesktopGuid)
                .Select(g => new { DesktopGuid = g.Key, Count = g.Count(), MaxUpdated = g.Max(n => n.UpdatedAt) })
                .ToDictionaryAsync(g => g.DesktopGuid, ct).ConfigureAwait(false);

            // Combine results
            var results = new List<(string, string?, int, DateTime)>(orphanGuids.Count);
            foreach (var guid in orphanGuids)
            {
                appStates.TryGetValue(guid, out var desktopName);
                int tabCount = 0;
                DateTime lastUpdated = OrphanFallbackDate;
                if (noteStats.TryGetValue(guid, out var stats))
                {
                    tabCount = stats.Count;
                    lastUpdated = stats.MaxUpdated;
                }
                results.Add((guid, desktopName, tabCount, lastUpdated));
            }
            return results;
        }
        catch (Exception ex)
        {
            LogService.Error("GetOrphanedSessionInfoAsync failed", ex);
            throw;
        }
        finally
        {
            DatabaseCore.ReleaseWriteLock();
        }
    }

    /// <summary>
    /// Permanently deletes an orphaned session and all its notes.
    /// </summary>
    public static async Task DeleteSessionAndNotesAsync(string desktopGuid)
    {
        await DatabaseCore.AcquireWriteLockAsync().ConfigureAwait(false);
        try
        {
            await using var context = DatabaseCore.CreateContext();
            await context.Notes
                .Where(n => n.DesktopGuid == desktopGuid)
                .ExecuteDeleteAsync().ConfigureAwait(false);
            await context.AppStates
                .Where(a => a.DesktopGuid == desktopGuid)
                .ExecuteDeleteAsync().ConfigureAwait(false);

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
