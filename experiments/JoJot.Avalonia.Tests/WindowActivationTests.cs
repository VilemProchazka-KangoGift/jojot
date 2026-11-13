using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using JoJot.AvaloniaExperiment.Platform;
using Xunit;

namespace JoJot.AvaloniaExperiment.Tests;

public sealed class WindowActivationTests
{
    [AvaloniaFact]
    public void Restores_minimized_window_for_an_incoming_launch()
    {
        var window = new Window();
        window.Show();
        try
        {
            window.WindowState = WindowState.Minimized;
            WindowActivation.Activate(window);
            Assert.Equal(WindowState.Normal, window.WindowState);
        }
        finally { window.Close(); }
    }
}
