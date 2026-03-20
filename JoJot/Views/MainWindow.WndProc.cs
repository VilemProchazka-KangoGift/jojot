using System.Windows.Interop;
using JoJot.Services;

namespace JoJot;

public partial class MainWindow
{
    private const int WM_ACTIVATE = 0x0006;
    private const int WA_INACTIVE = 0;

    private HwndSource? _hwndSource;

    /// <summary>
    /// Hooks the Win32 message loop after the window handle is created.
    /// Monitors WM_ACTIVATE to record activation timestamps for cross-desktop detection.
    /// </summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _hwndSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        _hwndSource?.AddHook(DesktopActivationHook);
    }

    /// <summary>
    /// WndProc hook that records every window activation with its timestamp.
    /// No decision is made here — the timestamp is compared against the COM
    /// desktop-switch timestamp in <see cref="DesktopSwitchDetector.WasCrossDesktopActivation"/>.
    /// For taskbar clicks, WM_ACTIVATE arrives BEFORE the desktop switch (SetForegroundWindow
    /// is synchronous). For deliberate switches, WM_ACTIVATE arrives AFTER the switch.
    /// </summary>
    private IntPtr DesktopActivationHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_ACTIVATE && (wParam.ToInt32() & 0xFFFF) != WA_INACTIVE)
        {
            DesktopSwitchDetector.NotifyWindowActivated(_desktopGuid);
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// Removes the WndProc hook on window close to prevent callbacks into disposed state.
    /// Called from OnClosing to ensure cleanup before the HWND is destroyed.
    /// </summary>
    private void RemoveDesktopActivationHook()
    {
        _hwndSource?.RemoveHook(DesktopActivationHook);
        _hwndSource = null;
    }
}
