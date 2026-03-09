using JoJot.Models;

namespace JoJot.Tests.Models;

public class AppStateTests
{
    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var state = new AppState();

        state.Id.Should().Be(0);
        state.DesktopGuid.Should().Be("");
        state.DesktopName.Should().BeNull();
        state.DesktopIndex.Should().BeNull();
        state.WindowLeft.Should().BeNull();
        state.WindowTop.Should().BeNull();
        state.WindowWidth.Should().BeNull();
        state.WindowHeight.Should().BeNull();
        state.ActiveTabId.Should().BeNull();
        state.ScrollOffset.Should().BeNull();
        state.WindowState.Should().BeNull();
    }

    [Fact]
    public void Properties_CanBeSet()
    {
        var state = new AppState
        {
            Id = 1,
            DesktopGuid = "guid-123",
            DesktopName = "My Desktop",
            DesktopIndex = 2,
            WindowLeft = 100.5,
            WindowTop = 200.5,
            WindowWidth = 800,
            WindowHeight = 600,
            ActiveTabId = 42,
            ScrollOffset = 150.7,
            WindowState = "Maximized"
        };

        state.Id.Should().Be(1);
        state.DesktopGuid.Should().Be("guid-123");
        state.DesktopName.Should().Be("My Desktop");
        state.DesktopIndex.Should().Be(2);
        state.WindowLeft.Should().Be(100.5);
        state.WindowTop.Should().Be(200.5);
        state.WindowWidth.Should().Be(800);
        state.WindowHeight.Should().Be(600);
        state.ActiveTabId.Should().Be(42);
        state.ScrollOffset.Should().Be(150.7);
        state.WindowState.Should().Be("Maximized");
    }
}
