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

    // ─── Max-wait bound (H3) ───────────────────────────────────────────

    [Fact]
    public void NotifyTextChanged_KeepsResettingTimer_WithinMaxWait()
    {
        var svc = CreateService();
        svc.DebounceMs = 500;
        svc.MaxWaitMs = 3000;
        svc.Configure(() => (1L, "content"), (_, _) => Task.CompletedTask);

        svc.NotifyTextChanged();
        int startsAfterFirst = _timer.StartCount;

        // Keep typing faster than the debounce but still within the max-wait window.
        _clock.Advance(TimeSpan.FromMilliseconds(200));
        svc.NotifyTextChanged();

        _timer.StartCount.Should().BeGreaterThan(startsAfterFirst,
            "within the max-wait window each keystroke resets the debounce timer");
    }

    [Fact]
    public void NotifyTextChanged_StopsResettingTimer_AfterMaxWait()
    {
        var svc = CreateService();
        svc.DebounceMs = 500;
        svc.MaxWaitMs = 3000;
        svc.Configure(() => (1L, "content"), (_, _) => Task.CompletedTask);

        // First keystroke arms the timer and starts the dirty streak.
        svc.NotifyTextChanged();
        int startsAfterFirst = _timer.StartCount;
        _timer.IsEnabled.Should().BeTrue();

        // Sustained sub-debounce typing carries the change past the max-wait cap.
        _clock.Advance(TimeSpan.FromMilliseconds(3100));
        svc.NotifyTextChanged();

        _timer.StartCount.Should().Be(startsAfterFirst,
            "past the max-wait cap the debounce timer must not be reset again");
        _timer.IsEnabled.Should().BeTrue(
            "the pending save timer stays armed so it can finally fire");
    }

    [Fact]
    public void NotifyTextChanged_ResetsDirtyStreak_AfterSuccessfulSave()
    {
        var svc = CreateService();
        svc.DebounceMs = 500;
        svc.MaxWaitMs = 3000;
        svc.Configure(() => (1L, "content"), (_, _) => Task.CompletedTask);

        svc.NotifyTextChanged();
        _clock.Advance(TimeSpan.FromMilliseconds(3100));
        svc.NotifyTextChanged(); // past cap — timer not reset

        // A save fires and clears the dirty streak.
        _timer.Fire();
        svc.IsDirty.Should().BeFalse();

        // New typing starts a fresh streak, so the timer is armed again immediately.
        _clock.Advance(TimeSpan.FromMilliseconds(600)); // past debounce so cooldown doesn't block
        int startsBefore = _timer.StartCount;
        svc.NotifyTextChanged();
        _timer.StartCount.Should().BeGreaterThan(startsBefore,
            "after a save the dirty streak restarts, so the debounce timer is rearmed");
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

    // ─── FlushInBackground ─────────────────────────────────────────────

    [Fact]
    public async Task FlushInBackground_CapturesContentAtCallTime_AndClearsDirty()
    {
        var saved = new List<(long TabId, string Content)>();
        long currentTab = 42;
        string currentContent = "before-switch";
        var service = new AutosaveService(new TestClock(), new TestTimer());
        service.Configure(
            contentProvider: () => (currentTab, currentContent),
            saveFunc: (id, content) => { saved.Add((id, content)); return Task.CompletedTask; });

        service.NotifyTextChanged();
        service.FlushInBackground();

        // Simulate the tab switch happening immediately after — must not affect the capture.
        currentTab = 99;
        currentContent = "after-switch";

        (await BackgroundWriter.DrainAsync(TimeSpan.FromSeconds(2))).Should().BeTrue();
        saved.Should().ContainSingle().Which.Should().Be((42L, "before-switch"));
        service.IsDirty.Should().BeFalse();
    }

    [Fact]
    public void FlushInBackground_SkipsAndStaysDirty_ForUnpersistedTab()
    {
        var service = new AutosaveService(new TestClock(), new TestTimer());
        service.Configure(
            contentProvider: () => (0L, "unsaved"),
            saveFunc: (_, _) => Task.CompletedTask);

        service.NotifyTextChanged();
        service.FlushInBackground();

        service.IsDirty.Should().BeTrue("content of an unpersisted tab must stay flagged for a later flush");
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
