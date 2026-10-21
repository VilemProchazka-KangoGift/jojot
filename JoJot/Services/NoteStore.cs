using Microsoft.EntityFrameworkCore;
using JoJot.Data;
using JoJot.Models;

namespace JoJot.Services;

/// <summary>
/// CRUD operations for the notes table.
/// </summary>
public static class NoteStore
{
    // ─── Compiled queries (expression tree parsed once, reused on every call) ───

    private static readonly Func<JoJotDbContext, string, IAsyncEnumerable<NoteTab>>
        CompiledGetNotesForDesktop = EF.CompileAsyncQuery<JoJotDbContext, string, NoteTab>(
            (ctx, guid) =>
                ctx.Notes.AsNoTracking()
                    .Where(n => n.DesktopGuid == guid)
                    .OrderByDescending(n => n.Pinned)
                    .ThenBy(n => n.SortOrder));

    private static readonly Func<JoJotDbContext, string, Task<int>>
        CompiledGetNoteCount = EF.CompileAsyncQuery(
            (JoJotDbContext ctx, string guid) =>
                ctx.Notes.Count(n => n.DesktopGuid == guid));

    private static readonly Func<JoJotDbContext, string, Task<int?>>
        CompiledGetMaxSortOrder = EF.CompileAsyncQuery(
            (JoJotDbContext ctx, string guid) =>
                ctx.Notes.Where(n => n.DesktopGuid == guid)
                    .Select(n => (int?)n.SortOrder)
                    .Max());

    /// <summary>
    /// Loads all notes for a desktop, ordered by pinned DESC then sort_order ASC.
    /// </summary>
    public static Task<List<NoteTab>> GetNotesForDesktopAsync(string desktopGuid, CancellationToken ct = default) =>
        DatabaseCore.RunOnDbThreadAsync(() => GetNotesForDesktopCoreAsync(desktopGuid, ct));

    private static async Task<List<NoteTab>> GetNotesForDesktopCoreAsync(string desktopGuid, CancellationToken ct = default)
    {
        try
        {
            await using var context = DatabaseCore.CreateContext();
            var results = new List<NoteTab>();
            await foreach (var note in CompiledGetNotesForDesktop(context, desktopGuid).WithCancellation(ct).ConfigureAwait(false))
                results.Add(note);
            return results;
        }
        catch (Exception ex)
        {
            LogService.Error("GetNotesForDesktopAsync failed (guid={DesktopGuid})", desktopGuid, ex);
            throw;
        }
    }

    /// <summary>
    /// Inserts a new note and returns its auto-generated ID.
    /// </summary>
    public static Task<long> InsertNoteAsync(string desktopGuid, string? name, string content, bool pinned, int sortOrder, string? filePath = null) =>
        DatabaseCore.RunOnDbThreadAsync(() => InsertNoteCoreAsync(desktopGuid, name, content, pinned, sortOrder, filePath));

