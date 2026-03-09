using JoJot.Services;

namespace JoJot.Tests.Services;

public class WindowPlacementHelperTests
{
    // ─── Constants ────────────────────────────────────────────────────

    [Fact]
    public void DefaultWidth_Is500()
    {
        WindowPlacementHelper.DefaultWidth.Should().Be(500);
    }

    [Fact]
    public void DefaultHeight_Is600()
    {
        WindowPlacementHelper.DefaultHeight.Should().Be(600);
    }

    [Fact]
    public void MinWidth_Is320()
    {
        WindowPlacementHelper.MinWidth.Should().Be(320);
    }

    [Fact]
    public void MinHeight_Is420()
    {
        WindowPlacementHelper.MinHeight.Should().Be(420);
    }

    // ─── ClampToNearestScreen ─────────────────────────────────────────

    [Fact]
    public void ClampToNearestScreen_VisiblePosition_ReturnsUnchanged()
    {
        // A position that should be visible on the primary screen
        var geo = new JoJot.Models.WindowGeometry(100, 100, 800, 600, false);

        var result = WindowPlacementHelper.ClampToNearestScreen(geo);

        // If position is on-screen, should return same geometry
        result.Left.Should().Be(100);
        result.Top.Should().Be(100);
        result.Width.Should().Be(800);
        result.Height.Should().Be(600);
    }

    [Fact]
    public void ClampToNearestScreen_FarOffScreen_ClampsToNearestScreen()
    {
        LogService.InitializeNoop();

        // Position far off-screen (no monitor at -5000, -5000)
        var geo = new JoJot.Models.WindowGeometry(-5000, -5000, 800, 600, false);

        var result = WindowPlacementHelper.ClampToNearestScreen(geo);

        // Should be clamped to be on-screen
        result.Left.Should().BeGreaterThan(-5000);
        result.Top.Should().BeGreaterThan(-5000);
        // Size should be preserved
        result.Width.Should().Be(800);
        result.Height.Should().Be(600);
    }

    [Fact]
    public void ClampToNearestScreen_PreservesSize()
    {
        LogService.InitializeNoop();

        var geo = new JoJot.Models.WindowGeometry(-9999, -9999, 1024, 768, false);

        var result = WindowPlacementHelper.ClampToNearestScreen(geo);

        result.Width.Should().Be(1024);
        result.Height.Should().Be(768);
    }

    [Fact]
    public void ClampToNearestScreen_PreservesMaximizedState()
    {
        LogService.InitializeNoop();

        var geo = new JoJot.Models.WindowGeometry(-9999, -9999, 800, 600, true);

        var result = WindowPlacementHelper.ClampToNearestScreen(geo);

        result.IsMaximized.Should().BeTrue();
    }

    [Fact]
    public void ClampToNearestScreen_PositiveOffScreen_ClampsToNearestScreen()
    {
        LogService.InitializeNoop();

        // Position far to the right (unlikely any monitor there)
        var geo = new JoJot.Models.WindowGeometry(99999, 99999, 800, 600, false);

        var result = WindowPlacementHelper.ClampToNearestScreen(geo);

        // Should be different from original since it's off-screen
        result.Left.Should().BeLessThan(99999);
        result.Top.Should().BeLessThan(99999);
    }
}
