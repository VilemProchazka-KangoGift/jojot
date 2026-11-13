using Avalonia;
using Avalonia.Controls;
using JoJot.AvaloniaExperiment.Platform;
using Xunit;
using static Avalonia.Controls.Win32Properties.Win32HitTestValue;

namespace JoJot.AvaloniaExperiment.Tests;

public sealed class WindowResizeHitTestTests
{
    [Theory]
    [InlineData(3, 150, Left)]
    [InlineData(396, 150, Right)]
    [InlineData(200, 3, Top)]
    [InlineData(200, 296, Bottom)]
    [InlineData(12, 3, TopLeft)]
    [InlineData(396, 12, TopRight)]
    [InlineData(3, 287, BottomLeft)]
    [InlineData(387, 296, BottomRight)]
    public void HitsVisibleEdgesAndCornersOnNegativeCoordinateMonitor(
        int x, int y, Win32Properties.Win32HitTestValue expected)
    {
        var frame = new PixelRect(-800, -400, 400, 300);
        Assert.Equal(expected, WindowResizeHitTest.GetHitTest(frame, new PixelPoint(frame.X + x, frame.Y + y), 1));
    }

    [Fact]
    public void ScalesTheTargetsAndLeavesTheInteriorAndExteriorToWindows()
    {
        var frame = new PixelRect(100, 200, 400, 300);
        Assert.Equal(TopLeft, WindowResizeHitTest.GetHitTest(frame, new PixelPoint(120, 208), 1.5));
        Assert.Null(WindowResizeHitTest.GetHitTest(frame, new PixelPoint(120, 208), 1));
        Assert.Null(WindowResizeHitTest.GetHitTest(frame, new PixelPoint(200, 300), 1.5));
        Assert.Null(WindowResizeHitTest.GetHitTest(frame, new PixelPoint(99, 300), 1.5));
        Assert.Null(WindowResizeHitTest.GetHitTest(frame, new PixelPoint(500, 300), 1.5));
    }
}
