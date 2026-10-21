using JoJot.Models;
using JoJot.Services;

namespace JoJot.Tests.Services;

/// <summary>
/// Zombie-kill safeguard: a cold-starting primary must never be killed by a second instance.
/// </summary>
public class IpcServiceZombieDecisionTests
{
    private static readonly DateTime Now = new(2026, 10, 21, 12, 0, 0, DateTimeKind.Local);

    [Fact]
    public void ShouldKillCandidate_PrimaryStillStarting_IsNeverKilled()
    {
        // Primary launched 3 s ago (mid cold start, ~2.4 s measured); connect failing for 6 s.
        IpcService.ShouldKillCandidate(Now.AddSeconds(-3), Now, TimeSpan.FromSeconds(6))
            .Should().BeFalse();
    }

    [Fact]
    public void ShouldKillCandidate_PrimaryJustInsideGrace_IsNotKilled()
    {
        IpcService.ShouldKillCandidate(Now.AddSeconds(-9.9), Now, TimeSpan.FromSeconds(15))
            .Should().BeFalse();
    }

    [Fact]
    public void ShouldKillCandidate_OldPrimaryUnresponsivePastBudget_IsKilled()
    {
        IpcService.ShouldKillCandidate(Now.AddMinutes(-30), Now, TimeSpan.FromSeconds(6))
            .Should().BeTrue();
    }

    [Fact]
    public void ShouldKillCandidate_OldPrimaryButShortWait_IsNotKilled()
    {
        // The old ~1.8 s budget would have killed here.
        IpcService.ShouldKillCandidate(Now.AddMinutes(-30), Now, TimeSpan.FromSeconds(1.8))
            .Should().BeFalse();
    }

    [Fact]
    public void ShouldKillCandidate_ExactBoundaries_AreKill()
    {
        IpcService.ShouldKillCandidate(Now - IpcService.StartupGrace, Now, IpcService.MinZombieWait)
            .Should().BeTrue();
    }

    [Fact]
    public void ShouldKillCandidate_UnknownStartTime_FallsBackToWaitGuard()
    {
        IpcService.ShouldKillCandidate(DateTime.MinValue, Now, TimeSpan.FromSeconds(1))
            .Should().BeFalse();
        IpcService.ShouldKillCandidate(DateTime.MinValue, Now, TimeSpan.FromSeconds(5))
            .Should().BeTrue();
    }

    [Fact]
    public void Budget_IsAtLeastFiveSecondsAndTenSecondGrace()
    {
        IpcService.MinZombieWait.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(5));
        IpcService.StartupGrace.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(10));
        // The ceiling must leave room for a primary to age out of its grace and be judged.
        IpcService.MaxTotalWait.Should().BeGreaterThan(IpcService.StartupGrace);
        IpcService.ConnectAttemptMs.Should().BeLessThan((int)IpcService.MinZombieWait.TotalMilliseconds);
    }
}

/// <summary>
/// Commands that reach the primary before their window exists are queued and replayed in order.
/// </summary>
public class IpcCommandQueueTests
{
    [Fact]
    public void TryDefer_BeforeStartupWithoutWindow_Defers()
    {
        var queue = new IpcCommandQueue();

        queue.TryDefer(new NewTabCommand(DesktopGuid: "d1"), targetWindowExists: false).Should().BeTrue();

        queue.Count.Should().Be(1);
        queue.IsReady.Should().BeFalse();
    }

    [Fact]
    public void TryDefer_BeforeStartupWithWindow_DoesNotDefer()
    {
        // The window exists (shell shown before the DB is ready): forward directly — the window
        // itself queues the new tab until its tabs have loaded.
        var queue = new IpcCommandQueue();

        queue.TryDefer(new NewTabCommand(DesktopGuid: "d1"), targetWindowExists: true).Should().BeFalse();

        queue.Count.Should().Be(0);
    }

    [Fact]
    public void MarkReady_ReturnsQueuedCommandsInArrivalOrder_AndEmptiesQueue()
    {
        var queue = new IpcCommandQueue();
        var first = new NewTabCommand(DesktopGuid: "d1");
        var second = new ActivateCommand();
        var third = new NewTabCommand(DesktopGuid: "d2");
        queue.TryDefer(first, false);
        queue.TryDefer(second, false);
        queue.TryDefer(third, false);

        var replay = queue.MarkReady();

        replay.Should().Equal(first, second, third);
        queue.Count.Should().Be(0);
        queue.IsReady.Should().BeTrue();
    }

    [Fact]
    public void AfterMarkReady_NothingIsDeferred_EvenWithoutWindow()
    {
        var queue = new IpcCommandQueue();
        queue.MarkReady();

        queue.TryDefer(new NewTabCommand(DesktopGuid: "d1"), targetWindowExists: false).Should().BeFalse();

        queue.Count.Should().Be(0);
    }

    [Fact]
    public void MarkReady_SecondCall_ReturnsEmpty()
    {
        var queue = new IpcCommandQueue();
        queue.TryDefer(new ActivateCommand(), false);

        queue.MarkReady().Should().HaveCount(1);
        queue.MarkReady().Should().BeEmpty();
    }

    [Fact]
    public void MarkReady_WithNothingQueued_ReturnsEmpty()
    {
        new IpcCommandQueue().MarkReady().Should().BeEmpty();
    }
}
