using JoJot.Services;

namespace JoJot.Tests.Services;

/// <summary>
/// Tests for DesktopSwitchDetector pure timing functions.
/// These test the time-window logic without requiring actual keyboard hooks or WndProc.
/// </summary>
public class DesktopSwitchDetectorTests
{
    private const long Frequency = 10_000_000; // 10 MHz (typical Stopwatch frequency)

    // ─── IsNavigationRecent ─────────────────────────────────────────

    [Fact]
    public void IsNavigationRecent_WithinWindow_ReturnsTrue()
    {
        long navTicks = 1000;
        long nowTicks = navTicks + Frequency; // 1 second later (within 5s window)

        DesktopSwitchDetector.IsNavigationRecent(navTicks, nowTicks, Frequency)
            .Should().BeTrue();
    }

    [Fact]
    public void IsNavigationRecent_AtBoundary_ReturnsTrue()
    {
        long navTicks = 1000;
        long nowTicks = navTicks + (Frequency * 5) - 1; // Just under 5 seconds

        DesktopSwitchDetector.IsNavigationRecent(navTicks, nowTicks, Frequency)
            .Should().BeTrue();
    }

    [Fact]
    public void IsNavigationRecent_OutsideWindow_ReturnsFalse()
    {
        long navTicks = 1000;
        long nowTicks = navTicks + Frequency * 6; // 6 seconds later

        DesktopSwitchDetector.IsNavigationRecent(navTicks, nowTicks, Frequency)
            .Should().BeFalse();
    }

    [Fact]
    public void IsNavigationRecent_ExactlyAtExpiry_ReturnsFalse()
    {
        long navTicks = 1000;
        long nowTicks = navTicks + Frequency * 5; // Exactly 5 seconds

        DesktopSwitchDetector.IsNavigationRecent(navTicks, nowTicks, Frequency)
            .Should().BeFalse();
    }

    [Fact]
    public void IsNavigationRecent_NeverNavigated_ReturnsFalse()
    {
        DesktopSwitchDetector.IsNavigationRecent(0, Frequency * 5, Frequency)
            .Should().BeFalse();
    }

    [Fact]
    public void IsNavigationRecent_JustHappened_ReturnsTrue()
    {
        long ticks = 50000;

        DesktopSwitchDetector.IsNavigationRecent(ticks, ticks, Frequency)
            .Should().BeTrue();
    }

    // ─── IsActivationBeforeSwitch ───────────────────────────────────

    [Fact]
    public void IsActivationBeforeSwitch_ActivationFirst_ReturnsTrue()
    {
        long activationTicks = 1000;
        long switchTicks = activationTicks + Frequency / 10; // 100ms later

        DesktopSwitchDetector.IsActivationBeforeSwitch(activationTicks, switchTicks, Frequency)
            .Should().BeTrue();
    }

    [Fact]
    public void IsActivationBeforeSwitch_SwitchFirst_ReturnsFalse()
    {
        long switchTicks = 1000;
        long activationTicks = switchTicks + Frequency / 10; // 100ms later

        DesktopSwitchDetector.IsActivationBeforeSwitch(activationTicks, switchTicks, Frequency)
            .Should().BeFalse();
    }

    [Fact]
    public void IsActivationBeforeSwitch_Simultaneous_ReturnsFalse()
    {
        long ticks = 50000;

        DesktopSwitchDetector.IsActivationBeforeSwitch(ticks, ticks, Frequency)
            .Should().BeFalse();
    }

    [Fact]
    public void IsActivationBeforeSwitch_TooFarApart_ReturnsFalse()
    {
        long activationTicks = 1000;
        long switchTicks = activationTicks + Frequency * 3; // 3 seconds later (> 2s window)

        DesktopSwitchDetector.IsActivationBeforeSwitch(activationTicks, switchTicks, Frequency)
            .Should().BeFalse();
    }

    [Fact]
    public void IsActivationBeforeSwitch_ExactlyAtExpiry_ReturnsFalse()
    {
        long activationTicks = 1000;
        long switchTicks = activationTicks + Frequency * 2; // Exactly 2 seconds

        DesktopSwitchDetector.IsActivationBeforeSwitch(activationTicks, switchTicks, Frequency)
            .Should().BeFalse();
    }

    [Fact]
    public void IsActivationBeforeSwitch_NeverActivated_ReturnsFalse()
    {
        DesktopSwitchDetector.IsActivationBeforeSwitch(0, 50000, Frequency)
            .Should().BeFalse();
    }

    [Fact]
    public void IsActivationBeforeSwitch_NeverSwitched_ReturnsFalse()
    {
        DesktopSwitchDetector.IsActivationBeforeSwitch(50000, 0, Frequency)
            .Should().BeFalse();
    }

    [Fact]
    public void IsActivationBeforeSwitch_JustBefore_ReturnsTrue()
    {
        long activationTicks = 50000;
        long switchTicks = activationTicks + 1; // 1 tick later

        DesktopSwitchDetector.IsActivationBeforeSwitch(activationTicks, switchTicks, Frequency)
            .Should().BeTrue();
    }
}
