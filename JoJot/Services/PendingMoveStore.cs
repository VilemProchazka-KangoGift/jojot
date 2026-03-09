using Microsoft.EntityFrameworkCore;
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
    public static async Task<long> InsertPendingMoveAsync(string windowId, string fromDesktop, string? toDesktop)
    {
        await DatabaseCore.AcquireWriteLockAsync().ConfigureAwait(false);
        try
        {
            await using var context = DatabaseCore.CreateContext();
            var move = new PendingMove(0, windowId, fromDesktop, toDesktop, DatabaseCore.Clock.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));
            context.PendingMoves.Add(move);
            await context.SaveChangesAsync().ConfigureAwait(false);

            LogService.Info("InsertPendingMove: id={MoveId}, window={WindowId}, from={FromDesktop}, to={ToDesktop}", move.Id, windowId, fromDesktop, toDesktop);
            return move.Id;
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
    public static async Task DeletePendingMoveAsync(string windowId)
    {
        await DatabaseCore.AcquireWriteLockAsync().ConfigureAwait(false);
        try
        {
            await using var context = DatabaseCore.CreateContext();
            int deleted = await context.PendingMoves
                .Where(p => p.WindowId == windowId)
                .ExecuteDeleteAsync().ConfigureAwait(false);
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
    public static async Task<List<PendingMove>> GetPendingMovesAsync(CancellationToken ct = default)
    {
        await using var context = DatabaseCore.CreateContext();
        return await context.PendingMoves
            .AsNoTracking()
            .ToListAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes all pending_moves rows after crash recovery resolves them.
    /// </summary>
    public static async Task DeleteAllPendingMovesAsync()
    {
        await DatabaseCore.AcquireWriteLockAsync().ConfigureAwait(false);
        try
        {
            await using var context = DatabaseCore.CreateContext();
            int deleted = await context.PendingMoves.ExecuteDeleteAsync().ConfigureAwait(false);
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
