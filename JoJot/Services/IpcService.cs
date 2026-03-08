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
    public const string PipeName = "JoJot_IPC";
    public const string MutexName = "Global\\JoJot_SingleInstance";

    private static CancellationTokenSource? _cts;
    private static Action<IpcMessage>? _commandHandler;

    // ─── Server side (first instance) ────────────────────────────────────

    /// <summary>
    /// Starts the named pipe server loop. Call from the first instance after acquiring the mutex.
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
                string? line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                if (line is not null)
                {
                    IpcMessage? message = JsonSerializer.Deserialize(
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

    // ─── Client side (second instance) ───────────────────────────────────

    /// <summary>
    /// Sends a command to the running first instance via named pipe.
    /// On timeout, kills the zombie process and returns so the caller can restart.
    /// </summary>
    /// <param name="message">The IPC command to send.</param>
    /// <param name="timeoutMs">Connect timeout in milliseconds (default 500ms).</param>
    /// <param name="cancellationToken">Optional cancellation token for the operation.</param>
    public static async Task SendCommandAsync(IpcMessage message, int timeoutMs = 500, CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.None);

            await Task.Run(() => client.Connect(timeoutMs), cancellationToken).ConfigureAwait(false);

            string json = JsonSerializer.Serialize(message, IpcMessageContext.Default.IpcMessage);

            using var writer = new StreamWriter(client, leaveOpen: false) { AutoFlush = true };
            await writer.WriteLineAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);

            LogService.Info($"IpcService: sent command '{json}' to first instance");
        }
        catch (TimeoutException)
        {
            LogService.Warn("IpcService: connect timeout — killing zombie process");
            KillExistingInstances();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogService.Warn("IpcService: failed to send command to first instance", ex);
        }
    }

    /// <summary>
    /// Kills all JoJot processes other than the current one.
    /// Used when the first instance is unresponsive (zombie).
    /// </summary>
    public static void KillExistingInstances()
    {
        int myPid = Environment.ProcessId;
        foreach (Process proc in Process.GetProcessesByName("JoJot"))
        {
            if (proc.Id == myPid)
                continue;

            try
            {
                LogService.Info($"IpcService: killing zombie process PID={proc.Id}");
                proc.Kill(entireProcessTree: true);
            }
            catch (Exception ex)
            {
                LogService.Warn($"IpcService: failed to kill process PID={proc.Id}", ex);
            }
        }
    }

    // ─── Mutex helper ─────────────────────────────────────────────────────

    /// <summary>
    /// Tries to acquire the global single-instance mutex.
    /// </summary>
    /// <param name="mutex">The mutex object — caller must keep this alive for the process lifetime.</param>
    /// <returns>True if this is the first instance; false if another instance already holds the mutex.</returns>
    public static bool TryAcquireMutex(out Mutex mutex)
    {
        mutex = new Mutex(true, MutexName, out bool createdNew);
        return createdNew;
    }
}
