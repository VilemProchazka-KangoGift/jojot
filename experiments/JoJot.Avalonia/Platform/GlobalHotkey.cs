using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace JoJot.AvaloniaExperiment.Platform;

// A dedicated message loop keeps the registration alive after the last window closes.
internal sealed unsafe partial class GlobalHotkey : IDisposable
{
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _started = new();
    private readonly Action _callback;
    private readonly uint _modifiers;
    private readonly uint _key;
    private uint _threadId;
    private Exception? _startupError;
    private bool _disposed;
    private static nint _keyboardHook;
    private static int _winHeld;
    internal static bool IsWinHeld => Volatile.Read(ref _winHeld) != 0;

    internal static string KeyName(uint key)
    {
        var scan = MapVirtualKey(key, 0);
        var extended = key is >= 0x21 and <= 0x28 or 0x2D or 0x2E or 0x6F;
        char* result = stackalloc char[128];
        var length = GetKeyNameText((int)((scan << 16) | (extended ? 0x01000000u : 0)), result, 128);
        return length > 0 ? new string(result, 0, length) : $"0x{key:X2}";
    }
    public bool Registered { get; private set; }

    public GlobalHotkey(uint modifiers, uint key, Action callback)
    {
        _modifiers = modifiers;
        _key = key;
        _callback = callback;
        _thread = new Thread(Run) { IsBackground = true, Name = "JoJot-Hotkey" };
        _thread.Start();
        _started.Wait();
        if (_startupError is not null)
            throw new InvalidOperationException("Could not initialize the global hotkey message loop.", _startupError);
    }

    private void Run()
    {
        try
        {
            _threadId = GetCurrentThreadId();
            // PeekMessage forces creation of this thread's message queue before signalling readiness.
            PeekMessage(out _, 0, 0, 0, 0);
            Registered = RegisterHotKey(0, 1, _modifiers | 0x4000, _key);
        }
        catch (Exception ex) { _startupError = ex; }
        finally { _started.Set(); }
        if (_startupError is not null) return;
        while (GetMessage(out var message, 0, 0, 0) > 0)
        {
            if (message.Message == 0x0312 && message.WParam == 1)
                _callback();
            else if (message.Message == 0x8001)
            {
                if (message.WParam == 1)
                {
                    if (Registered) UnregisterHotKey(0, 1);
                    Registered = false;
                    _keyboardHook = SetWindowsHookEx(13, &KeyboardHook, GetModuleHandle(null), 0);
                    if (_keyboardHook == 0)
                        JoJot.Services.LogService.Warn("Unable to install the global shortcut recording hook (error {Error})", Marshal.GetLastPInvokeError());
                }
                else
                {
                    if (_keyboardHook != 0) UnhookWindowsHookEx(_keyboardHook);
                    _keyboardHook = 0;
                    Volatile.Write(ref _winHeld, 0);
                    Registered = RegisterHotKey(0, 1, _modifiers | 0x4000, _key);
                }
            }
            TranslateMessage(ref message);
            DispatchMessage(ref message);
        }
        if (Registered)
            UnregisterHotKey(0, 1);
        if (_keyboardHook != 0) UnhookWindowsHookEx(_keyboardHook);
        _keyboardHook = 0;
        Volatile.Write(ref _winHeld, 0);
    }

    internal void PauseForRecording(bool pause) => PostThreadMessage(_threadId, 0x8001, pause ? 1u : 0u, 0);
    internal bool PostTestMessage() => PostThreadMessage(_threadId, 0x0312, 1, 0);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint KeyboardHook(int code, nuint message, nint data)
    {
        if (code >= 0)
        {
            var key = Marshal.ReadInt32(data);
            if (key is 0x5B or 0x5C)
            {
                if (message is 0x0100 or 0x0104) Volatile.Write(ref _winHeld, 1);
                else if (message is 0x0101 or 0x0105) Volatile.Write(ref _winHeld, 0);
                return 1;
            }
        }
        return CallNextHookEx(_keyboardHook, code, message, data);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        PostThreadMessage(_threadId, 0x0012, 0, 0);
        if (!_thread.Join(TimeSpan.FromSeconds(5)))
            throw new TimeoutException("The global hotkey thread did not stop.");
        _started.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public nint Hwnd;
        public uint Message;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public int X;
        public int Y;
        public uint Private;
    }

    [LibraryImport("kernel32.dll")] private static partial uint GetCurrentThreadId();
    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandle(string? moduleName);
    [LibraryImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(nint hwnd, int id);
    [LibraryImport("user32.dll", EntryPoint = "GetMessageW")] private static partial int GetMessage(out NativeMessage message, nint hwnd, uint min, uint max);
    [LibraryImport("user32.dll", EntryPoint = "PeekMessageW")] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PeekMessage(out NativeMessage message, nint hwnd, uint min, uint max, uint remove);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TranslateMessage(ref NativeMessage message);
    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")] private static partial nint DispatchMessage(ref NativeMessage message);
    [LibraryImport("user32.dll", EntryPoint = "PostThreadMessageW")] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostThreadMessage(uint threadId, uint message, nuint wParam, nint lParam);
    [LibraryImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    private static partial nint SetWindowsHookEx(int id, delegate* unmanaged[Stdcall]<int, nuint, nint, nint> callback, nint module, uint threadId);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnhookWindowsHookEx(nint hook);
    [LibraryImport("user32.dll")]
    private static partial nint CallNextHookEx(nint hook, int code, nuint message, nint data);
    [LibraryImport("user32.dll", EntryPoint = "MapVirtualKeyW")]
    private static partial uint MapVirtualKey(uint code, uint mapType);
    [LibraryImport("user32.dll", EntryPoint = "GetKeyNameTextW")]
    private static partial int GetKeyNameText(int lParam, char* text, int size);
}
