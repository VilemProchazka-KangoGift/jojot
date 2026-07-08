using System.Diagnostics;

namespace JoJot.Services;

/// <summary>
/// Tracks fire-and-forget persistence work behind optimistic UI updates. Every background
/// write registers here so: failures are logged and surfaced via <see cref="WriteFailed"/>,
/// and shutdown paths can <see cref="DrainAsync"/> to guarantee no write is abandoned.
/// </summary>
public static class BackgroundWriter
{
    private static readonly Lock _lock = new();
    private static readonly HashSet<Task> _pending = [];

    /// <summary>Raised on a background thread when a write fails (payload: operation name).
    /// UI subscribers must dispatch before touching visuals.</summary>
    public static event Action<string>? WriteFailed;

    /// <summary>Number of writes currently in flight. Test hook.</summary>
    internal static int PendingCount
    {
        get { lock (_lock) return _pending.Count; }
    }

    /// <summary>Starts and tracks a background write. Exceptions are logged, surfaced via
    /// <see cref="WriteFailed"/>, and never rethrown.</summary>
    public static void Run(Func<Task> operation, string operationName)
    {
        Task task = ExecuteAsync(operation, operationName);
        lock (_lock)
        {
            _pending.Add(task);
        }
        task.ContinueWith(
            t => { lock (_lock) { _pending.Remove(t); } },
            TaskContinuationOptions.ExecuteSynchronously);
    }

    private static async Task ExecuteAsync(Func<Task> operation, string operationName)
    {
        try
        {
            await operation().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogService.Error("Background write failed: {Operation}", operationName, ex);
            WriteFailed?.Invoke(operationName);
        }
    }

    /// <summary>
    /// Waits for all pending writes (including ones queued while draining) up to the timeout.
    /// Returns false if the timeout elapsed with work still in flight.
    /// </summary>
    public static async Task<bool> DrainAsync(TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (true)
        {
            Task[] snapshot;
            lock (_lock)
            {
                snapshot = [.. _pending];
            }
            if (snapshot.Length == 0) return true;

            var remaining = timeout - sw.Elapsed;
            if (remaining <= TimeSpan.Zero) return false;

            var all = Task.WhenAll(snapshot);
            var winner = await Task.WhenAny(all, Task.Delay(remaining)).ConfigureAwait(false);
            if (winner != all) return false;
        }
    }
}
