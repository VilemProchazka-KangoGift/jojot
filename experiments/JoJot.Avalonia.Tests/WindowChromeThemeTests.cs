using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using JoJot.AvaloniaExperiment.Platform;
using Xunit;

namespace JoJot.AvaloniaExperiment.Tests;

public sealed class WindowChromeThemeTests
{
    [AvaloniaFact]
    public async Task CaptionColorsFollowLiveThemeAndUseWindowsColorRefOrdering()
    {
        await using var fixture = new MainWindowTests.WindowFixture();
        var window = fixture.Open();
        await window.Ready;
        window.RequestedThemeVariant = ThemeVariant.Light;
        Assert.Equal((0x00FAF8F7u, 0x003B3122u), WindowChromeTheme.GetColors(window));
        window.RequestedThemeVariant = ThemeVariant.Dark;
        Assert.Equal((0x002C2419u, 0x00EDEBE4u), WindowChromeTheme.GetColors(window));
        window.RequestedThemeVariant = ThemeVariant.Light;
        Assert.Equal((0x00FAF8F7u, 0x003B3122u), WindowChromeTheme.GetColors(window));
    }

    [AvaloniaFact]
    public void MissingResourcesKeepTheSystemCaptionAndNonWindowsHandlesAreIgnored()
    {
        var window = new Window();
        Assert.Equal((uint.MaxValue, uint.MaxValue), WindowChromeTheme.GetColors(window));
        WindowChromeTheme.Apply(window);
    }
}
