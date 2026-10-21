namespace JoJot.Services;

/// <summary>
/// Per-window autosave service with reset-on-keystroke debounce.
/// Uses <see cref="IDebounceTimer"/> for timer management and accepts save/snapshot
/// delegates for decoupled operation.
/// The timer resets on every keystroke; a save fires after <see cref="DebounceMs"/> of
/// inactivity. A write frequency cap prevents scheduling a new write sooner than
/// <see cref="DebounceMs"/> after the previous write completed.
/// </summary>
public sealed class AutosaveService
{
    private readonly IDebounceTimer _timer;
    private readonly IClock _clock;
    private int _debounceMs = 500;
    private TimeSpan _debounceInterval = TimeSpan.FromMilliseconds(500);
    private int _maxWaitMs = 3000;
    // Ticks of the last completed write. Written from DB-thread continuations (FlushAsync /
    // FlushInBackground) and read on the UI thread, so accessed via Volatile.
    private long _lastWriteCompletedTicks;
    private DateTime _firstDirtyAt = DateTime.MinValue;
    // Cleared/restored by save continuations that may run off the UI thread.
    private volatile bool _isDirty;
    private Func<(long TabId, string Content)>? _contentProvider;
    private Func<long, string, Task>? _saveFunc;
    private Action<long, string>? _onSnapshot;
    private Action<long>? _onSaveCompleted;

    /// <summary>
    /// Initializes a new instance of <see cref="AutosaveService"/>.
    /// </summary>
    /// <param name="clock">Clock abstraction for testability.</param>
    /// <param name="timer">Timer abstraction for testability.</param>
    public AutosaveService(IClock? clock = null, IDebounceTimer? timer = null)
    {
        _clock = clock ?? SystemClock.Instance;
        _timer = timer ?? new DispatcherDebounceTimer();
        LastWriteCompleted = DateTime.MinValue;

        _timer.Interval = TimeSpan.FromMilliseconds(_debounceMs);
        _timer.Tick += OnTimerTick;
    }

    /// <summary>
    /// Debounce interval in milliseconds. Default is 500 ms.
    /// </summary>
    public int DebounceMs
    {
        get => _debounceMs;
        set
        {
            _debounceMs = value;
            _debounceInterval = TimeSpan.FromMilliseconds(value);
            _timer.Interval = _debounceInterval;
        }
    }

    /// <summary>
    /// Maximum time (ms) a change may stay unsaved while the user keeps typing faster than the
    /// debounce interval. Once exceeded, <see cref="NotifyTextChanged"/> stops resetting the timer
    /// so the pending save fires, bounding the worst-case amount of unsaved work. Default 3000 ms.
    /// </summary>
    public int MaxWaitMs
    {
        get => _maxWaitMs;
        set => _maxWaitMs = value;
    }

    private DateTime LastWriteCompleted
    {
        get => new(Volatile.Read(ref _lastWriteCompletedTicks));
        set => Volatile.Write(ref _lastWriteCompletedTicks, value.Ticks);
    }

    /// <summary>Whether there are unsaved changes pending.</summary>
    internal bool IsDirty => _isDirty;

    /// <summary>
    /// Clears the dirty flag after the caller has persisted the current content by other means
    /// (e.g. a synchronous save on the close/shutdown path), so a subsequent flush won't re-write.
    /// </summary>
    internal void MarkClean() => _isDirty = false;

    /// <summary>
    /// Configures the service with a content provider, save function, and callbacks.
    /// </summary>
    /// <param name="contentProvider">Returns <c>(TabId, Content)</c> for the currently active tab.</param>
    /// <param name="saveFunc">Async function to persist content (tabId, content).</param>
    /// <param name="onSnapshot">Optional callback after save for undo snapshots (tabId, content).</param>
    /// <param name="onSaveCompleted">Invoked after a successful save with the tab ID.</param>
    public void Configure(
        Func<(long TabId, string Content)> contentProvider,
        Func<long, string, Task> saveFunc,
        Action<long, string>? onSnapshot = null,
        Action<long>? onSaveCompleted = null)
    {
        _contentProvider = contentProvider;
        _saveFunc = saveFunc;
        _onSnapshot = onSnapshot;
        _onSaveCompleted = onSaveCompleted;
    }

