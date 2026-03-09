using System.Text.Json;
using JoJot.Models;

namespace JoJot.Tests.Models;

/// <summary>
/// Tests for model record property setters and IPC message serialization
/// targeting uncovered property setters in PendingMove, WindowGeometry,
/// NewTabCommand, ShowDesktopCommand.
/// </summary>
public class ModelCoverageTests
{
    // ─── PendingMove ────────────────────────────────────────────────

    [Fact]
    public void PendingMove_Properties_RoundTrip()
    {
        var move = new PendingMove(1, "win-1", "from-desk", "to-desk", "2025-06-15");

        move.Id.Should().Be(1);
        move.WindowId.Should().Be("win-1");
        move.FromDesktop.Should().Be("from-desk");
        move.ToDesktop.Should().Be("to-desk");
        move.DetectedAt.Should().Be("2025-06-15");
    }

    [Fact]
    public void PendingMove_With_CreatesModifiedCopy()
    {
        var original = new PendingMove(1, "win-1", "from", "to", "2025-01-01");
        var modified = original with { Id = 2, WindowId = "win-2", FromDesktop = "new-from", DetectedAt = "2025-06-15" };

        modified.Id.Should().Be(2);
        modified.WindowId.Should().Be("win-2");
        modified.FromDesktop.Should().Be("new-from");
        modified.DetectedAt.Should().Be("2025-06-15");
        modified.ToDesktop.Should().Be("to");
    }

    [Fact]
    public void PendingMove_NullToDesktop_Allowed()
    {
        var move = new PendingMove(1, "win-1", "from", null, "2025-01-01");
        move.ToDesktop.Should().BeNull();
    }

    // ─── WindowGeometry ─────────────────────────────────────────────

    [Fact]
    public void WindowGeometry_Properties_RoundTrip()
    {
        var geo = new WindowGeometry(100, 200, 800, 600, false);

        geo.Left.Should().Be(100);
        geo.Top.Should().Be(200);
        geo.Width.Should().Be(800);
        geo.Height.Should().Be(600);
        geo.IsMaximized.Should().BeFalse();
    }

    [Fact]
    public void WindowGeometry_With_ModifiesProperties()
    {
        var geo = new WindowGeometry(0, 0, 1024, 768, false);
        var maximized = geo with { Height = 1080, IsMaximized = true };

        maximized.Height.Should().Be(1080);
        maximized.IsMaximized.Should().BeTrue();
        maximized.Width.Should().Be(1024);
    }

    [Fact]
    public void WindowGeometry_Equality()
    {
        var a = new WindowGeometry(10, 20, 800, 600, true);
        var b = new WindowGeometry(10, 20, 800, 600, true);
        a.Should().Be(b);
    }

    // ─── IPC Messages ───────────────────────────────────────────────

    [Fact]
    public void ActivateCommand_IsIpcMessage()
    {
        IpcMessage msg = new ActivateCommand();
        msg.Should().BeOfType<ActivateCommand>();
    }

    [Fact]
    public void NewTabCommand_Properties_RoundTrip()
    {
        var cmd = new NewTabCommand("hello content", "desktop-123");
        cmd.InitialContent.Should().Be("hello content");
        cmd.DesktopGuid.Should().Be("desktop-123");
    }

    [Fact]
    public void NewTabCommand_DefaultProperties()
    {
        var cmd = new NewTabCommand();
        cmd.InitialContent.Should().BeNull();
        cmd.DesktopGuid.Should().BeNull();
    }

    [Fact]
    public void NewTabCommand_With_ModifiesProperties()
    {
        var cmd = new NewTabCommand("original", "desk-1");
        var modified = cmd with { InitialContent = "modified", DesktopGuid = "desk-2" };
        modified.InitialContent.Should().Be("modified");
        modified.DesktopGuid.Should().Be("desk-2");
    }

    [Fact]
    public void ShowDesktopCommand_Properties_RoundTrip()
    {
        var cmd = new ShowDesktopCommand("desktop-456");
        cmd.DesktopGuid.Should().Be("desktop-456");
    }

    [Fact]
    public void ShowDesktopCommand_With_ModifiesDesktopGuid()
    {
        var cmd = new ShowDesktopCommand("old");
        var modified = cmd with { DesktopGuid = "new" };
        modified.DesktopGuid.Should().Be("new");
    }

    // ─── IPC JSON Serialization ─────────────────────────────────────

    [Fact]
    public void IpcMessage_ActivateCommand_Serializes()
    {
        IpcMessage msg = new ActivateCommand();
        var json = JsonSerializer.Serialize(msg, IpcMessageContext.Default.IpcMessage);
        json.Should().Contain("\"action\":\"activate\"");
    }

    [Fact]
    public void IpcMessage_NewTabCommand_Serializes()
    {
        IpcMessage msg = new NewTabCommand("test content", "desk-1");
        var json = JsonSerializer.Serialize(msg, IpcMessageContext.Default.IpcMessage);
        json.Should().Contain("\"action\":\"new-tab\"");
        json.Should().Contain("test content");
    }

    [Fact]
    public void IpcMessage_ShowDesktopCommand_Serializes()
    {
        IpcMessage msg = new ShowDesktopCommand("desk-guid");
        var json = JsonSerializer.Serialize(msg, IpcMessageContext.Default.IpcMessage);
        json.Should().Contain("\"action\":\"show-desktop\"");
        json.Should().Contain("desk-guid");
    }

    [Fact]
    public void IpcMessage_ActivateCommand_Deserializes()
    {
        var json = """{"action":"activate"}""";
        var msg = JsonSerializer.Deserialize(json, IpcMessageContext.Default.IpcMessage);
        msg.Should().BeOfType<ActivateCommand>();
    }

    [Fact]
    public void IpcMessage_NewTabCommand_Deserializes()
    {
        var json = """{"action":"new-tab","InitialContent":"hello","DesktopGuid":"d1"}""";
        var msg = JsonSerializer.Deserialize(json, IpcMessageContext.Default.IpcMessage);
        msg.Should().BeOfType<NewTabCommand>();
        var cmd = (NewTabCommand)msg!;
        cmd.InitialContent.Should().Be("hello");
        cmd.DesktopGuid.Should().Be("d1");
    }

    [Fact]
    public void IpcMessage_ShowDesktopCommand_Deserializes()
    {
        var json = """{"action":"show-desktop","DesktopGuid":"d2"}""";
        var msg = JsonSerializer.Deserialize(json, IpcMessageContext.Default.IpcMessage);
        msg.Should().BeOfType<ShowDesktopCommand>();
        ((ShowDesktopCommand)msg!).DesktopGuid.Should().Be("d2");
    }

    // ─── AppState ───────────────────────────────────────────────────

    [Fact]
    public void AppState_Properties_RoundTrip()
    {
        var state = new AppState
        {
            DesktopGuid = "guid-1",
            ActiveTabId = 42,
            WindowState = "normal"
        };
        state.DesktopGuid.Should().Be("guid-1");
        state.ActiveTabId.Should().Be(42);
        state.WindowState.Should().Be("normal");
    }

    // ─── Preference ─────────────────────────────────────────────────

    [Fact]
    public void Preference_Properties_RoundTrip()
    {
        var pref = new Preference { Key = "theme", Value = "dark" };
        pref.Key.Should().Be("theme");
        pref.Value.Should().Be("dark");
    }
}
