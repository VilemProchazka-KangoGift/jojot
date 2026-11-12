using JoJot.Models;

namespace JoJot.Services;

/// <summary>
/// CRUD operations for the pending_moves table.
/// </summary>
public static class PendingMoveStore
{
    /// <summary>
    /// Inserts a pending_moves row when a window drag is detected.
    /// </summary>
    public static Task<long> InsertPendingMoveAsync(string windowId, string fromDesktop, string? toDesktop) =>
        DatabaseCore.RunOnDbThreadAsync(() => InsertPendingMoveCoreAsync(windowId, fromDesktop, toDesktop));

    private static async Task<long> InsertPendingMoveCoreAsync(string windowId, string fromDesktop, string? toDesktop)
    {
        await DatabaseCore.AcquireWriteLockAsync().ConfigureAwait(false);
        try
        {
            string detectedAt = DatabaseCore.Clock.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
            await using var cmd = DatabaseCore.CreateCommand(
                "INSERT INTO pending_moves (window_id, from_desktop, to_desktop, detected_at) VALUES (@window, @from, @to, @detected); SELECT last_insert_rowid();",
                ("@window", windowId), ("@from", fromDesktop), ("@to", (object?)toDesktop ?? DBNull.Value), ("@detected", detectedAt));
            long id = Convert.ToInt64(await cmd.ExecuteScalarAsync().ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
            LogService.Info("InsertPendingMove: id={MoveId}, window={WindowId}, from={FromDesktop}, to={ToDesktop}", id, windowId, fromDesktop, toDesktop);
            return id;
        }
        catch (Exception ex)
        {
            LogService.Error("InsertPendingMoveAsync failed (window={WindowId})", windowId, ex);
            throw;
        }
        finally
        {
            DatabaseCore.ReleaseWriteLock();
        }
    }

    /// <summary>
    /// Deletes the pending_moves row for a window after drag resolution.
    /// </summary>
    public static Task DeletePendingMoveAsync(string windowId) =>
        DatabaseCore.RunOnDbThreadAsync(() => DeletePendingMoveCoreAsync(windowId));

    private static async Task DeletePendingMoveCoreAsync(string windowId)
    {
        await DatabaseCore.AcquireWriteLockAsync().ConfigureAwait(false);
        try
        {
            await using var cmd = DatabaseCore.CreateCommand("DELETE FROM pending_moves WHERE window_id=@window;", ("@window", windowId));
            int deleted = await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            LogService.Info("DeletePendingMove: window={WindowId}, rows={Deleted}", windowId, deleted);
        }
        catch (Exception ex)
        {
            LogService.Error("DeletePendingMoveAsync failed (window={WindowId})", windowId, ex);
            throw;
        }
        finally
        {
            DatabaseCore.ReleaseWriteLock();
        }
    }

    /// <summary>
    /// Reads all pending_moves rows for crash recovery.
    /// </summary>
    public static Task<List<PendingMove>> GetPendingMovesAsync(CancellationToken ct = default) =>
        DatabaseCore.RunOnDbThreadAsync(() => GetPendingMovesCoreAsync(ct));

    private static async Task<List<PendingMove>> GetPendingMovesCoreAsync(CancellationToken ct = default)
    {
        await using var cmd = DatabaseCore.CreateCommand("SELECT id, window_id, from_desktop, to_desktop, detected_at FROM pending_moves;");
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var moves = new List<PendingMove>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            moves.Add(new PendingMove(reader.GetInt64(0), reader.GetString(1), reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetString(4)));
        return moves;
    }

    /// <summary>
    /// Deletes all pending_moves rows after crash recovery resolves them.
    /// </summary>
    public static Task DeleteAllPendingMovesAsync() =>
        DatabaseCore.RunOnDbThreadAsync(() => DeleteAllPendingMovesCoreAsync());

    private static async Task DeleteAllPendingMovesCoreAsync()
    {
        await DatabaseCore.AcquireWriteLockAsync().ConfigureAwait(false);
        try
        {
            await using var cmd = DatabaseCore.CreateCommand("DELETE FROM pending_moves;");
            int deleted = await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            LogService.Info("DeleteAllPendingMoves: rows={Deleted}", deleted);
        }
        catch (Exception ex)
        {
            LogService.Error("DeleteAllPendingMovesAsync failed", ex);
            throw;
        }
        finally
        {
            DatabaseCore.ReleaseWriteLock();
        }
    }
}
