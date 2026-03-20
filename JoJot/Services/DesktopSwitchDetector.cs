using System.Diagnostics;
using System.Runtime.InteropServices;

namespace JoJot.Services;

/// <summary>
/// Detects whether a desktop switch was deliberate (Task View, Ctrl+Win+Arrow, touchpad)
/// or caused by a cross-desktop window activation (taskbar click on JoJot).
/// Two layers:
///   1. Timestamp ordering: WM_ACTIVATE records activation time, COM callback records switch time.
///      For taskbar clicks, SetForegroundWindow sends WM_ACTIVATE BEFORE the shell switches
///      desktops (activation < switch). For deliberate switches, the desktop switches first
///      and then the window is activated (switch < activation).
///   2. Low-level keyboard hook detecting Ctrl+Win+Arrow, Alt+Tab, Win+Tab as a safety net.
/// The redirect requires activation-before-switch AND no recent keyboard navigation.
/// </summary>
public static class DesktopSwitchDetector
{
    // ─── P/Invoke ────────────────────────────────────────────────────────

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;

    private const int VK_LEFT = 0x25;
    private const int VK_RIGHT = 0x27;
    private const int VK_TAB = 0x09;
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;
    private const int VK_LCONTROL = 0xA2;
    private const int VK_RCONTROL = 0xA3;
    private const int VK_LMENU = 0xA4;  // Left Alt
    private const int VK_RMENU = 0xA5;  // Right Alt

    // ─── Activation / switch timestamp tracking ──────────────────────────

    private static long _lastActivationTicks;           // Stopwatch ticks when WM_ACTIVATE fired
    private static string? _lastActivatedDesktopGuid;   // desktop GUID of the activated window
    private static long _lastDesktopSwitchTicks;        // Stopwatch ticks when COM callback fired

    // Snapshot of cross-desktop detection result, set by NotifyDesktopSwitch
    private static bool _lastSwitchWasCrossDesktop;
    private static string? _lastCrossDesktopTargetGuid;

    /// <summary>
    /// Called from MainWindow WndProc on EVERY WM_ACTIVATE (not WA_INACTIVE).
    /// Records the activation timestamp and which window's desktop was activated.
    /// No decision is made here — the timestamp is compared against the COM
    /// callback timestamp later in <see cref="WasCrossDesktopActivation"/>.
    /// </summary>
    public static void NotifyWindowActivated(string windowDesktopGuid)
    {
        Volatile.Write(ref _lastActivatedDesktopGuid, windowDesktopGuid);
        Volatile.Write(ref _lastActivationTicks, Stopwatch.GetTimestamp());
    }

    /// <summary>
    /// Called from VirtualDesktopService when the COM desktop-changed callback fires
    /// (or from PollDesktopChange in fallback mode). Snapshots the activation state,
    /// evaluates cross-desktop detection, stores the result, then clears activation
    /// timestamps to prevent stale values from triggering on the next switch.
    /// <paramref name="newDesktopGuid"/> is the desktop being switched TO — used to verify
    /// the activation was for a window on the destination desktop (taskbar click pattern)
    /// rather than a window on the source desktop (normal interaction before a deliberate switch).
    /// </summary>
    public static void NotifyDesktopSwitch(string newDesktopGuid)
    {
        long switchTicks = Stopwatch.GetTimestamp();

        // Snapshot activation state before clearing
        long activationTicks = Volatile.Read(ref _lastActivationTicks);
        string? activatedGuid = Volatile.Read(ref _lastActivatedDesktopGuid);

        // Cross-desktop activation requires:
        // 1. An activation was recorded
        // 2. The activated window is on the DESTINATION desktop (taskbar click activates
        //    a window on the desktop being switched to; normal interaction activates a
        //    window on the source desktop — filtering this out prevents false positives
        //    when the user interacts with JoJot then deliberately switches away)
        // 3. The activation-to-switch gap is in the taskbar click range (50ms–2s)
        bool wasCross = activationTicks != 0
            && activatedGuid is not null
            && activatedGuid.Equals(newDesktopGuid, StringComparison.OrdinalIgnoreCase)
            && IsActivationBeforeSwitch(activationTicks, switchTicks, Stopwatch.Frequency);

        // Store result for WasCrossDesktopActivation queries
        Volatile.Write(ref _lastSwitchWasCrossDesktop, wasCross);
        Volatile.Write(ref _lastCrossDesktopTargetGuid, activatedGuid);

        // Clear activation state — prevents stale timestamps from triggering on next switch
        Volatile.Write(ref _lastActivationTicks, 0L);
        Volatile.Write(ref _lastActivatedDesktopGuid, null);

        Volatile.Write(ref _lastDesktopSwitchTicks, switchTicks);
    }

    /// <summary>
    /// Returns true if the last desktop switch was preceded by a cross-desktop window
    /// activation on <paramref name="targetDesktopGuid"/> — indicating a taskbar click.
    /// Uses the snapshot computed by <see cref="NotifyDesktopSwitch"/> rather than
    /// re-reading raw timestamps, which prevents stale activation data from causing
    /// false positives on subsequent switches.
    /// </summary>
    public static bool WasCrossDesktopActivation(string targetDesktopGuid)
    {
        if (!Volatile.Read(ref _lastSwitchWasCrossDesktop))
            return false;

        string? guid = Volatile.Read(ref _lastCrossDesktopTargetGuid);
        return guid is not null
            && guid.Equals(targetDesktopGuid, StringComparison.OrdinalIgnoreCase);
    }

