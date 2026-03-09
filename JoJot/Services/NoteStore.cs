using Microsoft.EntityFrameworkCore;
using JoJot.Models;

namespace JoJot.Services;

/// <summary>
/// CRUD operations for the notes table.
/// </summary>
public static class NoteStore
{
    /// <summary>
    /// Loads all notes for a desktop, ordered by pinned DESC then sort_order ASC.
    /// </summary>
    public static async Task<List<NoteTab>> GetNotesForDesktopAsync(string desktopGuid, CancellationToken ct = default)
    {
        await DatabaseCore.AcquireWriteLockAsync(ct).ConfigureAwait(false);
        try
        {
            using var context = DatabaseCore.CreateContext();
            return await context.Notes
                .AsNoTracking()
                .Where(n => n.DesktopGuid == desktopGuid)
                .OrderByDescending(n => n.Pinned)
                .ThenBy(n => n.SortOrder)
                .ToListAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogService.Error("GetNotesForDesktopAsync failed (guid={DesktopGuid})", desktopGuid, ex);
            throw;
        }
        finally
        {
            DatabaseCore.ReleaseWriteLock();
        }
    }

    /// <summary>
    /// Inserts a new note and returns its auto-generated ID.
    /// </summary>
    public static async Task<long> InsertNoteAsync(string desktopGuid, string? name, string content, bool pinned, int sortOrder)
    {
        await DatabaseCore.AcquireWriteLockAsync().ConfigureAwait(false);
        try
        {
            using var context = DatabaseCore.CreateContext();
            var now = DatabaseCore.Clock.UtcNow;
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
            LogService.Error("InsertNoteAsync failed (guid={DesktopGuid})", desktopGuid, ex);
            throw;
        }
        finally
        {
            DatabaseCore.ReleaseWriteLock();
        }
    }

