using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace JoJot.AvaloniaExperiment.Platform;

// Carbon's public hotkey API dispatches through the application event target.
// Construct, pause and dispose on Avalonia's macOS UI thread.
internal sealed unsafe partial class MacGlobalHotkey : IDisposable
{
    private const string Carbon = "/System/Library/Frameworks/Carbon.framework/Carbon";
    private const uint Signature = 0x4A4A4F54; // JJOT
    private const int EventNotHandled = -9874;
    private static int _nextId;
    private readonly Action _callback;
    private readonly uint _keycode;
    private readonly uint _modifiers;
    private readonly HotkeyId _id;
    private GCHandle _self;
    private nint _handler;
    private nint _hotkey;
    private bool _paused;
    private bool _disposed;

    internal static bool IsSupported => OperatingSystem.IsMacOS();
    internal static bool SupportsKey(uint key) => MapKey(key) is not null;
    internal bool Registered => _hotkey != 0;

    internal MacGlobalHotkey(uint modifiers, uint key, Action callback)
    {
        if (!IsSupported) throw new PlatformNotSupportedException("macOS global shortcuts are unavailable.");
        _keycode = MapKey(key) ?? throw new ArgumentException("The selected key is not supported on macOS.", nameof(key));
        _modifiers = MapModifiers(modifiers);
        _callback = callback;
        _id = new HotkeyId { Signature = Signature, Id = unchecked((uint)Interlocked.Increment(ref _nextId)) };
        _self = GCHandle.Alloc(this);
        try
        {
            var type = new EventType { Class = 0x6B657962, Kind = 5 }; // keyb / kEventHotKeyPressed
            nint handler = 0;
            var result = InstallEventHandler(GetApplicationEventTarget(), &OnHotkey, 1, &type,
                GCHandle.ToIntPtr(_self), &handler);
            if (result != 0) throw new InvalidOperationException($"Could not install macOS hotkey handler ({result}).");
            _handler = handler;
            Register();
        }
        catch { Dispose(); throw; }
    }

    private void Register()
    {
        nint hotkey = 0;
        if (RegisterEventHotKey(_keycode, _modifiers, _id, GetApplicationEventTarget(), 0, &hotkey) == 0)
            _hotkey = hotkey;
    }

    internal void PauseForRecording(bool pause)
    {
        if (_disposed || _paused == pause) return;
        _paused = pause;
        if (pause)
        {
            if (_hotkey != 0) UnregisterEventHotKey(_hotkey);
            _hotkey = 0;
        }
        else Register();
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int OnHotkey(nint nextHandler, nint eventRef, nint userData)
    {
        try
        {
            var id = default(HotkeyId);
            if (GetEventParameter(eventRef, 0x2D2D2D2D, 0x686B6964, 0, (uint)sizeof(HotkeyId), 0, &id) != 0)
                return EventNotHandled;
            if (GCHandle.FromIntPtr(userData).Target is not MacGlobalHotkey instance ||
                instance._disposed || id.Signature != instance._id.Signature || id.Id != instance._id.Id)
                return EventNotHandled;
            instance._callback();
            return 0;
        }
        catch { return EventNotHandled; } // Exceptions cannot cross the native event boundary.
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_hotkey != 0) { UnregisterEventHotKey(_hotkey); _hotkey = 0; }
        if (_handler != 0)
        {
            // Keep userData alive if Carbon could not detach the callback.
            if (RemoveEventHandler(_handler) != 0) return;
            _handler = 0;
        }
        if (_self.IsAllocated) _self.Free();
    }

    internal static uint MapModifiers(uint modifiers) =>
        ((modifiers & 1) != 0 ? 0x0800u : 0) | ((modifiers & 2) != 0 ? 0x1000u : 0) |
        ((modifiers & 4) != 0 ? 0x0200u : 0) | ((modifiers & 8) != 0 ? 0x0100u : 0);

    // ANSI physical key positions; non-US input layouts may label these keys differently.
    internal static uint? MapKey(uint key) => key switch
    {
        0x41 => 0x00, 0x42 => 0x0B, 0x43 => 0x08, 0x44 => 0x02, 0x45 => 0x0E, 0x46 => 0x03,
        0x47 => 0x05, 0x48 => 0x04, 0x49 => 0x22, 0x4A => 0x26, 0x4B => 0x28, 0x4C => 0x25,
        0x4D => 0x2E, 0x4E => 0x2D, 0x4F => 0x1F, 0x50 => 0x23, 0x51 => 0x0C, 0x52 => 0x0F,
        0x53 => 0x01, 0x54 => 0x11, 0x55 => 0x20, 0x56 => 0x09, 0x57 => 0x0D, 0x58 => 0x07,
        0x59 => 0x10, 0x5A => 0x06,
        0x30 => 0x1D, 0x31 => 0x12, 0x32 => 0x13, 0x33 => 0x14, 0x34 => 0x15,
        0x35 => 0x17, 0x36 => 0x16, 0x37 => 0x1A, 0x38 => 0x1C, 0x39 => 0x19,
        0x70 => 0x7A, 0x71 => 0x78, 0x72 => 0x63, 0x73 => 0x76, 0x74 => 0x60,
        0x75 => 0x61, 0x76 => 0x62, 0x77 => 0x64, 0x78 => 0x65, 0x79 => 0x6D,
        0x7A => 0x67, 0x7B => 0x6F, 0x7C => 0x69, 0x7D => 0x6B, 0x7E => 0x71,
        0x7F => 0x6A, 0x80 => 0x40, 0x81 => 0x4F, 0x82 => 0x50, 0x83 => 0x5A,
        0x08 => 0x33, 0x09 => 0x30, 0x0D => 0x24, 0x1B => 0x35, 0x20 => 0x31,
        0x21 => 0x74, 0x22 => 0x79, 0x23 => 0x77, 0x24 => 0x73,
        0x25 => 0x7B, 0x26 => 0x7E, 0x27 => 0x7C, 0x28 => 0x7D, 0x2E => 0x75,
        _ => null,
    };

    [StructLayout(LayoutKind.Sequential)] private struct EventType { internal uint Class, Kind; }
    [StructLayout(LayoutKind.Sequential)] private struct HotkeyId { internal uint Signature, Id; }
    [LibraryImport(Carbon)] private static partial nint GetApplicationEventTarget();
    [LibraryImport(Carbon)] private static partial int InstallEventHandler(nint target, delegate* unmanaged[Cdecl]<nint, nint, nint, int> handler, uint count, EventType* types, nint data, nint* installed);
    [LibraryImport(Carbon)] private static partial int RemoveEventHandler(nint handler);
    [LibraryImport(Carbon)] private static partial int RegisterEventHotKey(uint key, uint modifiers, HotkeyId id, nint target, uint options, nint* hotkey);
    [LibraryImport(Carbon)] private static partial int UnregisterEventHotKey(nint hotkey);
    [LibraryImport(Carbon)] private static partial int GetEventParameter(nint eventRef, uint name, uint type, nint actualType, uint size, nint actualSize, HotkeyId* data);
}
