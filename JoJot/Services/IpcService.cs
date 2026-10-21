using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Windows;
using JoJot.Models;

namespace JoJot.Services;

/// <summary>
/// Named pipe IPC service for single-instance enforcement.
/// The first instance runs the server; second instances use the client to send commands.
/// </summary>
public static class IpcService
{
    /// <summary>Named pipe name for IPC communication.</summary>
    public static string PipeName => AppEnvironment.PipeName;

    /// <summary>Global mutex name for single-instance enforcement.</summary>
    public static string MutexName => AppEnvironment.MutexName;

    private static CancellationTokenSource? _cts;
    private static Action<IpcMessage>? _commandHandler;

    /// <summary>Outcome of a second-instance <see cref="SendCommand"/> call.</summary>
    public enum SendResult
    {
        /// <summary>The command was delivered to the running first instance.</summary>
        Delivered,
        /// <summary>The first instance was an unresponsive zombie and was terminated; the caller
        /// should take over as the primary instance rather than exit.</summary>
        ZombieKilled,
        /// <summary>Delivery failed for another reason; the caller should exit to avoid two instances.</summary>
        Failed
    }

    // ─── Server side (first instance) ───────────────────────────────────

    /// <summary>
    /// Starts the named pipe server loop. Call from the first instance after acquiring the mutex —
    /// as early as possible: App.OnAppStartup starts it before the database opens, so a second
    /// instance launched during the primary's cold start connects instead of timing out. The
    /// handler must therefore tolerate commands that arrive before any window exists
    /// (see <see cref="IpcCommandQueue"/>).
    /// </summary>
    /// <param name="commandHandler">Callback invoked on the UI Dispatcher for each received command.</param>
    /// <param name="appShutdownToken">Token that fires when the application is shutting down.</param>
    public static void StartServer(Action<IpcMessage> commandHandler, CancellationToken appShutdownToken)
    {
        _commandHandler = commandHandler;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(appShutdownToken);
        Task.Run(() => ListenLoopAsync(_cts.Token), _cts.Token);
    }

