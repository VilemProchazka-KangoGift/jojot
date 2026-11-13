using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;

namespace JoJot.AvaloniaExperiment.Platform;

internal static partial class WindowChromeTheme
{
    internal const uint CaptionColor = 35;
    internal const uint TextColor = 36;

    internal static void Attach(Window window)
    {
        window.Opened += Update;
        window.ActualThemeVariantChanged += Update;
        // Avalonia also updates native theme attributes; apply our token colors afterwards.
        void Update(object? sender, EventArgs args) => Dispatcher.UIThread.Post(() =>
        {
            if (window.IsVisible) Apply(window);
        }, DispatcherPriority.Render);
    }

    internal static (uint Caption, uint Text) GetColors(Window window) =>
        (GetColor(window, "c-win-bg"), GetColor(window, "c-text-primary"));

    private static uint GetColor(Window window, string key) =>
        window.TryFindResource(key, window.ActualThemeVariant, out var resource) && resource is ISolidColorBrush brush
            ? (uint)(brush.Color.R | brush.Color.G << 8 | brush.Color.B << 16)
            : uint.MaxValue; // DWMWA_COLOR_DEFAULT restores the system-owned appearance.

    internal static void Apply(Window window)
    {
        if (!OperatingSystem.IsWindows() || window.TryGetPlatformHandle() is not { HandleDescriptor: "HWND" } platform)
            return;
        var dark = window.ActualThemeVariant == ThemeVariant.Dark ? 1u : 0u;
        _ = DwmSetWindowAttribute(platform.Handle, 20, in dark, sizeof(uint));
        var colors = GetColors(window);
        // Unsupported attributes return an HRESULT; leave native chrome intact on older Windows.
        _ = DwmSetWindowAttribute(platform.Handle, CaptionColor, in colors.Caption, sizeof(uint));
        _ = DwmSetWindowAttribute(platform.Handle, TextColor, in colors.Text, sizeof(uint));
    }

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint hwnd, uint attribute, in uint value, int size);
}
