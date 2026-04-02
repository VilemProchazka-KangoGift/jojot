using System.Text.Json;
using JoJot.Models;

namespace JoJot.Tests.Models;

public class IpcMessageTests
{
    // ─── ActivateCommand ──────────────────────────────────────────────

    [Fact]
    public void ActivateCommand_SerializesWithActionDiscriminator()
    {
        IpcMessage msg = new ActivateCommand();
        var json = JsonSerializer.Serialize(msg, IpcMessageContext.Default.IpcMessage);

        json.Should().Contain("\"action\":\"activate\"");
    }

    [Fact]
    public void ActivateCommand_Roundtrips()
    {
        IpcMessage msg = new ActivateCommand();
        var json = JsonSerializer.Serialize(msg, IpcMessageContext.Default.IpcMessage);
        var deserialized = JsonSerializer.Deserialize(json, IpcMessageContext.Default.IpcMessage);

        deserialized.Should().BeOfType<ActivateCommand>();
    }

    // ─── NewTabCommand ────────────────────────────────────────────────

    [Fact]
    public void NewTabCommand_SerializesWithActionDiscriminator()
    {
        IpcMessage msg = new NewTabCommand("hello", "desktop-1");
        var json = JsonSerializer.Serialize(msg, IpcMessageContext.Default.IpcMessage);

        json.Should().Contain("\"action\":\"new-tab\"");
    }

    [Fact]
    public void NewTabCommand_RoundtripsWithContent()
    {
        IpcMessage original = new NewTabCommand("hello world", "desktop-guid");
        var json = JsonSerializer.Serialize(original, IpcMessageContext.Default.IpcMessage);
        var deserialized = JsonSerializer.Deserialize(json, IpcMessageContext.Default.IpcMessage);

        deserialized.Should().BeOfType<NewTabCommand>();
        var cmd = (NewTabCommand)deserialized!;
        cmd.InitialContent.Should().Be("hello world");
        cmd.DesktopGuid.Should().Be("desktop-guid");
    }

    [Fact]
    public void NewTabCommand_RoundtripsWithNulls()
    {
        IpcMessage original = new NewTabCommand();
        var json = JsonSerializer.Serialize(original, IpcMessageContext.Default.IpcMessage);
        var deserialized = JsonSerializer.Deserialize(json, IpcMessageContext.Default.IpcMessage);

        var cmd = (NewTabCommand)deserialized!;
        cmd.InitialContent.Should().BeNull();
        cmd.DesktopGuid.Should().BeNull();
    }

    // ─── ShowDesktopCommand ───────────────────────────────────────────

    [Fact]
    public void ShowDesktopCommand_SerializesWithActionDiscriminator()
    {
        IpcMessage msg = new ShowDesktopCommand("abc-123");
        var json = JsonSerializer.Serialize(msg, IpcMessageContext.Default.IpcMessage);

        json.Should().Contain("\"action\":\"show-desktop\"");
    }

    [Fact]
    public void ShowDesktopCommand_Roundtrips()
    {
        IpcMessage original = new ShowDesktopCommand("desktop-abc");
        var json = JsonSerializer.Serialize(original, IpcMessageContext.Default.IpcMessage);
        var deserialized = JsonSerializer.Deserialize(json, IpcMessageContext.Default.IpcMessage);

        deserialized.Should().BeOfType<ShowDesktopCommand>();
        ((ShowDesktopCommand)deserialized!).DesktopGuid.Should().Be("desktop-abc");
    }

    // ─── Deserialization from raw JSON ────────────────────────────────

    [Fact]
    public void Deserialize_ActivateCommand_FromRawJson()
    {
        var json = """{"action":"activate"}""";
        var msg = JsonSerializer.Deserialize(json, IpcMessageContext.Default.IpcMessage);

        msg.Should().BeOfType<ActivateCommand>();
    }

    [Fact]
    public void Deserialize_NewTabCommand_FromRawJson()
    {
        var json = """{"action":"new-tab","InitialContent":"test","DesktopGuid":"d1"}""";
        var msg = JsonSerializer.Deserialize(json, IpcMessageContext.Default.IpcMessage);

        msg.Should().BeOfType<NewTabCommand>();
        var cmd = (NewTabCommand)msg!;
        cmd.InitialContent.Should().Be("test");
        cmd.DesktopGuid.Should().Be("d1");
    }

    [Fact]
    public void Deserialize_ShowDesktopCommand_FromRawJson()
    {
        var json = """{"action":"show-desktop","DesktopGuid":"xyz"}""";
        var msg = JsonSerializer.Deserialize(json, IpcMessageContext.Default.IpcMessage);

        msg.Should().BeOfType<ShowDesktopCommand>();
        ((ShowDesktopCommand)msg!).DesktopGuid.Should().Be("xyz");
    }

    // ─── Record equality ──────────────────────────────────────────────

    [Fact]
    public void ActivateCommand_ValueEquality()
    {
        var a = new ActivateCommand();
        var b = new ActivateCommand();
        a.Should().Be(b);
    }

    [Fact]
    public void NewTabCommand_ValueEquality()
    {
        var a = new NewTabCommand("content", "guid");
        var b = new NewTabCommand("content", "guid");
        a.Should().Be(b);
    }

    [Fact]
    public void NewTabCommand_ValueInequality()
    {
        var a = new NewTabCommand("content1", "guid");
        var b = new NewTabCommand("content2", "guid");
        a.Should().NotBe(b);
    }

    [Fact]
    public void ShowDesktopCommand_ValueEquality()
    {
        var a = new ShowDesktopCommand("guid-1");
        var b = new ShowDesktopCommand("guid-1");
        a.Should().Be(b);
    }

    // ─── Cross-desktop IPC scenarios ─────────────────────────────────

    [Fact]
    public void NewTabCommand_WithDesktopGuid_RoundtripsGuid()
    {
        // Second instance sends desktop GUID queried from COM
        var guid = "a1b2c3d4-e5f6-7890-abcd-ef1234567890";
        IpcMessage original = new NewTabCommand(DesktopGuid: guid);
        var json = JsonSerializer.Serialize(original, IpcMessageContext.Default.IpcMessage);
        var deserialized = (NewTabCommand)JsonSerializer.Deserialize(json, IpcMessageContext.Default.IpcMessage)!;

        deserialized.DesktopGuid.Should().Be(guid);
    }

    [Fact]
    public void NewTabCommand_WithoutDesktopGuid_DeserializesAsNull()
    {
        // COM query failed — second instance sends null
        IpcMessage original = new NewTabCommand(DesktopGuid: null);
        var json = JsonSerializer.Serialize(original, IpcMessageContext.Default.IpcMessage);
        var deserialized = (NewTabCommand)JsonSerializer.Deserialize(json, IpcMessageContext.Default.IpcMessage)!;

        deserialized.DesktopGuid.Should().BeNull();
    }

    [Fact]
    public void NewTabCommand_DesktopGuidOnlyNoContent_Roundtrips()
    {
        // Common case: second instance sends just the desktop GUID, no content
        IpcMessage original = new NewTabCommand(InitialContent: null, DesktopGuid: "desktop-2");
        var json = JsonSerializer.Serialize(original, IpcMessageContext.Default.IpcMessage);
        var deserialized = (NewTabCommand)JsonSerializer.Deserialize(json, IpcMessageContext.Default.IpcMessage)!;

        deserialized.InitialContent.Should().BeNull();
        deserialized.DesktopGuid.Should().Be("desktop-2");
    }
}
