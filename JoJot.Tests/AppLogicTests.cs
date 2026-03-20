using Serilog.Events;

namespace JoJot.Tests;

/// <summary>
/// Tests for App.xaml.cs extracted pure logic:
/// ParseLogLevel, ShouldRedirect, ShouldRecoverMove.
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

    // ─── ShouldRedirect ──────────────────────────────────────────

    [Fact]
    public void ShouldRedirect_NoOldWindow_HasNewWindow_NotCooledDown_ReturnsTrue()
    {
        var now = DateTime.UtcNow;
        var cooldownExpired = now.AddSeconds(-1);

        App.ShouldRedirect(now, cooldownExpired, hasOldWindow: false, hasNewWindow: true,
            crossDesktopActivation: true, isKeyboardNavigation: false)
            .Should().BeTrue();
    }

    [Fact]
    public void ShouldRedirect_CooldownActive_ReturnsFalse()
    {
        var now = DateTime.UtcNow;
        var cooldownActive = now.AddSeconds(3);

        App.ShouldRedirect(now, cooldownActive, hasOldWindow: false, hasNewWindow: true,
            crossDesktopActivation: true, isKeyboardNavigation: false)
            .Should().BeFalse();
    }

    [Fact]
    public void ShouldRedirect_BothWindowsExist_ReturnsFalse()
    {
        var now = DateTime.UtcNow;
        var cooldownExpired = DateTime.MinValue;

        App.ShouldRedirect(now, cooldownExpired, hasOldWindow: true, hasNewWindow: true,
            crossDesktopActivation: true, isKeyboardNavigation: false)
            .Should().BeFalse();
    }

    [Fact]
    public void ShouldRedirect_NeitherWindowExists_ReturnsFalse()
    {
        var now = DateTime.UtcNow;
        var cooldownExpired = DateTime.MinValue;

        App.ShouldRedirect(now, cooldownExpired, hasOldWindow: false, hasNewWindow: false,
            crossDesktopActivation: true, isKeyboardNavigation: false)
            .Should().BeFalse();
    }

    [Fact]
    public void ShouldRedirect_HasOldWindow_NoNewWindow_ReturnsFalse()
    {
        var now = DateTime.UtcNow;
        var cooldownExpired = DateTime.MinValue;

        App.ShouldRedirect(now, cooldownExpired, hasOldWindow: true, hasNewWindow: false,
            crossDesktopActivation: true, isKeyboardNavigation: false)
            .Should().BeFalse();
    }

    [Fact]
    public void ShouldRedirect_NoCrossDesktopActivation_ReturnsFalse()
    {
        var now = DateTime.UtcNow;
        var cooldownExpired = DateTime.MinValue;

        App.ShouldRedirect(now, cooldownExpired, hasOldWindow: false, hasNewWindow: true,
            crossDesktopActivation: false, isKeyboardNavigation: false)
            .Should().BeFalse();
    }

    [Fact]
    public void ShouldRedirect_KeyboardNavigation_ReturnsFalse()
    {
        var now = DateTime.UtcNow;
        var cooldownExpired = DateTime.MinValue;

        App.ShouldRedirect(now, cooldownExpired, hasOldWindow: false, hasNewWindow: true,
            crossDesktopActivation: true, isKeyboardNavigation: true)
            .Should().BeFalse();
    }

    [Fact]
    public void ShouldRedirect_BothCrossDesktopAndKeyboard_ReturnsFalse()
    {
        var now = DateTime.UtcNow;
        var cooldownExpired = DateTime.MinValue;

        App.ShouldRedirect(now, cooldownExpired, hasOldWindow: false, hasNewWindow: true,
            crossDesktopActivation: true, isKeyboardNavigation: true)
            .Should().BeFalse();
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
}
