using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using JoJot.Models;
using WinForms = System.Windows.Forms;

namespace JoJot.Services;

/// <summary>
/// P/Invoke helpers for saving and restoring window geometry via
/// GetWindowPlacement/SetWindowPlacement, plus off-screen recovery.
///
/// Uses workspace coordinates (GetWindowPlacement/SetWindowPlacement are self-consistent).
/// Never mix these coordinates with WPF Window.Left/Top or SetWindowPos — they use
/// different coordinate systems.
/// </summary>
public static class WindowPlacementHelper
{
    // ─── P/Invoke declarations ──────────────────────────────────────────────

    private const int SW_SHOWNORMAL = 1;
    private const int SW_SHOWMAXIMIZED = 3;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPLACEMENT
    {
        public int length;
        public int flags;
        public int showCmd;
        public POINT ptMinPosition;
        public POINT ptMaxPosition;
        public RECT rcNormalPosition;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

    // ─── Public API ─────────────────────────────────────────────────────────

    /// <summary>
    /// Captures the current window geometry using GetWindowPlacement.
    /// Returns the normal (non-maximized) position/size even if the window is currently maximized.
    /// Must be called while the window handle is still valid (before Close completes).
    /// </summary>
    public static WindowGeometry CaptureGeometry(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            // Window has no handle — return default geometry
            return new WindowGeometry(100, 100, DefaultWidth, DefaultHeight, false);
        }

        var wp = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
        GetWindowPlacement(hwnd, ref wp);

        return new WindowGeometry(
            Left: wp.rcNormalPosition.Left,
            Top: wp.rcNormalPosition.Top,
            Width: wp.rcNormalPosition.Right - wp.rcNormalPosition.Left,
            Height: wp.rcNormalPosition.Bottom - wp.rcNormalPosition.Top,
            IsMaximized: wp.showCmd == SW_SHOWMAXIMIZED);
    }

    /// <summary>
    /// Applies saved geometry to a window. Call AFTER Show() so the HWND exists.
    /// If geo is null, uses default size (500x600) centered on screen.
    /// Performs off-screen recovery before applying.
    ///
    /// Uses SetWindowPlacement to restore in workspace coordinates, consistent
    /// with how CaptureGeometry saved them via GetWindowPlacement.
    /// </summary>
    public static void ApplyGeometry(Window window, WindowGeometry? geo)
    {
        if (geo is null)
        {
            // No saved geometry — use WPF defaults (centered, default size)
            // WindowStartupLocation.CenterScreen is only effective before Show(),
            // so we set dimensions directly and let WPF center.
            window.Width = DefaultWidth;
            window.Height = DefaultHeight;
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            return;
        }

        // Off-screen recovery
        var corrected = ClampToNearestScreen(geo);

        // Apply via SetWindowPlacement for coordinate-system consistency
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            // No HWND yet — fall back to WPF properties (before Show)
            window.Left = corrected.Left;
            window.Top = corrected.Top;
            window.Width = corrected.Width;
            window.Height = corrected.Height;
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            if (corrected.IsMaximized)
                window.WindowState = WindowState.Maximized;
            return;
        }

        // Use SetWindowPlacement for full fidelity (workspace coordinates)
        var wp = new WINDOWPLACEMENT
        {
            length = Marshal.SizeOf<WINDOWPLACEMENT>(),
            showCmd = corrected.IsMaximized ? SW_SHOWMAXIMIZED : SW_SHOWNORMAL,
            rcNormalPosition = new RECT
            {
                Left = (int)corrected.Left,
                Top = (int)corrected.Top,
                Right = (int)(corrected.Left + corrected.Width),
                Bottom = (int)(corrected.Top + corrected.Height)
            }
        };
        SetWindowPlacement(hwnd, ref wp);
    }

    /// <summary>
    /// Off-screen recovery: if the saved window position is not visible on any
    /// connected monitor, snaps position to the nearest screen edge.
    /// Keeps size intact per user decision — only adjusts position.
    /// Checks whether the top-left 50x50px region is visible on any screen.
    /// </summary>
    public static WindowGeometry ClampToNearestScreen(WindowGeometry geo)
    {
        var testRect = new System.Drawing.Rectangle(
            (int)geo.Left, (int)geo.Top, 50, 50);

        bool visible = WinForms.Screen.AllScreens.Any(s =>
            s.WorkingArea.IntersectsWith(testRect));

        if (visible) return geo;

        // Find nearest screen by Manhattan distance from saved top-left
        var nearest = WinForms.Screen.AllScreens
            .OrderBy(s =>
            {
                int dx = Math.Max(s.WorkingArea.Left - (int)geo.Left, 0) +
                         Math.Max((int)geo.Left - s.WorkingArea.Right, 0);
                int dy = Math.Max(s.WorkingArea.Top - (int)geo.Top, 0) +
                         Math.Max((int)geo.Top - s.WorkingArea.Bottom, 0);
                return dx + dy;
            })
            .First();

        var wa = nearest.WorkingArea;
        double newLeft = Math.Clamp(geo.Left, wa.Left, Math.Max(wa.Left, wa.Right - geo.Width));
        double newTop = Math.Clamp(geo.Top, wa.Top, Math.Max(wa.Top, wa.Bottom - geo.Height));

        LogService.Info($"Off-screen recovery: ({geo.Left},{geo.Top}) -> ({newLeft},{newTop})");
        return geo with { Left = newLeft, Top = newTop };
    }

    // ─── Constants ──────────────────────────────────────────────────────────

    /// <summary>
    /// Default and minimum window dimensions per user decision.
    /// Default: 500x600 (compact notepad-sized). Minimum: 320x420.
    /// </summary>
    public const double DefaultWidth = 500;
    public const double DefaultHeight = 600;
    public const double MinWidth = 320;
    public const double MinHeight = 420;
}
