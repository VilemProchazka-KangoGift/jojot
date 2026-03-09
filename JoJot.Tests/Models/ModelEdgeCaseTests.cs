using System.Text.Json;
using JoJot.Models;

namespace JoJot.Tests.Models;

/// <summary>
/// Edge case tests for IPC messages, AppState, WindowGeometry, PendingMove.
/// </summary>
public class ModelEdgeCaseTests
{
    // ─── IPC edge cases ─────────────────────────────────────────────

    [Fact]
    public void IpcMessage_UnknownDiscriminator_Throws()
    {
        var json = """{"action":"unknown"}""";
        var act = () => JsonSerializer.Deserialize(json, IpcMessageContext.Default.IpcMessage);
        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void IpcMessage_CaseSensitive_UpperCaseActivate_Throws()
    {
        var json = """{"action":"ACTIVATE"}""";
        var act = () => JsonSerializer.Deserialize(json, IpcMessageContext.Default.IpcMessage);
        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void IpcMessage_MixedCaseAction_Throws()
    {
        var json = """{"action":"Activate"}""";
        var act = () => JsonSerializer.Deserialize(json, IpcMessageContext.Default.IpcMessage);
        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void NewTabCommand_EmptyInitialContent()
    {
        var cmd = new NewTabCommand("", "desk-1");
        cmd.InitialContent.Should().Be("");

        IpcMessage msg = cmd;
        var json = JsonSerializer.Serialize(msg, IpcMessageContext.Default.IpcMessage);
        var deserialized = (NewTabCommand)JsonSerializer.Deserialize(json, IpcMessageContext.Default.IpcMessage)!;
        deserialized.InitialContent.Should().Be("");
    }

    [Fact]
    public void NewTabCommand_EmptyDesktopGuid()
    {
        var cmd = new NewTabCommand("content", "");
        cmd.DesktopGuid.Should().Be("");
    }

    // ─── AppState edge cases ────────────────────────────────────────

    [Fact]
    public void AppState_NegativeCoordinates_MultiMonitor()
    {
        var state = new AppState
        {
            WindowLeft = -100,
            WindowTop = -50,
            WindowWidth = 800,
            WindowHeight = 600
        };

        state.WindowLeft.Should().Be(-100);
        state.WindowTop.Should().Be(-50);
    }

    [Fact]
    public void AppState_ZeroDimensions()
    {
        var state = new AppState
        {
            WindowWidth = 0,
            WindowHeight = 0
        };

        state.WindowWidth.Should().Be(0);
        state.WindowHeight.Should().Be(0);
    }

    [Fact]
    public void AppState_NegativeActiveTabId()
    {
        var state = new AppState { ActiveTabId = -1 };
        state.ActiveTabId.Should().Be(-1);
    }

    [Fact]
    public void AppState_NegativeScrollOffset()
    {
        var state = new AppState { ScrollOffset = -10.5 };
        state.ScrollOffset.Should().Be(-10.5);
    }

    // ─── WindowGeometry edge cases ──────────────────────────────────

    [Fact]
    public void WindowGeometry_NegativeCoords_MultiMonitor()
    {
        var geo = new WindowGeometry(-1920, -200, 1920, 1080, false);
        geo.Left.Should().Be(-1920);
        geo.Top.Should().Be(-200);
    }

    [Fact]
    public void WindowGeometry_ZeroDimensions()
    {
        var geo = new WindowGeometry(0, 0, 0, 0, false);
        geo.Width.Should().Be(0);
        geo.Height.Should().Be(0);
    }

    [Fact]
    public void WindowGeometry_VeryLargeValues()
    {
        var geo = new WindowGeometry(10000, 10000, 7680, 4320, true);
        geo.Left.Should().Be(10000);
        geo.Width.Should().Be(7680);
    }

    // ─── PendingMove edge cases ─────────────────────────────────────

    [Fact]
    public void PendingMove_SameFromAndTo()
    {
        var move = new PendingMove(1, "win-1", "desk-A", "desk-A", "2025-01-01");
        move.FromDesktop.Should().Be("desk-A");
        move.ToDesktop.Should().Be("desk-A");
    }

    [Fact]
    public void PendingMove_EmptyDesktopGuids()
    {
        var move = new PendingMove(1, "", "", "", "");
        move.WindowId.Should().Be("");
        move.FromDesktop.Should().Be("");
        move.ToDesktop.Should().Be("");
        move.DetectedAt.Should().Be("");
    }
}
