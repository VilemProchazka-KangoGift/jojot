using JoJot.Models;
using Microsoft.Data.Sqlite;

namespace JoJot.Services;

/// <summary>CRUD operations for the notes table.</summary>
public static class NoteStore
{
    /// <summary>
    /// Loads all notes for a desktop, ordered by pinned DESC then sort_order ASC.
    /// </summary>
    public static Task<List<NoteTab>> GetNotesForDesktopAsync(string desktopGuid, CancellationToken ct = default) =>
        DatabaseCore.RunOnDbThreadAsync(() => GetNotesForDesktopCoreAsync(desktopGuid, ct));

    /// <summary>
    /// Inserts a new note and returns its auto-generated ID.
    /// </summary>
    public static Task<long> InsertNoteAsync(string desktopGuid, string? name, string content, bool pinned, int sortOrder, string? filePath = null) =>
        DatabaseCore.RunOnDbThreadAsync(() => InsertNoteCoreAsync(desktopGuid, name, content, pinned, sortOrder, filePath));

    /// <summary>
    /// Updates a note's content and sets updated_at to now.
    /// </summary>
    public static Task UpdateNoteContentAsync(long noteId, string content) => UpdateWithTimestampAsync(
        "UPDATE notes SET content = @value, updated_at = @now WHERE id = @id", noteId, content, "UpdateNoteContentAsync");
    /// <summary>
    /// Updates a note's custom name. Pass null to clear the name.
    /// </summary>
    public static Task UpdateNoteNameAsync(long noteId, string? name) => UpdateWithTimestampAsync(
        "UPDATE notes SET name = @value, updated_at = @now WHERE id = @id", noteId, name, "UpdateNoteNameAsync");
    /// <summary>
    /// Updates a note's pinned status.
    /// </summary>
    public static Task UpdateNotePinnedAsync(long noteId, bool pinned) => UpdateWithTimestampAsync(
        "UPDATE notes SET pinned = @value, updated_at = @now WHERE id = @id", noteId, pinned, "UpdateNotePinnedAsync");

    private static Task UpdateWithTimestampAsync(string sql, long id, object? value, string operation) =>
        DatabaseCore.RunOnDbThreadAsync(() => UpdateWithTimestampCoreAsync(sql, id, value, operation));

    /// <summary>
    /// Updates a note's file path and name in a single DB round-trip.
    /// </summary>
    public static Task UpdateNoteFilePathAndNameAsync(long noteId, string? filePath, string? name) =>
        DatabaseCore.RunOnDbThreadAsync(() => UpdateNoteFilePathAndNameCoreAsync(noteId, filePath, name));

    /// <summary>
    /// Batch-updates sort_order atomically for multiple notes.
    /// </summary>
    public static Task UpdateNoteSortOrdersAsync(IEnumerable<(long Id, int SortOrder)> updates)
    {
        List<(long Id, int SortOrder)> list = [.. updates];
        if (list.Count == 0) return Task.CompletedTask;
        return DatabaseCore.RunOnDbThreadAsync(() => UpdateNoteSortOrdersCoreAsync(list));
    }

    /// <summary>
    /// Deletes a note by ID.
    /// </summary>
    public static Task DeleteNoteAsync(long noteId) => DatabaseCore.RunOnDbThreadAsync(() => DeleteNoteCoreAsync(noteId));

    /// <summary>Deletes multiple notes atomically. Returns the number of rows removed.</summary>
    public static Task<int> DeleteNotesAsync(IReadOnlyCollection<long> noteIds)
    {
        long[] snapshot = [.. noteIds];
        return DatabaseCore.RunOnDbThreadAsync(() => DeleteNotesCoreAsync(snapshot));
    }

    /// <summary>
    /// Deletes all unpinned notes older than the specified cutoff date across all desktops.
    /// Returns the number deleted.
    /// </summary>
    public static Task<int> DeleteOldNotesAsync(DateTime cutoff) => DatabaseCore.RunOnDbThreadAsync(() => DeleteOldNotesCoreAsync(cutoff));

    /// <summary>
    /// Returns the maximum sort_order for notes in a desktop, or -1 if no notes exist.
    /// </summary>
    public static Task<int> GetMaxSortOrderAsync(string desktopGuid, CancellationToken ct = default) => DatabaseCore.RunOnDbThreadAsync(() => GetMaxSortOrderCoreAsync(desktopGuid, ct));

