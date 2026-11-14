using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using JoJot.Models;

namespace JoJot.AvaloniaExperiment.Platform;

internal static partial class WindowPlacement
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Placement
    {
        public int Length, Flags, ShowCommand;
        public Point Minimum, Maximum;
        public Rect Normal;
    }

    internal static WindowGeometry? Capture(Window window)
    {
        if (!OperatingSystem.IsWindows()) return CapturePortable(window);
        var handle = window.TryGetPlatformHandle()?.Handle ?? 0;
        var placement = new Placement { Length = Marshal.SizeOf<Placement>() };
        if (handle == 0 || !GetWindowPlacement(handle, ref placement)) return null;
        var rect = placement.Normal;
        return new WindowGeometry(rect.Left, rect.Top, rect.Right - rect.Left,
            rect.Bottom - rect.Top, placement.ShowCommand == 3);
    }

    internal static void Restore(Window window, WindowGeometry? geometry)
    {
        if (geometry is null) return;
        if (!OperatingSystem.IsWindows()) { RestorePortable(window, geometry); return; }
        var handle = window.TryGetPlatformHandle()?.Handle ?? 0;
        if (handle == 0) return;
        // Screen bounds are in physical pixels, matching WINDOWPLACEMENT coordinates.
        var corrected = Clamp(geometry, window.Screens.All.Select(s => s.WorkingArea).ToArray());
        var placement = new Placement
        {
            Length = Marshal.SizeOf<Placement>(), ShowCommand = corrected.IsMaximized ? 3 : 1,
            Normal = new Rect { Left = (int)corrected.Left, Top = (int)corrected.Top,
                Right = (int)(corrected.Left + corrected.Width), Bottom = (int)(corrected.Top + corrected.Height) }
        };
        SetWindowPlacement(handle, ref placement);
    }

    // Avalonia reports position and screen work areas in physical pixels, but
    // window sizes in DIPs. This fallback preserves geometry where supported;
    // Wayland compositors may ignore client-requested positions.
    internal static WindowGeometry? CapturePortable(Window window)
    {
        var scale = window.RenderScaling;
        if (!double.IsFinite(scale) || scale <= 0 || !double.IsFinite(window.Width) ||
            !double.IsFinite(window.Height) || window.Width <= 0 || window.Height <= 0)
            return null;
        return new WindowGeometry(window.Position.X, window.Position.Y, window.Width * scale,
            window.Height * scale, window.WindowState == WindowState.Maximized);
    }

    internal static void RestorePortable(Window window, WindowGeometry geometry)
    {
        var scale = window.RenderScaling;
        if (!double.IsFinite(scale) || scale <= 0) scale = 1;
        var corrected = Clamp(geometry, window.Screens.All.Select(screen => screen.WorkingArea).ToArray());
        window.Position = new PixelPoint((int)corrected.Left, (int)corrected.Top);
        window.Width = corrected.Width / scale;
        window.Height = corrected.Height / scale;
        if (corrected.IsMaximized) window.WindowState = WindowState.Maximized;
    }

    internal static WindowGeometry Clamp(WindowGeometry geometry, IReadOnlyList<PixelRect> areas)
    {
        if (areas.Count > 0 && !areas.Any(area =>
            geometry.Left < area.Right && geometry.Left + 50 > area.X &&
            geometry.Top < area.Bottom && geometry.Top + 50 > area.Y))
        {
            var area = areas.OrderBy(area =>
            {
                var dx = Math.Max(area.X - geometry.Left, 0) + Math.Max(geometry.Left - area.Right, 0);
                var dy = Math.Max(area.Y - geometry.Top, 0) + Math.Max(geometry.Top - area.Bottom, 0);
                return dx + dy;
            }).First();
            return geometry with
            {
                Left = Math.Clamp(geometry.Left, area.X, Math.Max(area.X, area.Right - geometry.Width)),
                Top = Math.Clamp(geometry.Top, area.Y, Math.Max(area.Y, area.Bottom - geometry.Height))
            };
        }
        return geometry;
    }

    [LibraryImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowPlacement(nint hwnd, ref Placement placement);
    [LibraryImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPlacement(nint hwnd, ref Placement placement);
}
