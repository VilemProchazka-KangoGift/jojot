using JoJot.Services;
using JoJot.Tests.Helpers;

namespace JoJot.Tests.Services;

/// <summary>
/// Additional AutosaveService tests targeting uncovered branches:
/// DebounceMs getter, OnTimerTick edge cases (not dirty, tabId zero, no configure).
/// </summary>
public class AutosaveServiceCoverageTests
{
    private readonly TestClock _clock = new();
    private readonly TestTimer _timer = new();

    private AutosaveService CreateService() => new(_clock, _timer);

    [Fact]
    public void DebounceMs_Getter_ReturnsDefault()
    {
        var svc = CreateService();
        svc.DebounceMs.Should().Be(500);
    }

    [Fact]
    public void DebounceMs_Getter_ReturnsSetValue()
    {
        var svc = CreateService();
        svc.DebounceMs = 1000;
        svc.DebounceMs.Should().Be(1000);
    }

    [Fact]
    public void TimerTick_SkipsWhenNotDirty()
    {
        var svc = CreateService();
        bool saveCalled = false;

        svc.Configure(
            () => (1L, "content"),
            (_, _) => { saveCalled = true; return Task.CompletedTask; });

        // Fire without NotifyTextChanged — not dirty
        _timer.Fire();

        saveCalled.Should().BeFalse();
    }

    [Fact]
    public void TimerTick_SkipsWhenTabIdIsZero()
    {
        var svc = CreateService();
        bool saveCalled = false;

        svc.Configure(
            () => (0L, "content"),
            (_, _) => { saveCalled = true; return Task.CompletedTask; });

        svc.NotifyTextChanged();
        _timer.Fire();

        saveCalled.Should().BeFalse();
    }

    [Fact]
    public void TimerTick_SkipsWhenNotConfigured()
    {
        var svc = CreateService();
        // Don't call Configure

        svc.NotifyTextChanged();
        // Fire should not throw even with no configuration
        _timer.Fire();

        svc.IsDirty.Should().BeTrue("dirty flag set but save not possible without configuration");
    }

    [Fact]
    public void FlushAsync_SkipsWhenNotConfigured()
    {
        var svc = CreateService();
        // Don't call Configure

        svc.NotifyTextChanged();

        // Should not throw
        var task = svc.FlushAsync();
        task.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public void TimerTick_InvokesSnapshotCallback()
    {
        var svc = CreateService();
        long snapshotTabId = 0;
        string snapshotContent = "";

        svc.Configure(
            () => (5L, "tick snapshot"),
            (_, _) => Task.CompletedTask,
            onSnapshot: (id, content) => { snapshotTabId = id; snapshotContent = content; });

        svc.NotifyTextChanged();
        _timer.Fire();

        snapshotTabId.Should().Be(5);
        snapshotContent.Should().Be("tick snapshot");
    }

    [Fact]
    public void TimerTick_InvokesOnSaveCompleted()
    {
        var svc = CreateService();
        long completedTabId = 0;

        svc.Configure(
            () => (7L, "content"),
            (_, _) => Task.CompletedTask,
            onSaveCompleted: id => completedTabId = id);

        svc.NotifyTextChanged();
        _timer.Fire();

        completedTabId.Should().Be(7);
    }

    [Fact]
    public void Constructor_DefaultParameters_DoesNotThrow()
    {
        // Tests the default clock/timer path
        var svc = new AutosaveService();
        svc.DebounceMs.Should().Be(500);
    }

    [Fact]
    public async Task FlushAsync_SkipsWhenTabIdNegative()
    {
        var svc = CreateService();
        bool saveCalled = false;

        svc.Configure(
            () => (-1L, "content"),
            (_, _) => { saveCalled = true; return Task.CompletedTask; });

        svc.NotifyTextChanged();
        await svc.FlushAsync();
        saveCalled.Should().BeFalse();
    }
}
