using Avalonia.Controls;
using System.Runtime.InteropServices;

namespace JoJot.AvaloniaExperiment.Platform;

internal static partial class WindowActivation
{
    internal static void Activate(Window window)
    {
        if (window.WindowState == WindowState.Minimized)
        {
            var handle = window.TryGetPlatformHandle();
            if (OperatingSystem.IsWindows() && handle is { HandleDescriptor: "HWND", Handle: not 0 })
                ShowWindow(handle.Handle, 9); // SW_RESTORE preserves a pre-minimize maximized state.
            else
                window.WindowState = WindowState.Normal;
        }
        window.Activate();
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(nint handle, int command);
}
