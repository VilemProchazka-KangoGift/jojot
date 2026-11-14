using System.Diagnostics;
using JoJot.AvaloniaExperiment.Platform;
using Xunit;

namespace JoJot.AvaloniaExperiment.Tests;

public sealed class X11GlobalHotkeyTests
{
    [Fact]
    public void MapsCommonWindowsVirtualKeysToX11Keysyms()
    {
        Assert.Equal((nuint)'n', X11GlobalHotkey.ToKeysym(0x4E));
        Assert.Equal((nuint)0xFFC8, X11GlobalHotkey.ToKeysym(0x7A)); // F11
        Assert.Equal((nuint)0, X11GlobalHotkey.ToKeysym(0));
    }

    [Fact]
    public void PrivateXServerDeliversRealKeyEventsAndReleasesGrab()
    {
        // This test must never send input to the user's display.
        if (!OperatingSystem.IsLinux() || Environment.GetEnvironmentVariable("JOJOT_TEST_X11") != "private-xvfb") return;
        using var hit = new AutoResetEvent(false);
        using var shortcut = new X11GlobalHotkey(3, 0x4E, () => hit.Set()); // Ctrl+Alt+N
        Assert.True(shortcut.Registered);
        SendKey();
        Assert.True(hit.WaitOne(TimeSpan.FromSeconds(5)));
        using (var conflict = new X11GlobalHotkey(3, 0x4E, () => { })) Assert.False(conflict.Registered);
        SendKey();
        Assert.True(hit.WaitOne(TimeSpan.FromSeconds(5)));
        shortcut.PauseForRecording(true);
        Assert.False(shortcut.Registered);
        SendKey();
        Assert.False(hit.WaitOne(TimeSpan.FromMilliseconds(250)));
        shortcut.PauseForRecording(false);
        Assert.True(shortcut.Registered);
        SendKey();
        Assert.True(hit.WaitOne(TimeSpan.FromSeconds(5)));
    }

    private static void SendKey()
    {
        using var process = Process.Start(new ProcessStartInfo("xdotool")
        {
            UseShellExecute = false,
            ArgumentList = { "key", "ctrl+alt+n" },
        })!;
        Assert.True(process.WaitForExit(5000));
        Assert.Equal(0, process.ExitCode);
    }
}