    private static async Task<long> InsertNoteCoreAsync(string desktopGuid, string? name, string content, bool pinned, int sortOrder, string? filePath = null)
    {
        await DatabaseCore.AcquireWriteLockAsync().ConfigureAwait(false);
        try
        {
            await using var context = DatabaseCore.CreateContext();
            var now = DatabaseCore.Clock.Now;
            var note = new NoteTab
            {
                DesktopGuid = desktopGuid,
                Name = name,
                Content = content,
                Pinned = pinned,
                SortOrder = sortOrder,
                FilePath = filePath,
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
    public static Task UpdateNoteContentAsync(long noteId, string content) =>
        DatabaseCore.RunOnDbThreadAsync(() => UpdateNoteContentCoreAsync(noteId, content));

    private static async Task UpdateNoteContentCoreAsync(long noteId, string content)
    {
        await DatabaseCore.AcquireWriteLockAsync().ConfigureAwait(false);
        try
        {
            await using var context = DatabaseCore.CreateContext();
            var now = DatabaseCore.Clock.Now;
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
    public static Task UpdateNoteNameAsync(long noteId, string? name) =>
        DatabaseCore.RunOnDbThreadAsync(() => UpdateNoteNameCoreAsync(noteId, name));

    private static async Task UpdateNoteNameCoreAsync(long noteId, string? name)
    {
        await DatabaseCore.AcquireWriteLockAsync().ConfigureAwait(false);
        try
        {
            await using var context = DatabaseCore.CreateContext();
            var now = DatabaseCore.Clock.Now;
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
    /// Updates a note's file path and name in a single DB round-trip.
    /// </summary>
    public static Task UpdateNoteFilePathAndNameAsync(long noteId, string? filePath, string? name) =>
        DatabaseCore.RunOnDbThreadAsync(() => UpdateNoteFilePathAndNameCoreAsync(noteId, filePath, name));

    private static async Task UpdateNoteFilePathAndNameCoreAsync(long noteId, string? filePath, string? name)
    {
        await DatabaseCore.AcquireWriteLockAsync().ConfigureAwait(false);
        try
        {
            await using var context = DatabaseCore.CreateContext();
            await context.Notes
                .Where(n => n.Id == noteId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(n => n.FilePath, filePath)
                    .SetProperty(n => n.Name, name)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogService.Error("UpdateNoteFilePathAndNameAsync failed (id={NoteId})", noteId, ex);
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
    public static Task UpdateNotePinnedAsync(long noteId, bool pinned) =>
        DatabaseCore.RunOnDbThreadAsync(() => UpdateNotePinnedCoreAsync(noteId, pinned));

    private static async Task UpdateNotePinnedCoreAsync(long noteId, bool pinned)
    {
        await DatabaseCore.AcquireWriteLockAsync().ConfigureAwait(false);
        try
        {
            await using var context = DatabaseCore.CreateContext();
            var now = DatabaseCore.Clock.Now;
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
    /// Batch-updates sort_order for multiple notes in a single SQL statement.
    /// </summary>
    public static Task UpdateNoteSortOrdersAsync(IEnumerable<(long Id, int SortOrder)> updates)
    {
        // Materialize on the caller's thread: a lazy query over the UI's tab collection must not be
        // enumerated later on the DB thread while the UI mutates it.
        List<(long Id, int SortOrder)> list = [.. updates];
        if (list.Count == 0) return Task.CompletedTask;
        return DatabaseCore.RunOnDbThreadAsync(() => UpdateNoteSortOrdersCoreAsync(list));
    }

    private static async Task UpdateNoteSortOrdersCoreAsync(List<(long Id, int SortOrder)> list)
    {

        await DatabaseCore.AcquireWriteLockAsync().ConfigureAwait(false);
        try
        {
            await using var context = DatabaseCore.CreateContext();

            // Build a single UPDATE ... SET sort_order = CASE ... END WHERE id IN (...)
            var sb = new System.Text.StringBuilder(64 + list.Count * 32);
            sb.Append("UPDATE notes SET sort_order = CASE id ");
            foreach (var (id, sortOrder) in list)
                sb.Append("WHEN ").Append(id).Append(" THEN ").Append(sortOrder).Append(' ');
            sb.Append("END WHERE id IN (");
            for (int i = 0; i < list.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(list[i].Id);
            }
            sb.Append(')');

            await context.Database.ExecuteSqlRawAsync(sb.ToString()).ConfigureAwait(false);
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
    public static Task DeleteNoteAsync(long noteId) =>
        DatabaseCore.RunOnDbThreadAsync(() => DeleteNoteCoreAsync(noteId));

    private static async Task DeleteNoteCoreAsync(long noteId)
    {
        await DatabaseCore.AcquireWriteLockAsync().ConfigureAwait(false);
        try
        {
            await using var context = DatabaseCore.CreateContext();
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

    /// <summary>Deletes multiple notes in a single statement. Returns the number of rows removed.</summary>
    public static Task<int> DeleteNotesAsync(IReadOnlyCollection<long> noteIds)
    {
        // Snapshot on the caller's thread — the delete runs later on the DB thread.
        long[] snapshot = [.. noteIds];
        return DatabaseCore.RunOnDbThreadAsync(() => DeleteNotesCoreAsync(snapshot));
    }

    private static async Task<int> DeleteNotesCoreAsync(IReadOnlyCollection<long> noteIds)
    {
        if (noteIds.Count == 0) return 0;

        await DatabaseCore.AcquireWriteLockAsync().ConfigureAwait(false);
        try
        {
            await using var context = DatabaseCore.CreateContext();
            return await context.Notes
                .Where(n => noteIds.Contains(n.Id))
                .ExecuteDeleteAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogService.Error("DeleteNotesAsync failed ({Count} ids)", noteIds.Count, ex);
            throw;
        }
        finally
        {
            DatabaseCore.ReleaseWriteLock();
        }
    }

    /// <summary>
    /// Deletes all unpinned notes older than the specified cutoff date across all desktops.
    /// Returns the number deleted.
    /// </summary>
    public static Task<int> DeleteOldNotesAsync(DateTime cutoff) =>
        DatabaseCore.RunOnDbThreadAsync(() => DeleteOldNotesCoreAsync(cutoff));

    private static async Task<int> DeleteOldNotesCoreAsync(DateTime cutoff)
    {
        await DatabaseCore.AcquireWriteLockAsync().ConfigureAwait(false);
        try
        {
            await using var context = DatabaseCore.CreateContext();
            return await context.Notes
                .Where(n => n.UpdatedAt < cutoff && !n.Pinned)
                .ExecuteDeleteAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogService.Error("DeleteOldNotesAsync failed (cutoff={Cutoff})", cutoff, ex);
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
    public static Task<int> GetMaxSortOrderAsync(string desktopGuid, CancellationToken ct = default) =>
        DatabaseCore.RunOnDbThreadAsync(() => GetMaxSortOrderCoreAsync(desktopGuid, ct));

    private static async Task<int> GetMaxSortOrderCoreAsync(string desktopGuid, CancellationToken ct = default)
    {
        try
        {
            await using var context = DatabaseCore.CreateContext();
            return await CompiledGetMaxSortOrder(context, desktopGuid).ConfigureAwait(false) ?? -1;
        }
        catch (Exception ex)
        {
            LogService.Error("GetMaxSortOrderAsync failed (guid={DesktopGuid})", desktopGuid, ex);
            throw;
        }
    }

    /// <summary>
    /// Returns the first N note names for a desktop, ordered by sort_order.
    /// </summary>
    public static Task<List<string>> GetNoteNamesForDesktopAsync(string desktopGuid, int limit = 5, CancellationToken ct = default) =>
        DatabaseCore.RunOnDbThreadAsync(() => GetNoteNamesForDesktopCoreAsync(desktopGuid, limit, ct));

    private static async Task<List<string>> GetNoteNamesForDesktopCoreAsync(string desktopGuid, int limit = 5, CancellationToken ct = default)
    {
        try
        {
            await using var context = DatabaseCore.CreateContext();
            var names = await context.Database
                .SqlQueryRaw<string>(
                    "SELECT COALESCE(NULLIF(name, ''), SUBSTR(content, 1, 30)) AS [Value] FROM notes WHERE desktop_guid = {0} ORDER BY sort_order ASC LIMIT {1}",
                    desktopGuid, limit)
                .ToListAsync(ct).ConfigureAwait(false);

            return names.Select(n => n ?? Resources.Strings.Recovery_EmptyNote).ToList();
        }
        catch (Exception ex)
        {
            LogService.Error("GetNoteNamesForDesktopAsync failed (guid={DesktopGuid})", desktopGuid, ex);
            return [];
        }
    }

    /// <summary>
    /// Returns tab previews (name + content excerpt) for the recovery panel.
    /// </summary>
    public static Task<List<(string? Name, string Excerpt, DateTime CreatedAt, DateTime UpdatedAt)>> GetNotePreviewsForDesktopAsync(string desktopGuid, int limit = 5, CancellationToken ct = default) =>
        DatabaseCore.RunOnDbThreadAsync(() => GetNotePreviewsForDesktopCoreAsync(desktopGuid, limit, ct));

    private static async Task<List<(string? Name, string Excerpt, DateTime CreatedAt, DateTime UpdatedAt)>> GetNotePreviewsForDesktopCoreAsync(string desktopGuid, int limit = 5, CancellationToken ct = default)
    {
        try
        {
            await using var context = DatabaseCore.CreateContext();
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
    }

    /// <summary>
    /// Returns total note count for a desktop GUID.
    /// </summary>
    public static Task<int> GetNoteCountForDesktopAsync(string desktopGuid, CancellationToken ct = default) =>
        DatabaseCore.RunOnDbThreadAsync(() => GetNoteCountForDesktopCoreAsync(desktopGuid, ct));

    private static async Task<int> GetNoteCountForDesktopCoreAsync(string desktopGuid, CancellationToken ct = default)
    {
        try
        {
            await using var context = DatabaseCore.CreateContext();
            return await CompiledGetNoteCount(context, desktopGuid).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogService.Error("GetNoteCountForDesktopAsync failed (guid={DesktopGuid})", desktopGuid, ex);
            return 0;
        }
    }

    /// <summary>Returns note counts for many desktops in one grouped query (recovery panel).</summary>
    public static Task<Dictionary<string, int>> GetNoteCountsForDesktopsAsync(
        IReadOnlyCollection<string> desktopGuids)
    {
        // Snapshot on the caller's thread — the query runs later on the DB thread.
        string[] snapshot = [.. desktopGuids];
        return DatabaseCore.RunOnDbThreadAsync(() => GetNoteCountsForDesktopsCoreAsync(snapshot));
    }

    private static async Task<Dictionary<string, int>> GetNoteCountsForDesktopsCoreAsync(
        IReadOnlyCollection<string> desktopGuids)
    {
        if (desktopGuids.Count == 0) return [];

        try
        {
            await using var context = DatabaseCore.CreateContext();
            return await context.Notes
                .AsNoTracking()
                .Where(n => desktopGuids.Contains(n.DesktopGuid))
                .GroupBy(n => n.DesktopGuid)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Count).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Degrade like the single-desktop GetNoteCountForDesktopAsync: a transient DB error
            // shows zero counts (callers use GetValueOrDefault) rather than breaking the recovery panel.
            LogService.Error("GetNoteCountsForDesktopsAsync failed ({Count} guids)", desktopGuids.Count, ex);
            return [];
        }
    }

    /// <summary>
    /// Updates all notes from one desktop_guid to another, preserving sort_order and pin state.
    /// </summary>
    public static Task MigrateNotesDesktopGuidAsync(string fromGuid, string toGuid) =>
        DatabaseCore.RunOnDbThreadAsync(() => MigrateNotesDesktopGuidCoreAsync(fromGuid, toGuid));

    private static async Task MigrateNotesDesktopGuidCoreAsync(string fromGuid, string toGuid)
    {
        await DatabaseCore.AcquireWriteLockAsync().ConfigureAwait(false);
        try
        {
            await using var context = DatabaseCore.CreateContext();
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
    public static Task MigrateTabsAsync(string sourceGuid, string targetGuid) =>
        DatabaseCore.RunOnDbThreadAsync(() => MigrateTabsCoreAsync(sourceGuid, targetGuid));

    private static async Task MigrateTabsCoreAsync(string sourceGuid, string targetGuid)
    {
        await DatabaseCore.AcquireWriteLockAsync().ConfigureAwait(false);
        try
        {
            await using var context = DatabaseCore.CreateContext();
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
    public static Task MigrateTabsPreservePinsAsync(string sourceGuid, string targetGuid) =>
        DatabaseCore.RunOnDbThreadAsync(() => MigrateTabsPreservePinsCoreAsync(sourceGuid, targetGuid));

    private static async Task MigrateTabsPreservePinsCoreAsync(string sourceGuid, string targetGuid)
    {
        await DatabaseCore.AcquireWriteLockAsync().ConfigureAwait(false);
        try
        {
            await using var context = DatabaseCore.CreateContext();
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
