using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Xunit;

namespace JoJot.AvaloniaExperiment.Tests;

public sealed class FindContrastTests
{
    [AvaloniaFact]
    public async Task DisabledCleanupActionStaysReadableAndIgnoresPointerInput()
    {
        await using var fixture = new MainWindowTests.WindowFixture();
        var window = fixture.Open();
        await window.Ready;
        window.FindControl<MenuItem>("CleanupMenuItem")!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        var host = window.FindControl<Border>("PanelHost")!;
        await MainWindowTests.WaitForAsync(() => host.GetVisualDescendants().OfType<Button>().Any(button => button.Classes.Contains("panelDanger")), "cleanup action");
        var button = host.GetVisualDescendants().OfType<Button>().Single(button => button.Classes.Contains("panelDanger"));
        var clicks = 0;
        button.Click += (_, _) => clicks++;
        foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            window.RequestedThemeVariant = theme;
            Assert.False(button.IsEffectivelyEnabled);
            Assert.Equal(1, button.Opacity);
            var presenter = button.GetVisualDescendants().OfType<ContentPresenter>().Single();
            Assert.True(Contrast(Brush(presenter.Background), Brush(presenter.Foreground)) >= 4.5);
            var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
            window.MouseMove(point);
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Assert.False(button.IsPressed);
            Assert.Equal(0, clicks);
            Assert.Empty(window.OwnedWindows);
        }
    }

    [AvaloniaFact]
    public async Task FindAndToolbarStatesKeepReadableThemeContrast()
    {
        await using var fixture = new MainWindowTests.WindowFixture();
        var window = fixture.Open();
        await window.Ready;
        var editor = window.FindControl<TextBox>("Editor")!;
        editor.Text = "note and another note";
        foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            window.RequestedThemeVariant = theme;
            Color Resource(string key)
            {
                Assert.True(window.TryFindResource(key, theme, out var value));
                return Assert.IsAssignableFrom<ISolidColorBrush>(value).Color;
            }
            var overlay = Resource("c-find-match-bg");
            Assert.InRange(overlay.A, (byte)30, (byte)51);
            Assert.True(Contrast(Over(overlay, Resource("c-text-primary")), Over(overlay, Resource("c-editor-bg"))) >= 7);
            Assert.True(Contrast(Resource("c-find-match-border"), Resource("c-editor-bg")) >= 3);
            window.FindControl<Button>("FindButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(Contrast(Brush(editor.SelectionBrush), Brush(editor.SelectionForegroundBrush)) >= 7);
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Assert.DoesNotContain("findActive", editor.Classes);
            Assert.True(Contrast(Brush(editor.SelectionBrush), Brush(editor.SelectionForegroundBrush)) >= 4.5);
            foreach (var name in new[] { "AddButton", "CopyButton" })
            {
                var button = window.FindControl<Button>(name)!;
                var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
                window.MouseMove(point);
                Assert.True(button.IsPointerOver);
                var presenter = button.GetVisualDescendants().OfType<ContentPresenter>().Single();
                Assert.True(Contrast(Brush(presenter.Background), Brush(presenter.Foreground)) >= 4.5);
                window.MouseDown(point, MouseButton.Left);
                Assert.True(button.IsPressed);
                Assert.True(Contrast(Brush(presenter.Background), Brush(presenter.Foreground)) >= 4.5);
                window.MouseMove(new Point(-1, -1));
                window.MouseUp(new Point(-1, -1), MouseButton.Left);
            }
        }
    }

    private static Color Brush(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;
    private static Color Over(Color source, Color target)
    {
        var alpha = source.A / 255d;
        byte Mix(byte a, byte b) => (byte)Math.Round(a * alpha + b * (1 - alpha));
        return Color.FromRgb(Mix(source.R, target.R), Mix(source.G, target.G), Mix(source.B, target.B));
    }
    private static double Contrast(Color a, Color b)
    {
        static double Luminance(Color color)
        {
            static double Linear(byte channel) { var value = channel / 255d; return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4); }
            return .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
        }
        var x = Luminance(a); var y = Luminance(b);
        return (Math.Max(x, y) + .05) / (Math.Min(x, y) + .05);
    }
}
