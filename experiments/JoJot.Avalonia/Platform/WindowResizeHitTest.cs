using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using static Avalonia.Controls.Win32Properties.Win32HitTestValue;

namespace JoJot.AvaloniaExperiment.Platform;

internal static partial class WindowResizeHitTest
{
    private const uint NonClientHitTest = 0x0084;
    private const uint ExtendedFrameBounds = 9;
    private const int EdgeDip = 6;
    private const int CornerDip = 16;

    internal static void Attach(Window window)
    {
        if (OperatingSystem.IsWindows() && window.TryGetPlatformHandle()?.HandleDescriptor == "HWND")
            Win32Properties.AddWndProcHookCallback(window, HitTest);

        nint HitTest(nint handle, uint message, nint wParam, nint lParam, ref bool handled)
        {
            if (message != NonClientHitTest || !window.CanResize || window.WindowState != WindowState.Normal ||
                DwmGetWindowAttribute(handle, ExtendedFrameBounds, out var frame, Marshal.SizeOf<Frame>()) != 0)
                return 0;

            var x = unchecked((short)(long)lParam);
            var y = unchecked((short)((long)lParam >> 16));
            var hit = GetHitTest(new PixelRect(frame.Left, frame.Top, frame.Right - frame.Left, frame.Bottom - frame.Top),
                new PixelPoint(x, y), window.RenderScaling);
            if (hit is null)
                return 0;

            handled = true;
            return (nint)hit.Value;
        }
    }

    internal static Win32Properties.Win32HitTestValue? GetHitTest(PixelRect frame, PixelPoint pointer, double scale)
    {
        var left = pointer.X - frame.X;
        var top = pointer.Y - frame.Y;
        var right = frame.Width - 1 - left;
        var bottom = frame.Height - 1 - top;
        if (left < 0 || top < 0 || right < 0 || bottom < 0)
            return null;

        var edge = Math.Ceiling(EdgeDip * scale);
        var corner = Math.Ceiling(CornerDip * scale);
        if (left < edge)
            return top < corner ? TopLeft : bottom < corner ? BottomLeft : Left;
        if (right < edge)
            return top < corner ? TopRight : bottom < corner ? BottomRight : Right;
        if (top < edge)
            return left < corner ? TopLeft : right < corner ? TopRight : Top;
        if (bottom < edge)
            return left < corner ? BottomLeft : right < corner ? BottomRight : Bottom;
        return null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Frame
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmGetWindowAttribute(nint hwnd, uint attribute, out Frame frame, int size);
}