    /// <summary>
    /// Stops the server by cancelling its listen loop.
    /// </summary>
    public static void StopServer()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    /// <summary>
    /// Listens for incoming IPC connections in a loop until cancelled.
    /// Each connection reads a single JSON-serialized <see cref="IpcMessage"/> and
    /// dispatches it to the command handler on the UI thread.
    /// </summary>
    private static async Task ListenLoopAsync(CancellationToken ct)
    {
        LogService.Info("IpcService: server listen loop started");
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;
            try
            {
                server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(ct).ConfigureAwait(false);

                using var reader = new StreamReader(server, leaveOpen: false);
                var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                if (line is not null)
                {
                    var message = JsonSerializer.Deserialize(
                        line,
                        IpcMessageContext.Default.IpcMessage);

                    if (message is not null && _commandHandler is not null)
                    {
                        await Application.Current.Dispatcher.InvokeAsync(
                            () => _commandHandler(message));
                    }
                }
            }
            catch (OperationCanceledException)
            {
                LogService.Info("IpcService: server listen loop cancelled");
                break;
            }
            catch (Exception ex)
            {
                LogService.Error("IpcService: error in listen loop — continuing", ex);
            }
            finally
            {
                server?.Dispose();
            }
        }
        LogService.Info("IpcService: server listen loop exited");
    }

    // ─── Client side (second instance) ──────────────────────────────────
    //
    // Zombie safeguard. The primary starts its pipe server at the very top of App.OnAppStartup
    // (before the database opens), so a healthy primary answers within its first few hundred ms
    // even during a ~2.4 s cold start. A primary is killed only when BOTH hold:
    //   • this second instance has been failing to connect for at least MinZombieWait (5 s), and
    //   • the primary process is at least StartupGrace (10 s) old — a younger one is "still
    //     starting", and we keep retrying instead of killing it.
    // MaxTotalWait (20 s) bounds the wait; reaching it returns Failed (exit, never kill).
    // The old budget (3 × 500 ms connects + 300 ms of delays ≈ 1.8 s) killed healthy primaries
    // that were still starting, because their server only started after the tabs loaded.

    /// <summary>Per-attempt connect timeout. <see cref="NamedPipeClientStream.Connect(int)"/> polls
    /// until the pipe exists, so it returns as soon as the primary's server listens; this value
    /// only sets how often the zombie checks run.</summary>
    internal const int ConnectAttemptMs = 1000;

    /// <summary>Minimum total connect-failure time before the primary may be treated as a zombie.</summary>
    internal static readonly TimeSpan MinZombieWait = TimeSpan.FromSeconds(5);

    /// <summary>A primary process younger than this is still starting and is never killed.</summary>
    internal static readonly TimeSpan StartupGrace = TimeSpan.FromSeconds(10);

    /// <summary>Absolute ceiling on the second instance's wait; reaching it gives up without killing.</summary>
    internal static readonly TimeSpan MaxTotalWait = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Pure zombie decision for one candidate primary process: kill only after the connect has
    /// been failing for <see cref="MinZombieWait"/> AND the process started at least
    /// <see cref="StartupGrace"/> ago. Pass <see cref="DateTime.MinValue"/> when the start time
    /// cannot be read, which leaves only the wait-time guard.
    /// </summary>
    /// <param name="processStart">The candidate's <see cref="Process.StartTime"/> (local time).</param>
    /// <param name="now">Current local time.</param>
    /// <param name="waited">How long this second instance has been failing to connect.</param>
    internal static bool ShouldKillCandidate(DateTime processStart, DateTime now, TimeSpan waited) =>
        waited >= MinZombieWait && now - processStart >= StartupGrace;

    /// <summary>
    /// Sends a command to the running first instance via named pipe. Fully synchronous — built for
    /// the pre-WPF second-instance path in <c>Program.Main</c> (no dispatcher, no thread-pool hop,
    /// no async state machine). Retries until the primary answers; kills it only when
    /// <see cref="ShouldKillCandidate"/> says it is a genuinely hung primary.
    /// </summary>
    /// <param name="message">The IPC command to send.</param>
    public static SendResult SendCommand(IpcMessage message)
    {
        string json = JsonSerializer.Serialize(message, IpcMessageContext.Default.IpcMessage);
        var waited = Stopwatch.StartNew();
        bool loggedStillStarting = false;

        for (int attempt = 1; ; attempt++)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.None);
                client.Connect(ConnectAttemptMs);

                using (var writer = new StreamWriter(client, leaveOpen: true) { AutoFlush = true })
                {
                    writer.WriteLine(json);
                }

                LogService.Info("IpcService: sent command {Command} to first instance (attempt {Attempt}, {WaitedMs}ms)",
                    json, attempt, waited.ElapsedMilliseconds);
                return SendResult.Delivered;
            }
            catch (TimeoutException)
            {
                var elapsed = waited.Elapsed;
                if (elapsed >= MaxTotalWait)
                {
                    LogService.Warn("IpcService: first instance did not answer within {WaitedMs}ms — giving up without killing it",
                        (long)elapsed.TotalMilliseconds);
                    return SendResult.Failed;
                }

                if (elapsed < MinZombieWait)
                {
                    LogService.Warn("IpcService: connect timeout (attempt {Attempt}, {WaitedMs}ms) — retrying",
                        attempt, (long)elapsed.TotalMilliseconds);
                    continue;
                }

                if (TryKillZombie(elapsed, ref loggedStillStarting) is { } result)
                {
                    return result;
                }
            }
            catch (Exception ex)
            {
                LogService.Warn("IpcService: failed to send command to first instance", ex);
                return SendResult.Failed;
            }
        }
    }

    /// <summary>
    /// Zombie check after <see cref="MinZombieWait"/> of failed connects. Returns
    /// <see cref="SendResult.ZombieKilled"/> when the primary was killed (or no other JoJot
    /// process exists, so this instance should take over), or null to keep retrying because a
    /// candidate is still inside its <see cref="StartupGrace"/>.
    /// </summary>
    private static SendResult? TryKillZombie(TimeSpan elapsed, ref bool loggedStillStarting)
    {
        var others = GetOtherInstances();
        try
        {
            if (others.Count == 0)
            {
                LogService.Warn("IpcService: no other JoJot process found after {WaitedMs}ms — taking over as primary",
                    (long)elapsed.TotalMilliseconds);
                return SendResult.ZombieKilled;
            }

            var now = DateTime.Now;
            if (others.Any(p => !ShouldKillCandidate(StartTimeOf(p), now, elapsed)))
            {
                if (!loggedStillStarting)
                {
                    LogService.Info("IpcService: first instance is still starting (younger than {GraceSeconds}s) — waiting instead of killing",
                        StartupGrace.TotalSeconds);
                    loggedStillStarting = true;
                }
                return null;
            }

            LogService.Warn("IpcService: first instance unresponsive for {WaitedMs}ms — killing zombie process",
                (long)elapsed.TotalMilliseconds);
            KillInstances(others);
            return SendResult.ZombieKilled;
        }
        finally
        {
            foreach (var p in others)
            {
                p.Dispose();
            }
        }
    }

    /// <summary>All JoJot processes other than the current one (caller disposes them).</summary>
    private static List<Process> GetOtherInstances()
    {
        int myPid = Environment.ProcessId;
        var result = new List<Process>();
        foreach (var proc in Process.GetProcessesByName("JoJot"))
        {
            if (proc.Id == myPid)
            {
                proc.Dispose();
                continue;
            }
            result.Add(proc);
        }
        return result;
    }

    /// <summary>The process start time, or <see cref="DateTime.MinValue"/> if it cannot be read
    /// (exited, access denied), which leaves only the wait-time guard in force.</summary>
    private static DateTime StartTimeOf(Process process)
    {
        try
        {
            return process.StartTime;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            return DateTime.MinValue;
        }
    }

    /// <summary>
    /// Kills the given unresponsive JoJot processes, waiting briefly for each to exit.
    /// </summary>
    private static void KillInstances(List<Process> processes)
    {
        foreach (var proc in processes)
        {
            try
            {
                LogService.Info("IpcService: killing zombie process PID={ProcessId}", proc.Id);
                proc.Kill(entireProcessTree: true);
                // Wait briefly so the process fully exits and releases its mutex/DB handles before
                // this instance tries to take over as primary.
                proc.WaitForExit(2000);
            }
            catch (Exception ex)
            {
                LogService.Warn("IpcService: failed to kill process PID={ProcessId}", proc.Id, ex);
            }
        }
    }

    // ─── Mutex helper ───────────────────────────────────────────────────

    /// <summary>
    /// Tries to acquire the global single-instance mutex.
    /// </summary>
    /// <param name="mutex">The mutex object — caller must keep this alive for the process lifetime.</param>
    /// <returns><c>true</c> if this is the first instance; <c>false</c> if another instance already holds the mutex.</returns>
    public static bool TryAcquireMutex(out Mutex mutex)
    {
        mutex = new Mutex(true, MutexName, out bool createdNew);
        return createdNew;
    }
}

