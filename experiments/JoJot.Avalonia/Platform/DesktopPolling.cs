using System.Runtime.InteropServices;

namespace JoJot.AvaloniaExperiment.Platform;

// Notifications can be lost when Explorer restarts; keep a bounded fallback and
// detect shell replacement separately from the more expensive enumeration.
internal sealed partial class DesktopPolling
{
    private TimeSpan _lastEnumeration;
    private uint _shellProcess;

    internal void Start(TimeSpan now, uint shellProcess) { _lastEnumeration = now; _shellProcess = shellProcess; }

    internal bool ShouldEnumerate(TimeSpan now, uint shellProcess, bool notificationsRegistered, out bool shellChanged)
    {
        shellChanged = shellProcess != 0 && shellProcess != _shellProcess;
        if (shellChanged) _shellProcess = shellProcess;
        var interval = notificationsRegistered ? TimeSpan.FromSeconds(30) : TimeSpan.FromSeconds(2);
        if (!shellChanged && now - _lastEnumeration < interval) return false;
        _lastEnumeration = now;
        return true;
    }

    internal static uint GetShellProcess()
    {
        if (!OperatingSystem.IsWindows()) return 0;
        var window = GetShellWindow();
        if (window == 0) return 0;
        GetWindowThreadProcessId(window, out var process);
        return process;
    }

    [LibraryImport("user32.dll")] private static partial nint GetShellWindow();
    [LibraryImport("user32.dll")] private static partial uint GetWindowThreadProcessId(nint hwnd, out uint process);
}