    /// <summary>
    /// Called on every user-initiated <c>TextChanged</c> event. Resets the debounce timer
    /// unless within the write-frequency cooldown and a timer is already scheduled.
    /// </summary>
    public void NotifyTextChanged()
    {
        var now = _clock.Now;

        // Record when this dirty streak began so we can bound its worst-case latency.
        if (!_isDirty)
            _firstDirtyAt = now;

        var elapsed = now - LastWriteCompleted;
        if (elapsed.TotalMilliseconds < _debounceMs && _timer.IsEnabled)
        {
            // Within cooldown and timer already scheduled; don't reset
            return;
        }

        _isDirty = true;

        // Max-latency bound: under sustained sub-debounce typing the timer would be reset on every
        // keystroke and never elapse, so nothing is ever written until the user pauses. Once the
        // change has been pending past MaxWaitMs, leave the armed timer alone so it fires and the
        // save actually happens — capping the amount of work at risk from a crash/power loss.
        if (_timer.IsEnabled && (now - _firstDirtyAt).TotalMilliseconds >= _maxWaitMs)
        {
            return;
        }

        _timer.Stop();
        _timer.Interval = _debounceInterval;
        _timer.Start();
    }

    /// <summary>
    /// Flushes any pending content to the database immediately.
    /// Called on tab switch and application close.
    /// </summary>
    public async Task FlushAsync()
    {
        _timer.Stop();

        if (!_isDirty || _contentProvider is null || _saveFunc is null)
        {
            return;
        }

        var (tabId, content) = _contentProvider();
        if (tabId <= 0)
        {
            return;
        }

        // Clear BEFORE the save: the write now runs on the DB thread, so the user can keep typing
        // while it is in flight. Clearing afterwards would erase the dirty flag of those new edits.
        _isDirty = false;
        try
        {
            await _saveFunc(tabId, content).ConfigureAwait(false);
        }
        catch
        {
            _isDirty = true; // save failed — keep the content flagged for a retry
            throw;
        }
        LastWriteCompleted = _clock.Now;

        // Runs off the UI thread (ConfigureAwait(false)) — callback owners marshal as needed.
        _onSnapshot?.Invoke(tabId, content);
        _onSaveCompleted?.Invoke(tabId);
    }

    /// <summary>
    /// Captures the current tab's content synchronously and persists it in the background,
    /// so tab switches never wait on the database. The capture happens before this method
    /// returns — the caller may immediately change the active tab. An unpersisted tab
    /// (id &lt;= 0) is skipped and stays dirty; its content lives in the model until it has a row.
    /// </summary>
    public void FlushInBackground()
    {
        _timer.Stop();

        if (!_isDirty || _contentProvider is null || _saveFunc is null)
        {
            return;
        }

        var (tabId, content) = _contentProvider();
        if (tabId <= 0)
        {
            return; // stays dirty — flushed after the row exists
        }

        _isDirty = false;
        var save = _saveFunc;
        var snapshot = _onSnapshot;
        var completed = _onSaveCompleted;

        BackgroundWriter.Run(async () =>
        {
            await save(tabId, content).ConfigureAwait(false);
            LastWriteCompleted = _clock.Now; // benign race: only feeds the write-frequency cap
            // Runs off the UI thread — callback owners marshal as needed.
            snapshot?.Invoke(tabId, content);
            completed?.Invoke(tabId);
        }, "autosave flush");
    }

    /// <summary>
    /// Stops the autosave timer. Called on tab switch before the context changes.
    /// </summary>
    public void Stop()
    {
        _timer.Stop();
    }

    /// <summary>
    /// Timer tick handler. Fires after the debounce interval of inactivity, saves
    /// content via the save function, and pushes a snapshot.
    /// </summary>
    private async void OnTimerTick(object? sender, EventArgs e)
    {
        _timer.Stop(); // Single-shot: don't fire again until next NotifyTextChanged

        if (!_isDirty || _contentProvider is null || _saveFunc is null)
        {
            return;
        }

        var (tabId, content) = _contentProvider();
        if (tabId <= 0)
        {
            return;
        }

        // Clear BEFORE the save: the write runs on the DB thread and the UI stays responsive, so
        // keystrokes can land while it is in flight. Clearing afterwards would erase their dirty
        // flag and the re-armed timer would then find nothing to save.
        _isDirty = false;
        try
        {
            // No ConfigureAwait(false): the dispatcher timer ticks on the UI thread and the
            // continuation resumes there, so the callbacks below run on the UI thread in the app.
            await _saveFunc(tabId, content);
            LastWriteCompleted = _clock.Now;

            _onSnapshot?.Invoke(tabId, content);
            _onSaveCompleted?.Invoke(tabId);
        }
        catch (Exception ex)
        {
            // Restore _isDirty = true so the next tick retries the save
            _isDirty = true;
            LogService.Error("AutosaveService tick failed for tab {TabId} — will retry on next tick", tabId, ex);
        }
    }
}
