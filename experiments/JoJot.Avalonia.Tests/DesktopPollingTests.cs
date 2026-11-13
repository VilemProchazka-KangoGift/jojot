using JoJot.AvaloniaExperiment.Platform;
using Xunit;

namespace JoJot.AvaloniaExperiment.Tests;

public sealed class DesktopPollingTests
{
    [Fact]
    public void NotificationsReduceIdleEnumerationsAndExplorerRestartForcesRecovery()
    {
        var polling = new DesktopPolling();
        polling.Start(TimeSpan.Zero, 10);
        var enumerations = 0;
        for (var seconds = 2; seconds <= 30; seconds += 2)
            if (polling.ShouldEnumerate(TimeSpan.FromSeconds(seconds), 10, true, out _)) enumerations++;
        Assert.Equal(1, enumerations); // Instead of fifteen full enumerations in thirty seconds.
        Assert.False(polling.ShouldEnumerate(TimeSpan.FromSeconds(32), 0, true, out _));
        Assert.True(polling.ShouldEnumerate(TimeSpan.FromSeconds(34), 11, true, out var changed));
        Assert.True(changed);
        Assert.True(polling.ShouldEnumerate(TimeSpan.FromSeconds(36), 11, false, out changed));
        Assert.False(changed);
    }
}
