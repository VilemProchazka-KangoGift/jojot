using Avalonia;
using JoJot.AvaloniaExperiment.Platform;
using JoJot.Models;
using Xunit;

namespace JoJot.Avalonia.Tests;

public sealed class WindowPlacementTests
{
    [Fact]
    public void Clamp_chooses_closest_screen_edge_and_preserves_size_and_state()
    {
        var geometry = new WindowGeometry(1800, 1150, 500, 600, true);
        PixelRect[] areas = [new(0, 0, 1920, 1080), new(2200, 0, 1200, 1000)];
        var actual = WindowPlacement.Clamp(geometry, areas);
        Assert.Equal(new WindowGeometry(1420, 480, 500, 600, true), actual);
    }

    [Fact]
    public void Clamp_keeps_visible_window_and_no_screen_geometry()
    {
        var geometry = new WindowGeometry(-25, 10, 500, 600, false);
        Assert.Equal(geometry, WindowPlacement.Clamp(geometry, [new PixelRect(0, 0, 1920, 1080)]));
        Assert.Equal(geometry, WindowPlacement.Clamp(geometry, []));
    }
}
