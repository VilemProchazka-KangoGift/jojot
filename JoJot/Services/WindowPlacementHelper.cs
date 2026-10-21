using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using JoJot.Models;

namespace JoJot.Services;

/// <summary>
/// P/Invoke helpers for saving and restoring window geometry via
/// <c>GetWindowPlacement</c> / <c>SetWindowPlacement</c>, with off-screen recovery.
///
/// Uses workspace coordinates (GetWindowPlacement/SetWindowPlacement are self-consistent).
/// Never mix these coordinates with WPF <c>Window.Left/Top</c> or <c>SetWindowPos</c>,
/// which use different coordinate systems.
/// </summary>
public static partial class WindowPlacementHelper
{
    // P/Invoke declarations

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

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

    // Public API

    /// <summary>
    /// Captures the current window geometry using <c>GetWindowPlacement</c>.
    /// Returns the normal (non-maximized) position and size even if the window is currently maximized.
    /// Must be called while the window handle is still valid (before Close completes).
    /// </summary>
    /// <param name="window">The window whose geometry to capture.</param>
    public static WindowGeometry CaptureGeometry(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
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
    /// Applies saved geometry to a window. Call after <c>Show()</c> so the HWND exists.
    /// If <paramref name="geo"/> is <c>null</c>, uses default size (500x600) centered on screen.
    /// Performs off-screen recovery before applying.
    ///
    /// Uses <c>SetWindowPlacement</c> to restore in workspace coordinates, consistent
    /// with how <see cref="CaptureGeometry"/> saved them.
    /// </summary>
    /// <param name="window">The window to apply geometry to.</param>
    /// <param name="geo">The saved geometry, or <c>null</c> for defaults.</param>
    public static void ApplyGeometry(Window window, WindowGeometry? geo)
    {
        if (geo is null)
        {
            window.Width = DefaultWidth;
            window.Height = DefaultHeight;
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            return;
        }

        var corrected = ClampToNearestScreen(geo);

        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            // No HWND yet; fall back to WPF properties (before Show)
            window.Left = corrected.Left;
            window.Top = corrected.Top;
            window.Width = corrected.Width;
            window.Height = corrected.Height;
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            if (corrected.IsMaximized)
            {
                window.WindowState = WindowState.Maximized;
            }

            return;
        }

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
    /// Off-screen recovery: if the saved window position is not visible on any connected
    /// monitor, snaps the position to the nearest screen edge. Size is preserved; only the
    /// position is adjusted. Visibility is determined by whether the top-left 50x50 px region
    /// intersects any screen's working area.
    /// </summary>
    /// <param name="geo">The geometry to validate and potentially correct.</param>
    public static WindowGeometry ClampToNearestScreen(WindowGeometry geo) =>
        ClampToWorkAreas(geo, GetMonitorWorkAreas());

    /// <summary>
    /// A monitor working area in virtual-screen pixels (MONITORINFO.rcWork — the same values
    /// WinForms <c>Screen.WorkingArea</c> reported, which this replaced).
    /// </summary>
    internal readonly record struct WorkArea(int Left, int Top, int Right, int Bottom)
    {
        /// <summary>Same semantics as System.Drawing.Rectangle.IntersectsWith (edges exclusive).</summary>
        public bool IntersectsWith(int x, int y, int width, int height) =>
            x < Right && Left < x + width && y < Bottom && Top < y + height;
    }

    /// <summary>
    /// Pure core of <see cref="ClampToNearestScreen"/>: keeps <paramref name="geo"/> if its
    /// top-left 50x50 region intersects any work area, otherwise moves it onto the nearest one
    /// (Manhattan distance from the top-left). With no work areas, returns it unchanged.
    /// </summary>
    internal static WindowGeometry ClampToWorkAreas(WindowGeometry geo, IReadOnlyList<WorkArea> workAreas)
    {
        if (workAreas.Count == 0)
        {
            return geo;
        }

        int left = (int)geo.Left;
        int top = (int)geo.Top;

        if (workAreas.Any(wa => wa.IntersectsWith(left, top, 50, 50)))
        {
            return geo;
        }

        // Find nearest screen by Manhattan distance from saved top-left
        var wa = workAreas
            .OrderBy(s =>
            {
                int dx = Math.Max(s.Left - left, 0) + Math.Max(left - s.Right, 0);
                int dy = Math.Max(s.Top - top, 0) + Math.Max(top - s.Bottom, 0);
                return dx + dy;
            })
            .First();

        double newLeft = Math.Clamp(geo.Left, wa.Left, Math.Max(wa.Left, wa.Right - geo.Width));
        double newTop = Math.Clamp(geo.Top, wa.Top, Math.Max(wa.Top, wa.Bottom - geo.Height));

        LogService.Info("Off-screen recovery: ({OldLeft},{OldTop}) -> ({NewLeft},{NewTop})", geo.Left, geo.Top, newLeft, newTop);
        return geo with { Left = newLeft, Top = newTop };
    }

    // ─── Monitor enumeration (replaces WinForms Screen.AllScreens) ──────

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool EnumDisplayMonitors(
        IntPtr hdc, IntPtr lprcClip,
        delegate* unmanaged<IntPtr, IntPtr, RECT*, IntPtr, int> lpfnEnum, IntPtr dwData);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    /// <summary>
    /// Returns the working area of every connected monitor, as <c>EnumDisplayMonitors</c> +
    /// <c>GetMonitorInfo(rcWork)</c> — the same calls WinForms Screen.AllScreens made, so the
    /// coordinate space is unchanged.
    /// </summary>
    internal static unsafe List<WorkArea> GetMonitorWorkAreas()
    {
        var areas = new List<WorkArea>();
        var handle = GCHandle.Alloc(areas);
        try
        {
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, &CollectMonitorWorkArea, GCHandle.ToIntPtr(handle));
        }
        finally
        {
            handle.Free();
        }
        return areas;
    }

    [UnmanagedCallersOnly]
    private static unsafe int CollectMonitorWorkArea(IntPtr hMonitor, IntPtr hdc, RECT* lprcMonitor, IntPtr dwData)
    {
        if (GCHandle.FromIntPtr(dwData).Target is List<WorkArea> areas)
        {
            var mi = new MONITORINFO { cbSize = sizeof(MONITORINFO) };
            if (GetMonitorInfo(hMonitor, ref mi))
            {
                areas.Add(new WorkArea(mi.rcWork.Left, mi.rcWork.Top, mi.rcWork.Right, mi.rcWork.Bottom));
            }
        }
        return 1; // continue enumeration
    }

    // Constants

    /// <summary>Default window width: 1250 pixels.</summary>
    public const double DefaultWidth = 1250;

    /// <summary>Default window height: 600 pixels (compact notepad-sized).</summary>
    public const double DefaultHeight = 600;

    /// <summary>Minimum window width: 320 pixels.</summary>
    public const double MinWidth = 320;

    /// <summary>Minimum window height: 420 pixels.</summary>
    public const double MinHeight = 420;
}