    /// <summary>
    /// Updates a note's content and sets updated_at to now.
    /// </summary>
    public static async Task UpdateNoteContentAsync(long noteId, string content)
    {
        await DatabaseCore.AcquireWriteLockAsync().ConfigureAwait(false);
        try
        {
            using var context = DatabaseCore.CreateContext();
            var now = DatabaseCore.Clock.UtcNow;
            await context.Notes
                .Where(n => n.Id == noteId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(n => n.Content, content)
                    .SetProperty(n => n.UpdatedAt, now)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogService.Error("UpdateNoteContentAsync failed (id={NoteId})", noteId, ex);
            throw;
        }
        finally
        {
            DatabaseCore.ReleaseWriteLock();
        }
    }

    /// <summary>
    /// Updates a note's custom name. Pass null to clear the name.
    /// </summary>
    public static async Task UpdateNoteNameAsync(long noteId, string? name)
    {
        await DatabaseCore.AcquireWriteLockAsync().ConfigureAwait(false);
        try
        {
            using var context = DatabaseCore.CreateContext();
            var now = DatabaseCore.Clock.UtcNow;
            await context.Notes
                .Where(n => n.Id == noteId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(n => n.Name, name)
                    .SetProperty(n => n.UpdatedAt, now)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogService.Error("UpdateNoteNameAsync failed (id={NoteId})", noteId, ex);
            throw;
        }
        finally
        {
            DatabaseCore.ReleaseWriteLock();
        }
    }

    /// <summary>
    /// Updates a note's pinned status.
    /// </summary>
    public static async Task UpdateNotePinnedAsync(long noteId, bool pinned)
    {
        await DatabaseCore.AcquireWriteLockAsync().ConfigureAwait(false);
        try
        {
            using var context = DatabaseCore.CreateContext();
            var now = DatabaseCore.Clock.UtcNow;
            await context.Notes
                .Where(n => n.Id == noteId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(n => n.Pinned, pinned)
                    .SetProperty(n => n.UpdatedAt, now)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogService.Error("UpdateNotePinnedAsync failed (id={NoteId})", noteId, ex);
            throw;
        }
        finally
        {
            DatabaseCore.ReleaseWriteLock();
        }
    }

    /// <summary>
    /// Batch-updates sort_order for multiple notes.
    /// </summary>
    public static async Task UpdateNoteSortOrdersAsync(IEnumerable<(long Id, int SortOrder)> updates)
    {
        await DatabaseCore.AcquireWriteLockAsync().ConfigureAwait(false);
        try
        {
            using var context = DatabaseCore.CreateContext();
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
            DatabaseCore.ReleaseWriteLock();
        }
    }

    /// <summary>
    /// Deletes a note by ID.
    /// </summary>
    public static async Task DeleteNoteAsync(long noteId)
    {
        await DatabaseCore.AcquireWriteLockAsync().ConfigureAwait(false);
        try
        {
            using var context = DatabaseCore.CreateContext();
            await context.Notes
                .Where(n => n.Id == noteId)
                .ExecuteDeleteAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogService.Error("DeleteNoteAsync failed (id={NoteId})", noteId, ex);
            throw;
        }
        finally
        {
            DatabaseCore.ReleaseWriteLock();
        }
    }

    /// <summary>
    /// Deletes all notes with empty or whitespace-only content for a given desktop.
    /// Pinned notes are preserved. Returns the number deleted.
    /// </summary>
    public static async Task<int> DeleteEmptyNotesAsync(string desktopGuid)
    {
        await DatabaseCore.AcquireWriteLockAsync().ConfigureAwait(false);
        try
        {
            using var context = DatabaseCore.CreateContext();
            int deleted = await context.Notes
                .Where(n => n.DesktopGuid == desktopGuid
                    && (n.Content == null || n.Content.Trim() == "")
                    && !n.Pinned)
                .ExecuteDeleteAsync().ConfigureAwait(false);
            if (deleted > 0)
                LogService.Info("Startup cleanup: deleted {DeletedCount} empty note(s) for desktop {DesktopGuid}", deleted, desktopGuid);
            return deleted;
        }
        catch (Exception ex)
        {
            LogService.Error("DeleteEmptyNotesAsync failed (guid={DesktopGuid})", desktopGuid, ex);
            return 0;
        }
        finally
        {
            DatabaseCore.ReleaseWriteLock();
        }
    }

    /// <summary>
    /// Returns the maximum sort_order for notes in a desktop, or -1 if no notes exist.
    /// </summary>
    public static async Task<int> GetMaxSortOrderAsync(string desktopGuid, CancellationToken ct = default)
    {
        await DatabaseCore.AcquireWriteLockAsync(ct).ConfigureAwait(false);
        try
        {
            using var context = DatabaseCore.CreateContext();
            var maxOrder = await context.Notes
                .Where(n => n.DesktopGuid == desktopGuid)
                .Select(n => (int?)n.SortOrder)
                .MaxAsync(ct).ConfigureAwait(false);
            return maxOrder ?? -1;
        }
        catch (Exception ex)
        {
            LogService.Error("GetMaxSortOrderAsync failed (guid={DesktopGuid})", desktopGuid, ex);
            throw;
        }
        finally
        {
            DatabaseCore.ReleaseWriteLock();
        }
    }

    /// <summary>
    /// Returns the first N note names for a desktop, ordered by sort_order.
    /// </summary>
    public static async Task<List<string>> GetNoteNamesForDesktopAsync(string desktopGuid, int limit = 5, CancellationToken ct = default)
    {
        await DatabaseCore.AcquireWriteLockAsync(ct).ConfigureAwait(false);
        try
        {
            using var context = DatabaseCore.CreateContext();
            var names = await context.Database
                .SqlQueryRaw<string>(
                    "SELECT COALESCE(NULLIF(name, ''), SUBSTR(content, 1, 30)) AS [Value] FROM notes WHERE desktop_guid = {0} ORDER BY sort_order ASC LIMIT {1}",
                    desktopGuid, limit)
                .ToListAsync(ct).ConfigureAwait(false);

            return names.Select(n => n ?? "Empty note").ToList();
        }
        catch (Exception ex)
        {
            LogService.Error("GetNoteNamesForDesktopAsync failed (guid={DesktopGuid})", desktopGuid, ex);
            return [];
        }
        finally
        {
            DatabaseCore.ReleaseWriteLock();
        }
    }

    /// <summary>
    /// Returns tab previews (name + content excerpt) for the recovery panel.
    /// </summary>
    public static async Task<List<(string? Name, string Excerpt, DateTime CreatedAt, DateTime UpdatedAt)>> GetNotePreviewsForDesktopAsync(string desktopGuid, int limit = 5, CancellationToken ct = default)
    {
        await DatabaseCore.AcquireWriteLockAsync(ct).ConfigureAwait(false);
        try
        {
            using var context = DatabaseCore.CreateContext();
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
            LogService.Error("GetNotePreviewsForDesktopAsync failed (guid={DesktopGuid})", desktopGuid, ex);
            return [];
        }
        finally
        {
            DatabaseCore.ReleaseWriteLock();
        }
    }

    /// <summary>
    /// Returns total note count for a desktop GUID.
    /// </summary>
    public static async Task<int> GetNoteCountForDesktopAsync(string desktopGuid, CancellationToken ct = default)
    {
        await DatabaseCore.AcquireWriteLockAsync(ct).ConfigureAwait(false);
        try
        {
            using var context = DatabaseCore.CreateContext();
            return await context.Notes.CountAsync(n => n.DesktopGuid == desktopGuid, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogService.Error("GetNoteCountForDesktopAsync failed (guid={DesktopGuid})", desktopGuid, ex);
            return 0;
        }
        finally
        {
            DatabaseCore.ReleaseWriteLock();
        }
    }

    /// <summary>
    /// Updates all notes from one desktop_guid to another, preserving sort_order and pin state.
    /// </summary>
    public static async Task MigrateNotesDesktopGuidAsync(string fromGuid, string toGuid)
    {
        await DatabaseCore.AcquireWriteLockAsync().ConfigureAwait(false);
        try
        {
            using var context = DatabaseCore.CreateContext();
            int affected = await context.Notes
                .Where(n => n.DesktopGuid == fromGuid)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(n => n.DesktopGuid, toGuid)).ConfigureAwait(false);
            LogService.Info("Reparented {Affected} notes from {FromGuid} to {ToGuid}", affected, fromGuid, toGuid);
        }
        catch (Exception ex)
        {
            LogService.Error("MigrateNotesDesktopGuidAsync failed (from={FromGuid}, to={ToGuid})", fromGuid, toGuid, ex);
            throw;
        }
        finally
        {
            DatabaseCore.ReleaseWriteLock();
        }
    }

    /// <summary>
    /// Migrates all notes from sourceGuid to targetGuid.
    /// Reassigns sort_order after target's max. Pinned tabs are un-pinned.
    /// </summary>
    public static async Task MigrateTabsAsync(string sourceGuid, string targetGuid)
    {
        await DatabaseCore.AcquireWriteLockAsync().ConfigureAwait(false);
        try
        {
            using var context = DatabaseCore.CreateContext();
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
                tab.Pinned = false;
                tab.SortOrder = baseOrder + tab.SortOrder;
            }
            await context.SaveChangesAsync().ConfigureAwait(false);

            LogService.Info("Migrated tabs from {SourceGuid} to {TargetGuid} (base sort_order: {BaseOrder})", sourceGuid, targetGuid, baseOrder);
        }
        catch (Exception ex)
        {
            LogService.Error("MigrateTabsAsync failed (source={SourceGuid}, target={TargetGuid})", sourceGuid, targetGuid, ex);
            throw;
        }
        finally
        {
            DatabaseCore.ReleaseWriteLock();
        }
    }

    /// <summary>
    /// Migrates tabs from source to target desktop, preserving pin state.
    /// </summary>
    public static async Task MigrateTabsPreservePinsAsync(string sourceGuid, string targetGuid)
    {
        await DatabaseCore.AcquireWriteLockAsync().ConfigureAwait(false);
        try
        {
            using var context = DatabaseCore.CreateContext();
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

            LogService.Info("Migrated tabs (preserving pins) from {SourceGuid} to {TargetGuid} (base sort_order: {BaseOrder})", sourceGuid, targetGuid, baseOrder);
        }
        catch (Exception ex)
        {
            LogService.Error("MigrateTabsPreservePinsAsync failed (source={SourceGuid}, target={TargetGuid})", sourceGuid, targetGuid, ex);
            throw;
        }
        finally
        {
            DatabaseCore.ReleaseWriteLock();
        }
    }
}