    /// <summary>
    /// Returns the first N note names for a desktop, ordered by sort_order.
    /// </summary>
    public static Task<List<string>> GetNoteNamesForDesktopAsync(string desktopGuid, int limit = 5, CancellationToken ct = default) => DatabaseCore.RunOnDbThreadAsync(() => GetNoteNamesForDesktopCoreAsync(desktopGuid, limit, ct));

    /// <summary>
    /// Returns tab previews (name + content excerpt) for the recovery panel.
    /// </summary>
    public static Task<List<(string? Name, string Excerpt, DateTime CreatedAt, DateTime UpdatedAt)>> GetNotePreviewsForDesktopAsync(string desktopGuid, int limit = 5, CancellationToken ct = default) => DatabaseCore.RunOnDbThreadAsync(() => GetNotePreviewsForDesktopCoreAsync(desktopGuid, limit, ct));

    /// <summary>
    /// Returns total note count for a desktop GUID.
    /// </summary>
    public static Task<int> GetNoteCountForDesktopAsync(string desktopGuid, CancellationToken ct = default) => DatabaseCore.RunOnDbThreadAsync(() => GetNoteCountForDesktopCoreAsync(desktopGuid, ct));

    /// <summary>Returns note counts for many desktops in one grouped query (recovery panel).</summary>
    public static Task<Dictionary<string, int>> GetNoteCountsForDesktopsAsync(IReadOnlyCollection<string> desktopGuids)
    {
        string[] snapshot = [.. desktopGuids];
        return DatabaseCore.RunOnDbThreadAsync(() => GetNoteCountsForDesktopsCoreAsync(snapshot));
    }

    /// <summary>
    /// Updates all notes from one desktop_guid to another, preserving sort_order and pin state.
    /// </summary>
    public static Task MigrateNotesDesktopGuidAsync(string fromGuid, string toGuid) => RunMigrationAsync(
        "UPDATE notes SET desktop_guid = @toGuid WHERE desktop_guid = @fromGuid", fromGuid, toGuid, "MigrateNotesDesktopGuidAsync");

    /// <summary>
    /// Migrates all notes from sourceGuid to targetGuid.
    /// Reassigns sort_order after target's max. Pinned tabs are un-pinned.
    /// </summary>
    public static Task MigrateTabsAsync(string sourceGuid, string targetGuid) => MigrateTabsCoreAsync(sourceGuid, targetGuid, preservePins: false);
    /// <summary>
    /// Migrates tabs from source to target desktop, preserving pin state.
    /// </summary>
    public static Task MigrateTabsPreservePinsAsync(string sourceGuid, string targetGuid) => MigrateTabsCoreAsync(sourceGuid, targetGuid, preservePins: true);

    private static Task MigrateTabsCoreAsync(string sourceGuid, string targetGuid, bool preservePins) => DatabaseCore.RunOnDbThreadAsync(() => MigrateTabsOnDbThreadCoreAsync(sourceGuid, targetGuid, preservePins));

    private static Task RunMigrationAsync(string sql, string fromGuid, string toGuid, string operation) => DatabaseCore.RunOnDbThreadAsync(() => RunMigrationCoreAsync(sql, fromGuid, toGuid, operation));

    private static async Task<List<NoteTab>> GetNotesForDesktopCoreAsync(string desktopGuid, CancellationToken ct)
    {
        try
        {
            await using var cmd = DatabaseCore.CreateCommand(
                "SELECT id, desktop_guid, name, content, pinned, created_at, updated_at, sort_order, editor_scroll_offset, cursor_position, file_path FROM notes WHERE desktop_guid = @guid ORDER BY pinned DESC, sort_order ASC",
                ("@guid", desktopGuid));
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            var notes = new List<NoteTab>();
            while (await reader.ReadAsync(ct).ConfigureAwait(false)) notes.Add(ReadNote(reader));
            return notes;
        }
        catch (Exception ex)
        {
            LogService.Error("GetNotesForDesktopAsync failed (guid={DesktopGuid})", desktopGuid, ex);
            throw;
        }
    }

