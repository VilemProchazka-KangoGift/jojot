using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace JoJot.Services;

/// <summary>
/// P/Invoke helpers for cross-process window focus.
/// Uses <c>AttachThreadInput</c> and <c>SetForegroundWindow</c> to reliably bring a window
/// to the foreground even when a different application currently has focus.
/// </summary>
public static class WindowActivationHelper
{
    private const int SW_RESTORE = 9;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    /// <summary>
    /// Activates the specified WPF window and brings it to the foreground.
    /// Must be called on the UI thread.
    /// </summary>
    /// <param name="window">The window to activate.</param>
    public static void ActivateWindow(Window window)
    {
        System.Diagnostics.Debug.Assert(window.Dispatcher.CheckAccess(),
            "ActivateWindow must be called on the UI thread");

        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;

        IntPtr hwnd = new WindowInteropHelper(window).Handle;

        // Attach to the foreground window's thread so SetForegroundWindow succeeds
        IntPtr foregroundHwnd = GetForegroundWindow();
        uint foregroundThread = GetWindowThreadProcessId(foregroundHwnd, out _);
        uint appThread = GetCurrentThreadId();

        if (foregroundThread != appThread)
        {
            AttachThreadInput(foregroundThread, appThread, true);
            SetForegroundWindow(hwnd);
            AttachThreadInput(foregroundThread, appThread, false);
        }
        else
        {
            SetForegroundWindow(hwnd);
        }

        window.Activate();
        window.Focus();
    }
}
