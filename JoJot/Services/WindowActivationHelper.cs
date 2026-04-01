using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace JoJot.Services;

/// <summary>
/// P/Invoke helpers for cross-process window focus.
/// Uses <c>AttachThreadInput</c> and <c>SetForegroundWindow</c> to reliably bring a window
/// to the foreground even when a different application currently has focus.
/// </summary>
public static partial class WindowActivationHelper
{
    private const int SW_RESTORE = 9;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool fAttach);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(IntPtr hWnd, int nCmdShow);

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
        {
            window.WindowState = WindowState.Normal;
        }

        var hwnd = new WindowInteropHelper(window).Handle;

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
