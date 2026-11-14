using System.Runtime.InteropServices;

namespace JoJot.AvaloniaExperiment.Platform;

// A private XCB connection avoids changing Xlib's process-wide error handler.
// XWayland shortcuts are limited to what the compositor exposes to X11 clients.
internal sealed partial class X11GlobalHotkey : IDisposable
{
    private readonly object _gate = new();
    private readonly Action _callback;
    private readonly Thread _thread;
    private readonly byte _keycode;
    private readonly ushort _modifiers;
    private nint _connection;
    private readonly uint _root;
    private bool _stopping;
    private bool _paused;
    private static readonly ushort[] LockModifiers = [0, 2, 16, 18]; // Caps Lock and Num Lock

    internal static bool IsSupported => OperatingSystem.IsLinux() && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"));
    internal static bool SupportsKey(uint key) => ToKeysym(key) != 0;
    internal bool Registered { get; private set; }

    internal X11GlobalHotkey(uint modifiers, uint key, Action callback)
    {
        if (!IsSupported) throw new PlatformNotSupportedException("X11 is unavailable.");
        _callback = callback;
        var keysym = ToKeysym(key);
        if (keysym == 0) throw new ArgumentException("The selected key is not supported by X11.", nameof(key));
        var display = XOpenDisplay(0);
        if (display == 0) throw new InvalidOperationException("Could not open the X11 display.");
        try
        {
            _keycode = XKeysymToKeycode(display, keysym);
            _root = checked((uint)XDefaultRootWindow(display));
        }
        finally { XCloseDisplay(display); }
        if (_keycode == 0) throw new ArgumentException("The selected key is unavailable in the X11 keyboard map.", nameof(key));
        _modifiers = (ushort)(((modifiers & 1) != 0 ? 8 : 0) | ((modifiers & 2) != 0 ? 4 : 0) |
            ((modifiers & 4) != 0 ? 1 : 0) | ((modifiers & 8) != 0 ? 64 : 0));
        _connection = xcb_connect(0, 0);
        if (_connection == 0 || xcb_connection_has_error(_connection) != 0)
        {
            if (_connection != 0) xcb_disconnect(_connection);
            _connection = 0;
            throw new InvalidOperationException("Could not connect to the X11 display.");
        }
        try { Registered = Grab(); }
        catch { xcb_disconnect(_connection); _connection = 0; throw; }
        _thread = new Thread(Run) { IsBackground = true, Name = "JoJot-X11Hotkey" };
        _thread.Start();
    }

    private bool Grab()
    {
        foreach (var locks in LockModifiers)
        {
            var cookie = xcb_grab_key_checked(_connection, 0, _root, (ushort)(_modifiers | locks), _keycode, 1, 1);
            var error = xcb_request_check(_connection, cookie);
            if (error == 0) continue;
            free(error);
            Ungrab();
            return false;
        }
        xcb_flush(_connection);
        return true;
    }

    private void Ungrab()
    {
        foreach (var locks in LockModifiers) xcb_ungrab_key(_connection, _keycode, _root, (ushort)(_modifiers | locks));
        xcb_flush(_connection);
    }

    internal void PauseForRecording(bool pause)
    {
        lock (_gate)
        {
            if (_stopping || _paused == pause) return;
            _paused = pause;
            if (pause) { if (Registered) Ungrab(); Registered = false; }
            else Registered = Grab();
        }
    }

    private void Run()
    {
        while (true)
        {
            nint item;
            lock (_gate)
            {
                if (_stopping) return;
                item = xcb_poll_for_event(_connection);
            }
            if (item == 0) { Thread.Sleep(20); continue; }
            try
            {
                if ((Marshal.ReadByte(item) & 0x7F) == 2 && Marshal.ReadByte(item, 1) == _keycode)
                {
                    try { _callback(); }
                    catch (Exception ex) { JoJot.Services.LogService.Error("X11 global shortcut callback failed", ex); }
                }
            }
            finally { free(item); }
        }
    }

    public void Dispose()
    {
        lock (_gate) { if (_stopping) return; _stopping = true; }
        _thread.Join();
        lock (_gate)
        {
            if (Registered) Ungrab();
            xcb_disconnect(_connection);
            _connection = 0;
            Registered = false;
        }
    }

    internal static nuint ToKeysym(uint key) => key switch
    {
        >= 0x41 and <= 0x5A => key + 0x20,
        >= 0x30 and <= 0x39 => key,
        >= 0x70 and <= 0x87 => 0xFFBEu + key - 0x70,
        >= 0x60 and <= 0x69 => 0xFFB0u + key - 0x60,
        0x08 => 0xFF08, 0x09 => 0xFF09, 0x0D => 0xFF0D, 0x1B => 0xFF1B,
        0x20 => 0x20, 0x21 => 0xFF55, 0x22 => 0xFF56, 0x23 => 0xFF57, 0x24 => 0xFF50,
        0x25 => 0xFF51, 0x26 => 0xFF52, 0x27 => 0xFF53, 0x28 => 0xFF54,
        0x2D => 0xFF63, 0x2E => 0xFFFF,
        _ => 0,
    };

    [LibraryImport("libX11.so.6")] private static partial nint XOpenDisplay(nint name);
    [LibraryImport("libX11.so.6")] private static partial int XCloseDisplay(nint display);
    [LibraryImport("libX11.so.6")] private static partial nuint XDefaultRootWindow(nint display);
    [LibraryImport("libX11.so.6")] private static partial byte XKeysymToKeycode(nint display, nuint keysym);
    [LibraryImport("libxcb.so.1")] private static partial nint xcb_connect(nint display, nint screen);
    [LibraryImport("libxcb.so.1")] private static partial int xcb_connection_has_error(nint connection);
    [LibraryImport("libxcb.so.1")] private static partial void xcb_disconnect(nint connection);
    [LibraryImport("libxcb.so.1")] private static partial uint xcb_grab_key_checked(nint connection, byte ownerEvents, uint window, ushort modifiers, byte key, byte pointerMode, byte keyboardMode);
    [LibraryImport("libxcb.so.1")] private static partial nint xcb_request_check(nint connection, uint cookie);
    [LibraryImport("libxcb.so.1")] private static partial uint xcb_ungrab_key(nint connection, byte key, uint window, ushort modifiers);
    [LibraryImport("libxcb.so.1")] private static partial int xcb_flush(nint connection);
    [LibraryImport("libxcb.so.1")] private static partial nint xcb_poll_for_event(nint connection);
    [LibraryImport("libc")] private static partial void free(nint pointer);
}
