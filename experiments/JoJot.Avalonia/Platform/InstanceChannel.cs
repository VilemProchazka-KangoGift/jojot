using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JoJot.Models;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace JoJot.AvaloniaExperiment.Platform;

// Separate namespace per data directory keeps the experiment isolated from installed JoJot.
internal sealed partial class InstanceChannel : IDisposable
{
    private readonly Mutex _mutex;
    private readonly string _pipeName;
    private readonly string _ownerPath;
    private readonly CancellationTokenSource _stop = new();
    private Task? _server;
    private FileStream? _ownerFile;
    public bool IsPrimary { get; private set; }
    internal string PipeNameForTests => _pipeName;
    internal string? LastError { get; private set; }

    public InstanceChannel(string dataDirectory)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(dataDirectory.ToUpperInvariant())))[..24];
        using var process = System.Diagnostics.Process.GetCurrentProcess();
#if DEBUG
        const string suffix = ".Dev";
#else
        const string suffix = "";
#endif
        _pipeName = $"JoJot_Avalonia_{process.SessionId}_{hash}{suffix}";
        _ownerPath = Path.Combine(dataDirectory, $".experiment-owner{suffix}");
        _mutex = new Mutex(true, $"Local\\{_pipeName}", out var created);
        IsPrimary = created;
        if (created)
        {
            try { WriteOwnerIdentity(); }
            catch { _mutex.ReleaseMutex(); _mutex.Dispose(); throw; }
        }
    }

    private void WriteOwnerIdentity()
    {
        _ownerFile = new FileStream(_ownerPath, FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
        using var process = Process.GetCurrentProcess();
        using var writer = new StreamWriter(_ownerFile, leaveOpen: true) { AutoFlush = true };
        writer.WriteLine(process.Id);
        writer.WriteLine(process.StartTime.ToUniversalTime().Ticks);
        writer.WriteLine(Environment.ProcessPath);
    }

    public void Start(Action<IpcMessage> command)
    {
        _server = Task.Run(async () =>
        {
            while (!_stop.IsCancellationRequested)
            {
                try
                {
                    await using var pipe = new NamedPipeServerStream(_pipeName, PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await pipe.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false);
                    using var reader = new StreamReader(pipe);
                    using var readDeadline = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                    readDeadline.CancelAfter(TimeSpan.FromSeconds(2));
                    var line = await reader.ReadLineAsync(readDeadline.Token).ConfigureAwait(false);
                    if (line is not null && JsonSerializer.Deserialize(line, IpcMessageContext.Default.IpcMessage) is { } message)
                        command(message);
                }
                catch (OperationCanceledException) when (_stop.IsCancellationRequested) { break; }
                catch (OperationCanceledException) { }
                catch (IOException) { }
                catch (JsonException) { }
            }
        });
    }

    public enum SendResult { Delivered, Failed, Takeover }

    public SendResult Send(IpcMessage message)
    {
        var waited = Stopwatch.StartNew();
        while (waited.Elapsed < TimeSpan.FromSeconds(20))
        {
            try
            {
                using var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out);
                pipe.Connect(1000);
                // A shell-launched second instance can transfer its foreground permission
                // to the actual pipe server before requesting activation.
                var serverProcessId = GetServerProcessId(pipe);
                if (serverProcessId != 0) AllowSetForegroundWindow(serverProcessId);
                using var writer = new StreamWriter(pipe) { AutoFlush = true };
                writer.WriteLine(JsonSerializer.Serialize(message, IpcMessageContext.Default.IpcMessage));
                return SendResult.Delivered;
            }
            catch (TimeoutException)
            {
                if (TryTakeAbandonedMutex()) return SendResult.Takeover;
                if (waited.Elapsed >= TimeSpan.FromSeconds(5) && TryRecoverOwner(waited.Elapsed))
                    return SendResult.Takeover;
            }
            catch (IOException ex)
            {
                LastError = ex.ToString();
                JoJot.Services.LogService.Warn("IPC send failed", ex);
                Thread.Sleep(50);
            }
            catch (UnauthorizedAccessException ex) { LastError = ex.ToString(); return SendResult.Failed; }
        }
        return SendResult.Failed;
    }

    internal static bool IsEligibleOwner(DateTime startUtc, DateTime nowUtc, TimeSpan waited) =>
        waited >= TimeSpan.FromSeconds(5) && nowUtc - startUtc >= TimeSpan.FromSeconds(10);

    internal static uint GetServerProcessId(NamedPipeClientStream pipe) =>
        OperatingSystem.IsWindows() && GetNamedPipeServerProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out var id) ? id : 0;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetNamedPipeServerProcessId(nint pipe, out uint serverProcessId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllowSetForegroundWindow(uint processId);

    private bool TryTakeAbandonedMutex()
    {
        try { if (!_mutex.WaitOne(0)) return false; }
        catch (AbandonedMutexException) { }
        try { WriteOwnerIdentity(); IsPrimary = true; return true; }
        catch { _mutex.ReleaseMutex(); throw; }
    }

    private bool TryRecoverOwner(TimeSpan waited)
    {
        try
        {
            string[] lines;
            int[] holders;
            using (var file = new FileStream(_ownerPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(file))
            {
                lines = [reader.ReadLine() ?? "", reader.ReadLine() ?? "", reader.ReadLine() ?? ""];
                holders = GetFileOwners(file);
            }
            if (!int.TryParse(lines[0], out var pid) || !long.TryParse(lines[1], out var ticks) ||
                !IdentityMatches(pid, ticks, lines[2], holders, Environment.ProcessId, Environment.ProcessPath))
                return false;
            using var process = Process.GetProcessById(pid);
            if (process.StartTime.ToUniversalTime().Ticks != ticks ||
                !string.Equals(process.MainModule?.FileName, lines[2], StringComparison.OrdinalIgnoreCase) ||
                !IsEligibleOwner(process.StartTime.ToUniversalTime(), DateTime.UtcNow, waited)) return false;
            // Verify the identity file is still held exclusively against writes by its owner.
            try { using var probe = new FileStream(_ownerPath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite); return false; }
            catch (IOException) { }
            process.Kill();
            if (!process.WaitForExit(2000)) return false;
            try { if (!_mutex.WaitOne(2000)) return false; }
            catch (AbandonedMutexException) { }
            IsPrimary = true;
            WriteOwnerIdentity();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or OverflowException or System.ComponentModel.Win32Exception)
        { return false; }
    }

    internal static bool IdentityMatches(int pid, long ticks, string path, IReadOnlyCollection<int> holders,
        int currentPid, string? currentPath) => pid > 0 && pid != currentPid && ticks > 0 &&
        !string.IsNullOrWhiteSpace(path) && holders.Contains(pid) &&
        !holders.Any(holder => holder != pid && holder != currentPid) &&
        string.Equals(path, currentPath, StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(Path.GetFileNameWithoutExtension(path), "dotnet", StringComparison.OrdinalIgnoreCase);

    private static unsafe int[] GetFileOwners(FileStream file)
    {
        // FileProcessIdsUsingFileInformation returns the actual processes holding this file,
        // rather than trusting a PID written to a stale or forged marker.
        byte* buffer = stackalloc byte[4096];
        nint* status = stackalloc nint[2];
        var result = NtQueryInformationFile(file.SafeFileHandle.DangerousGetHandle(), status, buffer, 4096, 47);
        if (result < 0) return [];
        var count = *(uint*)buffer;
        if (count > 511) return [];
        var ids = (nuint*)(buffer + (IntPtr.Size == 8 ? 8 : 4));
        var owners = new int[count];
        for (var i = 0; i < count; i++) owners[i] = checked((int)ids[i]);
        return owners;
    }

    [LibraryImport("ntdll.dll")]
    private static unsafe partial int NtQueryInformationFile(nint file, nint* ioStatus, byte* information, uint length, int informationClass);

    public void Dispose()
    {
        _stop.Cancel();
        _server?.GetAwaiter().GetResult();
        _stop.Dispose();
        _ownerFile?.Dispose();
        if (IsPrimary)
            _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
