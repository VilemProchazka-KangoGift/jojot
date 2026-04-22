using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using JoJot.Interop;

namespace JoJot.Services;

/// <summary>
/// Diagnostic-only telemetry for the virtual-desktop redirect bug.
/// Pure observation — no decisions made here. Grep logs for "DESKTOP-TELEMETRY".
/// </summary>
public static partial class DesktopTelemetry
{
    // ─── P/Invoke ───────────────────────────────────────────────────────

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWinEventHook(
        uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
        WinEventDelegate pfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnhookWinEvent(IntPtr hWinEventHook);

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    private delegate void WinEventDelegate(
        IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    private const uint WINEVENT_SKIPOWNPROCESS = 0x0002;

    // ─── State ──────────────────────────────────────────────────────────

    private static IntPtr _foregroundHook;
    private static WinEventDelegate? _foregroundProc; // keep alive — required for callback
    private static readonly long _startTicks = Stopwatch.GetTimestamp();

    /// <summary>
    /// Monotonic milliseconds since telemetry module init — used to correlate
    /// all DESKTOP-TELEMETRY log lines on a single timeline.
    /// </summary>
    public static long MonotonicMs
    {
        get
        {
            long ticks = Stopwatch.GetTimestamp() - _startTicks;
            return ticks * 1000 / Stopwatch.Frequency;
        }
    }

    /// <summary>
    /// Installs the EVENT_SYSTEM_FOREGROUND hook. Call once on UI thread during startup.
    /// Safe to call multiple times — subsequent calls are no-ops.
    /// </summary>
    public static void Initialize()
    {
        if (_foregroundHook != IntPtr.Zero) return;

        _foregroundProc = OnForegroundChanged;
        _foregroundHook = SetWinEventHook(
            EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _foregroundProc, 0, 0,
            WINEVENT_OUTOFCONTEXT);

        if (_foregroundHook == IntPtr.Zero)
        {
            LogService.Warn("DESKTOP-TELEMETRY: failed to install foreground hook (Win32 error {Win32Error})",
                Marshal.GetLastWin32Error());
        }
        else
        {
            LogService.Info("DESKTOP-TELEMETRY: foreground hook installed (t0={MonotonicMs}ms)", MonotonicMs);
        }
    }

    /// <summary>
    /// Removes the hook on shutdown.
    /// </summary>
    public static void Shutdown()
    {
        if (_foregroundHook != IntPtr.Zero)
        {
            UnhookWinEvent(_foregroundHook);
            _foregroundHook = IntPtr.Zero;
            _foregroundProc = null;
        }
    }

    private static void OnForegroundChanged(
        IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (hwnd == IntPtr.Zero) return;

        try
        {
            string className = GetHwndClassName(hwnd);
            // Cheap class match first — skips the WindowInteropHelper scan + COM call
            // for the ~99% of foreground events unrelated to JoJot or the shell.
            if (!IsInterestingClass(className) && !IsJoJotWindow(hwnd)) return;

            LogService.Info(
                "DESKTOP-TELEMETRY fg-change t={MonotonicMs}ms hwnd=0x{Hwnd:X} class={ClassName} desktop={DesktopGuid}",
                MonotonicMs, hwnd.ToInt64(), className, TryGetDesktopGuid(hwnd));
        }
        catch (Exception ex)
        {
            LogService.Debug("DESKTOP-TELEMETRY fg-change error: {Error}", ex.Message);
        }
    }

    /// <summary>
    /// Logs a structured snapshot of the current foreground state plus event-specific fields.
    /// Pass <paramref name="extraTemplate"/> with Serilog-style placeholders and matching
    /// <paramref name="extraArgs"/> — the extras are appended to the base template so all
    /// fields remain structured (no string interpolation at call sites).
    /// </summary>
    public static void LogSnapshot(string eventName, string? extraTemplate = null, params object?[] extraArgs)
    {
        try
        {
            IntPtr fg = GetForegroundWindow();
            string fgClass = fg != IntPtr.Zero ? GetHwndClassName(fg) : "";
            string fgDesktop = fg != IntPtr.Zero ? TryGetDesktopGuid(fg) : "";
            int threadId = Environment.CurrentManagedThreadId;

            const string baseTemplate = "DESKTOP-TELEMETRY {EventName} t={MonotonicMs}ms thread={ThreadId} fg-hwnd=0x{FgHwnd:X} fg-class={FgClass} fg-desktop={FgDesktop}";
            object?[] baseArgs = [eventName, MonotonicMs, threadId, fg.ToInt64(), fgClass, fgDesktop];

            if (extraTemplate is null)
            {
                LogService.Info(baseTemplate, baseArgs);
                return;
            }

            var combined = new object?[baseArgs.Length + extraArgs.Length];
            Array.Copy(baseArgs, combined, baseArgs.Length);
            Array.Copy(extraArgs, 0, combined, baseArgs.Length, extraArgs.Length);
            LogService.Info(baseTemplate + " " + extraTemplate, combined);
        }
        catch (Exception ex)
        {
            LogService.Debug("DESKTOP-TELEMETRY snapshot error: {Error}", ex.Message);
        }
    }

    private static bool IsInterestingClass(string className)
    {
        return className is "Shell_TrayWnd"
            or "Shell_SecondaryTrayWnd"
            or "MSTaskListWClass"
            or "MSTaskSwWClass"
            or "Windows.UI.Core.CoreWindow"
            or "ApplicationFrameWindow"
            or "XamlExplorerHostIslandWindow"
            or "WorkerW";
    }

    private static bool IsJoJotWindow(IntPtr hwnd)
    {
        var app = System.Windows.Application.Current as App;
        if (app is null) return false;

        foreach (var w in app.GetAllWindows())
        {
            var wHwnd = new System.Windows.Interop.WindowInteropHelper(w).Handle;
            if (wHwnd == hwnd) return true;
        }
        return false;
    }

    private static string GetHwndClassName(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        int len = GetClassName(hwnd, sb, sb.Capacity);
        return len > 0 ? sb.ToString() : "";
    }

    private static string TryGetDesktopGuid(IntPtr hwnd)
    {
        try
        {
            return VirtualDesktopInterop.GetWindowDesktopId(hwnd).ToString();
        }
        catch
        {
            return "(unknown)";
        }
    }
}