    // ─── Keyboard hook ───────────────────────────────────────────────────

    private static IntPtr _hookHandle;
    private static LowLevelKeyboardProc? _hookProc; // prevent GC collection of delegate
    private static long _lastKeyboardNavTicks;

    /// <summary>
    /// Returns true if keyboard-based desktop navigation (Ctrl+Win+Arrow, Alt+Tab, Win+Tab)
    /// was detected within the last 5 seconds.
    /// </summary>
    public static bool IsRecentKeyboardNavigation
    {
        get
        {
            long ticks = Volatile.Read(ref _lastKeyboardNavTicks);
            return IsNavigationRecent(ticks, Stopwatch.GetTimestamp(), Stopwatch.Frequency);
        }
    }

    /// <summary>
    /// Installs the low-level keyboard hook. Called once during App startup.
    /// </summary>
    public static void Initialize()
    {
        if (_hookHandle != IntPtr.Zero) return;

        _hookProc = HookCallback;
        _hookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc, GetModuleHandle(null), 0);

        if (_hookHandle == IntPtr.Zero)
        {
            LogService.Warn("DesktopSwitchDetector: failed to install keyboard hook (Win32 error {Win32Error})",
                Marshal.GetLastWin32Error());
        }
        else
        {
            LogService.Info("DesktopSwitchDetector: keyboard hook installed");
        }
    }

    /// <summary>
    /// Removes the keyboard hook. Called during App shutdown.
    /// </summary>
    public static void Shutdown()
    {
        if (_hookHandle != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
            _hookProc = null;
            LogService.Info("DesktopSwitchDetector: keyboard hook removed");
        }
    }

    /// <summary>
    /// Low-level keyboard hook callback. Detects desktop-switching key combos:
    /// - Ctrl+Win+Left/Right (direct desktop switch)
    /// - Alt+Tab (window switcher — can target cross-desktop windows)
    /// - Win+Tab (Task View)
    /// NEVER swallows keys — always calls CallNextHookEx.
    /// </summary>
    private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = wParam.ToInt32();
            if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
            {
                int vkCode = Marshal.ReadInt32(lParam);

                if (IsDesktopNavigationKey(vkCode))
                {
                    Volatile.Write(ref _lastKeyboardNavTicks, Stopwatch.GetTimestamp());
                }
            }
        }

        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    /// <summary>
    /// Checks if the given virtual key code, combined with current modifier state,
    /// represents a desktop-switching key combination.
    /// </summary>
    private static bool IsDesktopNavigationKey(int vkCode)
    {
        // Ctrl+Win+Left/Right — direct desktop switch
        if ((vkCode == VK_LEFT || vkCode == VK_RIGHT)
            && IsKeyDown(VK_LCONTROL, VK_RCONTROL)
            && IsKeyDown(VK_LWIN, VK_RWIN))
        {
            return true;
        }

        // Alt+Tab — window switcher (can switch desktops)
        if (vkCode == VK_TAB && IsKeyDown(VK_LMENU, VK_RMENU))
        {
            return true;
        }

        // Win+Tab — Task View
        if (vkCode == VK_TAB && IsKeyDown(VK_LWIN, VK_RWIN))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Returns true if either of the two virtual keys (left/right variant) is pressed.
    /// </summary>
    private static bool IsKeyDown(int vkLeft, int vkRight)
    {
        return (GetAsyncKeyState(vkLeft) & 0x8000) != 0
            || (GetAsyncKeyState(vkRight) & 0x8000) != 0;
    }

    // ─── Pure functions for unit testing ─────────────────────────────────

    /// <summary>
    /// Returns true if <paramref name="lastNavTicks"/> is within 5 seconds of
    /// <paramref name="nowTicks"/> given the <paramref name="frequency"/>.
    /// Returns false if <paramref name="lastNavTicks"/> is 0 (never navigated).
    /// </summary>
    internal static bool IsNavigationRecent(long lastNavTicks, long nowTicks, long frequency)
    {
        if (lastNavTicks == 0) return false;
        long elapsed = nowTicks - lastNavTicks;
        return elapsed >= 0 && elapsed < frequency * 5; // 5 seconds
    }

    /// <summary>
    /// Returns true if <paramref name="activationTicks"/> occurred BEFORE
    /// <paramref name="switchTicks"/> with a gap between 50ms and 2 seconds.
    /// This indicates the window was activated cross-desktop before the shell
    /// switched desktops (taskbar click pattern, typically 50-200ms gap).
    /// Returns false if either timestamp is 0, activation came after the switch
    /// (deliberate switch pattern), or the gap is under 50ms (near-simultaneous
    /// events from WM_ACTIVATE racing the COM callback during deliberate switches).
    /// </summary>
    internal static bool IsActivationBeforeSwitch(long activationTicks, long switchTicks, long frequency)
    {
        if (activationTicks == 0 || switchTicks == 0) return false;
        long gap = switchTicks - activationTicks;
        long minGap = frequency / 20; // 50ms — filters near-simultaneous events
        return gap > minGap && gap < frequency * 2;
    }
}