    private static async Task<long> InsertNoteCoreAsync(string desktopGuid, string? name, string content, bool pinned, int sortOrder, string? filePath)
    {
        await DatabaseCore.AcquireWriteLockAsync().ConfigureAwait(false);
        try
        {
            var now = DatabaseCore.Clock.Now;
            await using var cmd = DatabaseCore.CreateCommand(
                "INSERT INTO notes (desktop_guid, name, content, pinned, sort_order, file_path, created_at, updated_at) VALUES (@guid, @name, @content, @pinned, @sortOrder, @filePath, @createdAt, @updatedAt); SELECT last_insert_rowid();",
                ("@guid", desktopGuid), ("@name", name), ("@content", content), ("@pinned", pinned), ("@sortOrder", sortOrder),
                ("@filePath", filePath), ("@createdAt", now), ("@updatedAt", now));
            return Convert.ToInt64(await cmd.ExecuteScalarAsync().ConfigureAwait(false));
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

    private static async Task UpdateWithTimestampCoreAsync(string sql, long id, object? value, string operation)
    {
        await DatabaseCore.AcquireWriteLockAsync().ConfigureAwait(false);
        try
        {
            await using var cmd = DatabaseCore.CreateCommand(sql, ("@value", value), ("@now", DatabaseCore.Clock.Now), ("@id", id));
            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogService.Error(operation + " failed (id={NoteId})", id, ex);
            throw;
        }
        finally
        {
            DatabaseCore.ReleaseWriteLock();
        }
    }

    private static async Task UpdateNoteFilePathAndNameCoreAsync(long noteId, string? filePath, string? name)
    {
        await DatabaseCore.AcquireWriteLockAsync().ConfigureAwait(false);
        try
        {
            await using var cmd = DatabaseCore.CreateCommand("UPDATE notes SET file_path = @filePath, name = @name WHERE id = @id",
                ("@filePath", filePath), ("@name", name), ("@id", noteId));
            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
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

    private static async Task UpdateNoteSortOrdersCoreAsync(List<(long Id, int SortOrder)> list)
    {
        await DatabaseCore.AcquireWriteLockAsync().ConfigureAwait(false);
        try
        {
            // Preserve CASE semantics: the first requested value wins when an id is repeated.
            var first = list.DistinctBy(x => x.Id).ToArray();
            await using var anchor = DatabaseCore.CreateCommand("SELECT 1");
            await using var tx = anchor.Connection!.BeginTransaction();
            foreach (var chunk in first.Chunk(300))
            {
                var sql = new System.Text.StringBuilder("UPDATE notes SET sort_order = CASE id ");
                var parameters = new List<(string name, object? value)>();
                for (int i = 0; i < chunk.Length; i++)
                {
                    sql.Append("WHEN @id").Append(i).Append(" THEN @sort").Append(i).Append(' ');
                    parameters.Add(("@id" + i, chunk[i].Id));
                    parameters.Add(("@sort" + i, chunk[i].SortOrder));
                }
                sql.Append("END WHERE id IN (");
                for (int i = 0; i < chunk.Length; i++)
                {
                    if (i > 0) sql.Append(',');
                    sql.Append("@id").Append(i);
                }
                sql.Append(')');
                await using var cmd = DatabaseCore.CreateCommand(sql.ToString(), [.. parameters]);
                cmd.Transaction = tx;
                await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
            await tx.CommitAsync().ConfigureAwait(false);
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

    private static async Task DeleteNoteCoreAsync(long noteId)
    {
        await DatabaseCore.AcquireWriteLockAsync().ConfigureAwait(false);
        try
        {
            await using var cmd = DatabaseCore.CreateCommand("DELETE FROM notes WHERE id = @id", ("@id", noteId));
            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
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

    private static async Task<int> DeleteNotesCoreAsync(long[] snapshot)
    {
        if (snapshot.Length == 0) return 0;
        await DatabaseCore.AcquireWriteLockAsync().ConfigureAwait(false);
        try
        {
            int deleted = 0;
            await using var anchor = DatabaseCore.CreateCommand("SELECT 1");
            await using var tx = anchor.Connection!.BeginTransaction();
            for (int start = 0; start < snapshot.Length; start += 900)
            {
                var chunk = snapshot.Skip(start).Take(900).ToArray();
                var sql = "DELETE FROM notes WHERE id IN (" + string.Join(',', Enumerable.Range(0, chunk.Length).Select(i => "@id" + i)) + ")";
                var parameters = chunk.Select((id, i) => ("@id" + i, (object?)id)).ToArray();
                await using var cmd = DatabaseCore.CreateCommand(sql, parameters);
                cmd.Transaction = tx;
                deleted += await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
            await tx.CommitAsync().ConfigureAwait(false);
            return deleted;
        }
        catch (Exception ex)
        {
            LogService.Error("DeleteNotesAsync failed ({Count} ids)", snapshot.Length, ex);
            throw;
        }
        finally
        {
            DatabaseCore.ReleaseWriteLock();
        }
    }

    private static async Task<int> DeleteOldNotesCoreAsync(DateTime cutoff)
    {
        await DatabaseCore.AcquireWriteLockAsync().ConfigureAwait(false);
        try
        {
            await using var cmd = DatabaseCore.CreateCommand("DELETE FROM notes WHERE updated_at < @cutoff AND pinned = 0", ("@cutoff", cutoff));
            return await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
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

    private static async Task<int> GetMaxSortOrderCoreAsync(string desktopGuid, CancellationToken ct)
    {
        try
        {
            await using var cmd = DatabaseCore.CreateCommand("SELECT MAX(sort_order) FROM notes WHERE desktop_guid = @guid", ("@guid", desktopGuid));
            var value = await cmd.ExecuteScalarAsync().ConfigureAwait(false);
            return value is null or DBNull ? -1 : Convert.ToInt32(value);
        }
        catch (Exception ex)
        {
            LogService.Error("GetMaxSortOrderAsync failed (guid={DesktopGuid})", desktopGuid, ex);
            throw;
        }
    }

    private static async Task<List<string>> GetNoteNamesForDesktopCoreAsync(string desktopGuid, int limit, CancellationToken ct)
    {
        try
        {
            await using var cmd = DatabaseCore.CreateCommand("SELECT COALESCE(NULLIF(name, ''), SUBSTR(content, 1, 30)) FROM notes WHERE desktop_guid = @guid ORDER BY sort_order ASC LIMIT @limit", ("@guid", desktopGuid), ("@limit", limit));
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            var result = new List<string>();
            while (await reader.ReadAsync(ct).ConfigureAwait(false)) result.Add(reader.IsDBNull(0) ? Resources.Strings.Recovery_EmptyNote : reader.GetString(0));
            return result;
        }
        catch (Exception ex)
        {
            LogService.Error("GetNoteNamesForDesktopAsync failed (guid={DesktopGuid})", desktopGuid, ex);
            return [];
        }
    }

    private static async Task<List<(string? Name, string Excerpt, DateTime CreatedAt, DateTime UpdatedAt)>> GetNotePreviewsForDesktopCoreAsync(string desktopGuid, int limit, CancellationToken ct)
    {
        try
        {
            await using var cmd = DatabaseCore.CreateCommand("SELECT name, content, created_at, updated_at FROM notes WHERE desktop_guid = @guid ORDER BY sort_order ASC LIMIT @limit", ("@guid", desktopGuid), ("@limit", limit));
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            var results = new List<(string?, string, DateTime, DateTime)>();
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                string? name = reader.IsDBNull(0) ? null : reader.GetString(0);
                if (string.IsNullOrWhiteSpace(name)) name = null;
                var content = reader.GetString(1);
                var excerpt = content.Length > 60 ? content[..60] : content;
                results.Add((name, excerpt, reader.GetDateTime(2), reader.GetDateTime(3)));
            }
            return results;
        }
        catch (Exception ex)
        {
            LogService.Error("GetNotePreviewsForDesktopAsync failed (guid={DesktopGuid})", desktopGuid, ex);
            return [];
        }
    }

    private static async Task<int> GetNoteCountForDesktopCoreAsync(string desktopGuid, CancellationToken ct)
    {
        try
        {
            await using var cmd = DatabaseCore.CreateCommand("SELECT COUNT(*) FROM notes WHERE desktop_guid = @guid", ("@guid", desktopGuid));
            return Convert.ToInt32(await cmd.ExecuteScalarAsync().ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            LogService.Error("GetNoteCountForDesktopAsync failed (guid={DesktopGuid})", desktopGuid, ex);
            return 0;
        }
    }

    private static async Task<Dictionary<string, int>> GetNoteCountsForDesktopsCoreAsync(string[] snapshot)
    {
        if (snapshot.Length == 0) return [];
        try
        {
            var result = new Dictionary<string, int>();
            foreach (var chunk in snapshot.Distinct().Chunk(900))
            {
                var sql = "SELECT desktop_guid, COUNT(*) FROM notes WHERE desktop_guid IN (" + string.Join(',', Enumerable.Range(0, chunk.Length).Select(i => "@guid" + i)) + ") GROUP BY desktop_guid";
                var parameters = chunk.Select((guid, i) => ("@guid" + i, (object?)guid)).ToArray();
                await using var cmd = DatabaseCore.CreateCommand(sql, parameters);
                await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
                while (await reader.ReadAsync().ConfigureAwait(false)) result[reader.GetString(0)] = reader.GetInt32(1);
            }
            return result;
        }
        catch (Exception ex)
        {
            LogService.Error("GetNoteCountsForDesktopsAsync failed ({Count} guids)", snapshot.Length, ex);
            return [];
        }
    }

    private static async Task MigrateTabsOnDbThreadCoreAsync(string sourceGuid, string targetGuid, bool preservePins)
    {
        await DatabaseCore.AcquireWriteLockAsync().ConfigureAwait(false);
        try
        {
            await using var maxCmd = DatabaseCore.CreateCommand("SELECT MAX(sort_order) FROM notes WHERE desktop_guid = @guid", ("@guid", targetGuid));
            var value = await maxCmd.ExecuteScalarAsync().ConfigureAwait(false);
            int baseOrder = (value is null or DBNull ? 0 : Convert.ToInt32(value)) + 1;
            string sql = preservePins
                ? "UPDATE notes SET desktop_guid = @target, sort_order = sort_order + @base WHERE desktop_guid = @source"
                : "UPDATE notes SET desktop_guid = @target, pinned = 0, sort_order = sort_order + @base WHERE desktop_guid = @source";
            await using var cmd = DatabaseCore.CreateCommand(sql, ("@target", targetGuid), ("@base", baseOrder), ("@source", sourceGuid));
            int affected = await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            LogService.Info(preservePins ? "Migrated {Affected} tabs (preserving pins) from {SourceGuid} to {TargetGuid} (base sort_order: {BaseOrder})" : "Migrated {Affected} tabs from {SourceGuid} to {TargetGuid} (base sort_order: {BaseOrder})", affected, sourceGuid, targetGuid, baseOrder);
        }
        catch (Exception ex)
        {
            LogService.Error(preservePins ? "MigrateTabsPreservePinsAsync failed (source={SourceGuid}, target={TargetGuid})" : "MigrateTabsAsync failed (source={SourceGuid}, target={TargetGuid})", sourceGuid, targetGuid, ex);
            throw;
        }
        finally
        {
            DatabaseCore.ReleaseWriteLock();
        }
    }

    private static async Task RunMigrationCoreAsync(string sql, string fromGuid, string toGuid, string operation)
    {
        await DatabaseCore.AcquireWriteLockAsync().ConfigureAwait(false);
        try
        {
            await using var cmd = DatabaseCore.CreateCommand(sql, ("@toGuid", toGuid), ("@fromGuid", fromGuid));
            int affected = await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            LogService.Info("Reparented {Affected} notes from {FromGuid} to {ToGuid}", affected, fromGuid, toGuid);
        }
        catch (Exception ex)
        {
            LogService.Error(operation + " failed (from={FromGuid}, to={ToGuid})", fromGuid, toGuid, ex);
            throw;
        }
        finally
        {
            DatabaseCore.ReleaseWriteLock();
        }
    }

    private static NoteTab ReadNote(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(0), DesktopGuid = reader.GetString(1), Name = reader.IsDBNull(2) ? null : reader.GetString(2),
        Content = reader.GetString(3), Pinned = reader.GetInt32(4) != 0, CreatedAt = reader.GetDateTime(5), UpdatedAt = reader.GetDateTime(6),
        SortOrder = reader.GetInt32(7), EditorScrollOffset = reader.GetInt32(8), CursorPosition = reader.GetInt32(9), FilePath = reader.IsDBNull(10) ? null : reader.GetString(10)
    };
}
