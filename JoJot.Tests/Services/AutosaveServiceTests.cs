using JoJot.Services;
using JoJot.Tests.Helpers;

namespace JoJot.Tests.Services;

public class AutosaveServiceTests
{
    private readonly TestClock _clock = new();
    private readonly TestTimer _timer = new();

    private AutosaveService CreateService() => new(_clock, _timer);

    // ─── NotifyTextChanged ─────────────────────────────────────────────

    [Fact]
    public void NotifyTextChanged_StartsTimer()
    {
        var svc = CreateService();
        svc.Configure(
            () => (1L, "content"),
            (_, _) => Task.CompletedTask);

        svc.NotifyTextChanged();
        _timer.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void NotifyTextChanged_SetsDirtyFlag()
    {
        var svc = CreateService();
        svc.Configure(
            () => (1L, "content"),
            (_, _) => Task.CompletedTask);

        svc.NotifyTextChanged();
        svc.IsDirty.Should().BeTrue();
    }

    [Fact]
    public void NotifyTextChanged_DoesNotResetTimer_WhenInCooldown()
    {
        var svc = CreateService();
        svc.DebounceMs = 500;

        svc.Configure(
            () => (1L, "content"),
            (_, _) => Task.CompletedTask);

        // First notify starts the timer
        svc.NotifyTextChanged();
        _timer.IsEnabled.Should().BeTrue();

        // Simulate a save completing (sets _lastWriteCompleted)
        _timer.Fire(); // triggers save, which sets _lastWriteCompleted to clock.Now

        // Advance only 200ms (within cooldown)
        _clock.Advance(TimeSpan.FromMilliseconds(200));

        // Mark dirty again and start timer
        svc.NotifyTextChanged();
        // Timer should be started (it's a new notification)
        _timer.IsEnabled.Should().BeTrue();

        // Now second notify during cooldown with timer already running should not reset
        svc.NotifyTextChanged();
        // Timer is still enabled (not reset, just kept)
        _timer.IsEnabled.Should().BeTrue();
    }

    // ─── FlushAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task FlushAsync_SavesContent_WhenDirty()
    {
        var svc = CreateService();
        long savedTabId = 0;
        string savedContent = "";

        svc.Configure(
            () => (42L, "test content"),
            (id, content) => { savedTabId = id; savedContent = content; return Task.CompletedTask; });

        svc.NotifyTextChanged();
        await svc.FlushAsync();

        savedTabId.Should().Be(42);
        savedContent.Should().Be("test content");
    }

    [Fact]
    public async Task FlushAsync_StopsTimer()
    {
        var svc = CreateService();
        svc.Configure(
            () => (1L, "content"),
            (_, _) => Task.CompletedTask);

        svc.NotifyTextChanged();
        _timer.IsEnabled.Should().BeTrue();

        await svc.FlushAsync();
        _timer.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task FlushAsync_ClearsDirtyFlag()
    {
        var svc = CreateService();
        svc.Configure(
            () => (1L, "content"),
            (_, _) => Task.CompletedTask);

        svc.NotifyTextChanged();
        svc.IsDirty.Should().BeTrue();

        await svc.FlushAsync();
        svc.IsDirty.Should().BeFalse();
    }

    [Fact]
    public async Task FlushAsync_SkipsWhenNotDirty()
    {
        var svc = CreateService();
        bool saveCalled = false;

        svc.Configure(
            () => (1L, "content"),
            (_, _) => { saveCalled = true; return Task.CompletedTask; });

        await svc.FlushAsync();
        saveCalled.Should().BeFalse();
    }

    [Fact]
    public async Task FlushAsync_SkipsWhenTabIdIsZero()
    {
        var svc = CreateService();
        bool saveCalled = false;

        svc.Configure(
            () => (0L, "content"),
            (_, _) => { saveCalled = true; return Task.CompletedTask; });

        svc.NotifyTextChanged();
        await svc.FlushAsync();
        saveCalled.Should().BeFalse();
    }

    [Fact]
    public async Task FlushAsync_InvokesSnapshotCallback()
    {
        var svc = CreateService();
        long snapshotTabId = 0;
        string snapshotContent = "";

        svc.Configure(
            () => (5L, "snapshot test"),
            (_, _) => Task.CompletedTask,
            onSnapshot: (id, content) => { snapshotTabId = id; snapshotContent = content; });

        svc.NotifyTextChanged();
        await svc.FlushAsync();

        snapshotTabId.Should().Be(5);
        snapshotContent.Should().Be("snapshot test");
    }

    [Fact]
    public async Task FlushAsync_InvokesOnSaveCompleted()
    {
        var svc = CreateService();
        long completedTabId = 0;

        svc.Configure(
            () => (7L, "content"),
            (_, _) => Task.CompletedTask,
            onSaveCompleted: id => completedTabId = id);

        svc.NotifyTextChanged();
        await svc.FlushAsync();

        completedTabId.Should().Be(7);
    }

    // ─── Stop ──────────────────────────────────────────────────────────

    [Fact]
    public void Stop_StopsTimer()
    {
        var svc = CreateService();
        svc.Configure(
            () => (1L, "content"),
            (_, _) => Task.CompletedTask);

        svc.NotifyTextChanged();
        _timer.IsEnabled.Should().BeTrue();

        svc.Stop();
        _timer.IsEnabled.Should().BeFalse();
    }

    // ─── Timer Tick ────────────────────────────────────────────────────

    [Fact]
    public void TimerTick_SavesAndClearsDirty()
    {
        var svc = CreateService();
        long savedTabId = 0;

        svc.Configure(
            () => (3L, "tick content"),
            (id, _) => { savedTabId = id; return Task.CompletedTask; });

        svc.NotifyTextChanged();
        _timer.Fire(); // simulate debounce elapsed

        savedTabId.Should().Be(3);
        svc.IsDirty.Should().BeFalse();
        _timer.IsEnabled.Should().BeFalse();
    }

    // ─── DebounceMs ────────────────────────────────────────────────────

    [Fact]
    public void DebounceMs_UpdatesTimerInterval()
    {
        var svc = CreateService();
        svc.DebounceMs = 1000;

        _timer.Interval.Should().Be(TimeSpan.FromMilliseconds(1000));
    }

    // ─── Save Failure Resilience ─────────────────────────────────────

    [Fact]
    public void TimerTick_KeepsDirtyFlag_WhenSaveFails()
    {
        var svc = CreateService();

        svc.Configure(
            () => (1L, "content"),
            (_, _) => throw new InvalidOperationException("DB locked"));

        svc.NotifyTextChanged();
        _timer.Fire(); // triggers save which throws

        svc.IsDirty.Should().BeTrue("save failed, so content should remain dirty for retry");
    }

    [Fact]
    public void TimerTick_RetriesOnNextTick_AfterFailure()
    {
        var svc = CreateService();
        int attempt = 0;
        long savedTabId = 0;

        svc.Configure(
            () => (1L, "content"),
            (id, _) =>
            {
                attempt++;
                if (attempt == 1)
                    throw new InvalidOperationException("DB locked");
                savedTabId = id;
                return Task.CompletedTask;
            });

        svc.NotifyTextChanged();
        _timer.Fire(); // first attempt fails
        svc.IsDirty.Should().BeTrue();

        // Simulate user typing again which starts the timer
        svc.NotifyTextChanged();
        _timer.Fire(); // second attempt succeeds

        savedTabId.Should().Be(1);
        svc.IsDirty.Should().BeFalse();
    }
}