/// <summary>
/// Holds IPC commands that arrive before the primary can act on them. The pipe server starts
/// before the database opens, so a command can arrive while its target window does not exist yet
/// and cannot be created (window creation needs the DB). Such commands are deferred and replayed,
/// in arrival order, once startup completes. A command whose target window already exists is never
/// deferred: the window itself queues a new-tab request until its tabs have loaded.
/// UI-thread only (not thread-safe).
/// </summary>
internal sealed class IpcCommandQueue
{
    private readonly List<IpcMessage> _pending = [];

    /// <summary>True once <see cref="MarkReady"/> has run (startup finished).</summary>
    public bool IsReady { get; private set; }

    /// <summary>Number of commands waiting for replay.</summary>
    public int Count => _pending.Count;

    /// <summary>
    /// Defers <paramref name="message"/> when startup has not completed and no window exists for
    /// its target desktop. Returns true if deferred (the caller must not handle it now).
    /// </summary>
    public bool TryDefer(IpcMessage message, bool targetWindowExists)
    {
        if (IsReady || targetWindowExists)
        {
            return false;
        }
        _pending.Add(message);
        return true;
    }

    /// <summary>
    /// Marks startup complete (later commands are never deferred) and returns the deferred
    /// commands in arrival order, emptying the queue. A second call returns an empty list.
    /// </summary>
    public IReadOnlyList<IpcMessage> MarkReady()
    {
        IsReady = true;
        var drained = _pending.ToArray();
        _pending.Clear();
        return drained;
    }
}
