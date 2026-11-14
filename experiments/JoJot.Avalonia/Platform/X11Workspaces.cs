using System.Runtime.InteropServices;

namespace JoJot.AvaloniaExperiment.Platform;

internal static partial class X11Workspaces
{
    private const uint Cardinal = 6;
    private const int MaximumDesktops = 1024;

    internal static (IReadOnlyList<VirtualDesktop.Desktop> Desktops, string Current)? Read()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
            return null;
        nint display;
        try { display = XOpenDisplay(0); }
        catch (DllNotFoundException) { return null; }
        if (display == 0)
            return null;
        try
        {
            var root = XDefaultRootWindow(display);
            var count = ReadCardinal(display, root, "_NET_NUMBER_OF_DESKTOPS");
            var current = ReadCardinal(display, root, "_NET_CURRENT_DESKTOP");
            if (count is not (>= 1 and <= MaximumDesktops) || current is null || current >= count)
                return null;
            var desktops = Enumerable.Range(0, (int)count.Value)
                .Select(index => new VirtualDesktop.Desktop(Id((uint)index), "", index)).ToArray();
            return (desktops, Id(current.Value));
        }
        finally { XCloseDisplay(display); }
    }

    // EWMH exposes indices rather than persistent workspace IDs. Keep workspace
    // one on the existing fallback key so established Linux previews keep their notes.
    private static string Id(uint index) => index == 0 ? "default" : $"x11-workspace-{index + 1}";

    private static uint? ReadCardinal(nint display, nuint root, string name)
    {
        var atom = XInternAtom(display, name, 1);
        if (atom == 0)
            return null;
        var status = XGetWindowProperty(display, root, atom, 0, 1, 0, Cardinal,
            out var type, out var format, out var count, out var remainder, out var data);
        try
        {
            return status == 0 && type == Cardinal && format == 32 && count == 1 && remainder == 0 && data != 0
                ? unchecked((uint)Marshal.ReadInt32(data)) : null;
        }
        finally { if (data != 0) XFree(data); }
    }

    [LibraryImport("libX11.so.6")] private static partial nint XOpenDisplay(nint name);
    [LibraryImport("libX11.so.6")] private static partial nuint XDefaultRootWindow(nint display);
    [LibraryImport("libX11.so.6")] private static partial int XCloseDisplay(nint display);
    [LibraryImport("libX11.so.6")] private static partial int XFree(nint data);
    [LibraryImport("libX11.so.6", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nuint XInternAtom(nint display, string name, int onlyIfExists);
    [LibraryImport("libX11.so.6")]
    private static partial int XGetWindowProperty(nint display, nuint window, nuint property, nint offset, nint length,
        int delete, nuint requestedType, out nuint actualType, out int format, out nuint count,
        out nuint remainder, out nint data);
}
