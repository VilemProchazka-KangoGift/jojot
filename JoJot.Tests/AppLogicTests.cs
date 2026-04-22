using Serilog.Events;

namespace JoJot.Tests;

/// <summary>
/// Tests for App.xaml.cs extracted pure logic:
/// ParseLogLevel, ShouldRecoverMove.
/// </summary>
public class AppLogicTests
{
    // ─── ParseLogLevel ───────────────────────────────────────────

    [Fact]
    public void ParseLogLevel_ValidLevel_Debug()
    {
        App.ParseLogLevel("Debug").Should().Be(LogEventLevel.Debug);
    }

    [Fact]
    public void ParseLogLevel_ValidLevel_Warning()
    {
        App.ParseLogLevel("Warning").Should().Be(LogEventLevel.Warning);
    }

    [Fact]
    public void ParseLogLevel_CaseInsensitive()
    {
        App.ParseLogLevel("debug").Should().Be(LogEventLevel.Debug);
        App.ParseLogLevel("DEBUG").Should().Be(LogEventLevel.Debug);
    }

    [Fact]
    public void ParseLogLevel_Null_ReturnsNull()
    {
        App.ParseLogLevel(null).Should().BeNull();
    }

    [Fact]
    public void ParseLogLevel_Invalid_ReturnsNull()
    {
        App.ParseLogLevel("notavalidlevel").Should().BeNull();
    }

    [Fact]
    public void ParseLogLevel_Empty_ReturnsNull()
    {
        App.ParseLogLevel("").Should().BeNull();
    }

    [Fact]
    public void ParseLogLevel_AllValidLevels()
    {
        App.ParseLogLevel("Verbose").Should().Be(LogEventLevel.Verbose);
        App.ParseLogLevel("Information").Should().Be(LogEventLevel.Information);
        App.ParseLogLevel("Error").Should().Be(LogEventLevel.Error);
        App.ParseLogLevel("Fatal").Should().Be(LogEventLevel.Fatal);
    }

    // ─── ShouldRecoverMove ───────────────────────────────────────

    [Fact]
    public void ShouldRecoverMove_ValidMove_ReturnsTrue()
    {
        App.ShouldRecoverMove("desktop-B", "desktop-A").Should().BeTrue();
    }

    [Fact]
    public void ShouldRecoverMove_NullToDesktop_ReturnsFalse()
    {
        App.ShouldRecoverMove(null, "desktop-A").Should().BeFalse();
    }

    [Fact]
    public void ShouldRecoverMove_SameDesktop_ReturnsFalse()
    {
        App.ShouldRecoverMove("desktop-A", "desktop-A").Should().BeFalse();
    }

    [Fact]
    public void ShouldRecoverMove_SameDesktop_CaseInsensitive_ReturnsFalse()
    {
        App.ShouldRecoverMove("Desktop-A", "desktop-a").Should().BeFalse();
    }

    [Fact]
    public void ShouldRecoverMove_DifferentGuids_CaseInsensitive_ReturnsTrue()
    {
        App.ShouldRecoverMove("GUID-B", "guid-a").Should().BeTrue();
    }

    // ─── ResolveTargetDesktop ───────────────────────────────────────

    [Fact]
    public void ResolveTargetDesktop_SenderGuid_TakesPriorityOverCached()
    {
        // Second instance queried COM and found Desktop 2 — use that,
        // not the first instance's cached Desktop 1
        App.ResolveTargetDesktop("desktop-2", "desktop-1").Should().Be("desktop-2");
    }

    [Fact]
    public void ResolveTargetDesktop_NullSenderGuid_FallsToCached()
    {
        // COM query failed in second instance — fall back to first instance's cached GUID
        App.ResolveTargetDesktop(null, "desktop-1").Should().Be("desktop-1");
    }

    [Fact]
    public void ResolveTargetDesktop_SenderGuid_SameAsCached_ReturnsSender()
    {
        // Both agree on the same desktop — no conflict
        App.ResolveTargetDesktop("desktop-1", "desktop-1").Should().Be("desktop-1");
    }

    [Fact]
    public void ResolveTargetDesktop_SenderGuid_PreservedExactly()
    {
        // Verify the sender's GUID is returned verbatim (e.g., casing preserved)
        var guid = "A1B2C3D4-E5F6-7890-ABCD-EF1234567890";
        App.ResolveTargetDesktop(guid, "cached-guid").Should().Be(guid);
    }
}
