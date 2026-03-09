using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace JoJot.Services;

/// <summary>
/// Global hotkey service using Win32 RegisterHotKey P/Invoke.
/// Registers a system-wide hotkey that works from any application.
/// Uses HwndSource message hook to receive WM_HOTKEY messages.
/// Default hotkey: Win+Shift+N. Persists custom hotkey to preferences database.
/// </summary>
public static class HotkeyService
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const int WM_HOTKEY = 0x0312;
    private const int HOTKEY_ID = 0x9001;

    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;
    private const uint MOD_NOREPEAT = 0x4000;

    // Default: Win+Shift+N
    private static uint _modifiers = MOD_WIN | MOD_SHIFT;
    private static uint _vk = 0x4E; // VK_N

    private static IntPtr _hwnd;
    private static HwndSource? _source;
    private static Action? _onHotkeyPressed;
    private static bool _isRegistered;

    /// <summary>
    /// Initializes the global hotkey service. Must be called after the window has a valid HWND.
    /// Loads saved hotkey from preferences and registers it.
    /// Returns false if the hotkey is already in use by another application.
    /// </summary>
    /// <param name="window">The window to use for hotkey message reception.</param>
    /// <param name="onHotkeyPressed">Callback invoked when the hotkey is pressed.</param>
    public static async Task<bool> InitializeAsync(Window window, Action onHotkeyPressed)
    {
        _onHotkeyPressed = onHotkeyPressed;
        _hwnd = new WindowInteropHelper(window).Handle;
        _source = HwndSource.FromHwnd(_hwnd);
        _source?.AddHook(WndProc);

        var savedModifiers = await DatabaseService.GetPreferenceAsync("hotkey_modifiers").ConfigureAwait(false);
        var savedVk = await DatabaseService.GetPreferenceAsync("hotkey_vk").ConfigureAwait(false);

        if (savedModifiers is not null && uint.TryParse(savedModifiers, out var mod))
        {
            _modifiers = mod;
        }

        if (savedVk is not null && uint.TryParse(savedVk, out var vk))
        {
            _vk = vk;
        }

        bool success = RegisterHotKey(_hwnd, HOTKEY_ID, _modifiers | MOD_NOREPEAT, _vk);
        if (!success)
        {
            int error = Marshal.GetLastWin32Error();
            LogService.Warn("Global hotkey registration failed (Win32 error {Win32Error}). Key may be in use by another app.", error);
        }
        else
        {
            _isRegistered = true;
            LogService.Info("Global hotkey registered: {HotkeyDisplay}", GetHotkeyDisplayString());
        }

        return success;
    }

    /// <summary>
    /// Updates the registered hotkey to a new key combination.
    /// Unregisters the current hotkey, registers the new one, and persists to database.
    /// Returns false if the new hotkey is already in use.
    /// </summary>
    /// <param name="modifiers">Win32 modifier flags.</param>
    /// <param name="vk">Virtual key code.</param>
    public static async Task<bool> UpdateHotkeyAsync(uint modifiers, uint vk)
    {
        if (_isRegistered)
        {
            UnregisterHotKey(_hwnd, HOTKEY_ID);
            _isRegistered = false;
        }

        bool success = RegisterHotKey(_hwnd, HOTKEY_ID, modifiers | MOD_NOREPEAT, vk);
        if (success)
        {
            _modifiers = modifiers;
            _vk = vk;
            _isRegistered = true;

            await DatabaseService.SetPreferenceAsync("hotkey_modifiers", modifiers.ToString()).ConfigureAwait(false);
            await DatabaseService.SetPreferenceAsync("hotkey_vk", vk.ToString()).ConfigureAwait(false);

            LogService.Info("Global hotkey updated: {HotkeyDisplay}", GetHotkeyDisplayString());
        }
        else
        {
            int error = Marshal.GetLastWin32Error();
            LogService.Warn("Failed to register new hotkey (Win32 error {Win32Error}). Restoring previous.", error);

            RegisterHotKey(_hwnd, HOTKEY_ID, _modifiers | MOD_NOREPEAT, _vk);
            _isRegistered = true;
        }

        return success;
    }

    /// <summary>
    /// Returns the current hotkey modifiers and virtual key code.
    /// </summary>
    public static (uint modifiers, uint vk) GetCurrentHotkey() => (_modifiers, _vk);

    /// <summary>
    /// Formats the current hotkey as a human-readable string (e.g., "Win + Shift + N").
    /// </summary>
    public static string GetHotkeyDisplayString() => FormatHotkey(_modifiers, _vk);

    /// <summary>
    /// Formats any modifier+vk combination as a human-readable string.
    /// </summary>
    /// <param name="modifiers">Win32 modifier flags.</param>
    /// <param name="vk">Virtual key code.</param>
    public static string FormatHotkey(uint modifiers, uint vk)
    {
        List<string> parts = [];

        if ((modifiers & MOD_WIN) != 0)
        {
            parts.Add("Win");
        }

        if ((modifiers & MOD_CONTROL) != 0)
        {
            parts.Add("Ctrl");
        }

        if ((modifiers & MOD_ALT) != 0)
        {
            parts.Add("Alt");
        }

        if ((modifiers & MOD_SHIFT) != 0)
        {
            parts.Add("Shift");
        }

        try
        {
            var key = KeyInterop.KeyFromVirtualKey((int)vk);
            parts.Add(key.ToString());
        }
        catch
        {
            // Intentional silent catch: KeyFromVirtualKey may throw for
            // unrecognized virtual key codes. Fall back to hex representation.
            parts.Add($"0x{vk:X2}");
        }

        return string.Join(" + ", parts);
    }

    /// <summary>
    /// Converts WPF <see cref="ModifierKeys"/> to Win32 modifier flags for RegisterHotKey.
    /// </summary>
    /// <param name="modifiers">The WPF modifier keys to convert.</param>
    public static uint ModifierKeysToWin32(ModifierKeys modifiers)
    {
        uint result = 0;
        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            result |= MOD_ALT;
        }

        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            result |= MOD_CONTROL;
        }

        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            result |= MOD_SHIFT;
        }

        if (modifiers.HasFlag(ModifierKeys.Windows))
        {
            result |= MOD_WIN;
        }

        return result;
    }

    /// <summary>
    /// Temporarily unregisters the global hotkey so it can be re-recorded.
    /// Called when user starts hotkey recording in preferences.
    /// </summary>
    public static void PauseHotkey()
    {
        if (_isRegistered && _hwnd != IntPtr.Zero)
        {
            UnregisterHotKey(_hwnd, HOTKEY_ID);
            _isRegistered = false;
            LogService.Info("Global hotkey paused for recording");
        }
    }

    /// <summary>
    /// Re-registers the global hotkey after recording is cancelled.
    /// Called when user cancels hotkey recording or closes preferences.
    /// </summary>
    public static void ResumeHotkey()
    {
        if (!_isRegistered && _hwnd != IntPtr.Zero)
        {
            bool success = RegisterHotKey(_hwnd, HOTKEY_ID, _modifiers | MOD_NOREPEAT, _vk);
            _isRegistered = success;
            if (success)
            {
                LogService.Info("Global hotkey resumed: {HotkeyDisplay}", GetHotkeyDisplayString());
            }
            else
            {
                LogService.Warn("Failed to resume global hotkey after recording cancel");
            }
        }
    }

    /// <summary>
    /// Cleans up: unregisters the global hotkey and removes the message hook.
    /// Called from App.OnExit.
    /// </summary>
    public static void Shutdown()
    {
        if (_isRegistered && _hwnd != IntPtr.Zero)
        {
            UnregisterHotKey(_hwnd, HOTKEY_ID);
            _isRegistered = false;
        }

        _source?.RemoveHook(WndProc);
        _source = null;
        LogService.Info("Global hotkey service shutdown");
    }

    /// <summary>
    /// Win32 message hook for receiving WM_HOTKEY messages.
    /// </summary>
    private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
        {
            _onHotkeyPressed?.Invoke();
            handled = true;
        }

        return IntPtr.Zero;
    }
}
