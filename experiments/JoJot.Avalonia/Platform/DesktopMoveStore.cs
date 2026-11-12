using JoJot.Services;

namespace JoJot.AvaloniaExperiment.Platform;

// Resolve the move and remove its recovery marker in one SQLite transaction. A marker
// left by an interruption therefore represents an unresolved decision, never a half move.
internal static class DesktopMoveStore
{
    internal static Task ResolveAsync(string source, string target, bool merge, string? name, int? index) =>
        DatabaseCore.RunOnDbThreadAsync(() => ResolveCoreAsync(source, target, merge, name, index));

    private static async Task ResolveCoreAsync(string source, string target, bool merge, string? name, int? index)
    {
        await DatabaseCore.AcquireWriteLockAsync().ConfigureAwait(false);
        try
        {
            await using var move = DatabaseCore.CreateCommand(
                "UPDATE notes SET desktop_guid=@target, sort_order=sort_order + " +
                "COALESCE((SELECT MAX(sort_order)+1 FROM notes WHERE desktop_guid=@target), 1) WHERE desktop_guid=@source",
                ("@target", target), ("@source", source));
            using var transaction = move.Connection!.BeginTransaction();
            move.Transaction = transaction;
            await move.ExecuteNonQueryAsync().ConfigureAwait(false);
            if (!merge)
            {
                await using var deleteTarget = DatabaseCore.CreateCommand("DELETE FROM app_state WHERE desktop_guid=@target", ("@target", target));
                deleteTarget.Transaction = transaction;
                await deleteTarget.ExecuteNonQueryAsync().ConfigureAwait(false);
                await using var update = DatabaseCore.CreateCommand(
                    "UPDATE app_state SET desktop_guid=@target, desktop_name=@name, desktop_index=@index WHERE desktop_guid=@source",
                    ("@target", target), ("@name", (object?)name ?? DBNull.Value), ("@index", (object?)index ?? DBNull.Value), ("@source", source));
                update.Transaction = transaction;
                await update.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
            await using var deletePending = DatabaseCore.CreateCommand("DELETE FROM pending_moves WHERE window_id=@source", ("@source", source));
            deletePending.Transaction = transaction;
            await deletePending.ExecuteNonQueryAsync().ConfigureAwait(false);
            transaction.Commit();
        }
        finally { DatabaseCore.ReleaseWriteLock(); }
    }
}
