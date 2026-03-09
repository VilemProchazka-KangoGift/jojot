using System.Windows.Threading;

namespace JoJot.Services;

/// <summary>
/// Per-window autosave service with reset-on-keystroke debounce.
/// Uses <see cref="DispatcherTimer"/> for UI-thread-safe timer management.
/// The timer resets on every keystroke; a save fires after <see cref="DebounceMs"/> of
/// inactivity. A write frequency cap prevents scheduling a new write sooner than
/// <see cref="DebounceMs"/> after the previous write completed.
/// </summary>
public class AutosaveService
{
    private readonly DispatcherTimer _timer;
    private int _debounceMs = 500;
    private DateTime _lastWriteCompleted = DateTime.MinValue;
    private bool _isDirty;
    private Func<(long TabId, string Content)>? _contentProvider;
    private Action<long>? _onSaveCompleted;

    /// <summary>
    /// Initializes a new instance of <see cref="AutosaveService"/> with a 500 ms debounce timer.
    /// </summary>
    public AutosaveService()
    {
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(_debounceMs)
        };
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
            _timer.Interval = TimeSpan.FromMilliseconds(value);
        }
    }

    /// <summary>
    /// Configures the service with a content provider and a completion callback.
    /// </summary>
    /// <param name="contentProvider">Returns <c>(TabId, Content)</c> for the currently active tab.</param>
    /// <param name="onSaveCompleted">Invoked after a successful save with the tab ID.</param>
    public void Configure(
        Func<(long TabId, string Content)> contentProvider,
        Action<long> onSaveCompleted)
    {
        _contentProvider = contentProvider;
        _onSaveCompleted = onSaveCompleted;
    }

    /// <summary>
    /// Called on every user-initiated <c>TextChanged</c> event. Resets the debounce timer
    /// unless within the write-frequency cooldown and a timer is already scheduled.
    /// </summary>
    public void NotifyTextChanged()
    {
        var elapsed = DateTime.Now - _lastWriteCompleted;
        if (elapsed.TotalMilliseconds < _debounceMs && _timer.IsEnabled)
        {
            // Within cooldown and timer already scheduled; don't reset
            return;
        }

        _isDirty = true;
        _timer.Stop();
        _timer.Interval = TimeSpan.FromMilliseconds(_debounceMs);
        _timer.Start();
    }

    /// <summary>
    /// Flushes any pending content to the database immediately.
    /// Called on tab switch and application close.
    /// </summary>
    public async Task FlushAsync()
    {
        _timer.Stop();

        if (!_isDirty || _contentProvider is null)
        {
            return;
        }

        var (tabId, content) = _contentProvider();
        if (tabId <= 0)
        {
            return;
        }

        try
        {
            await DatabaseService.UpdateNoteContentAsync(tabId, content).ConfigureAwait(false);
            _lastWriteCompleted = DateTime.Now;
            _isDirty = false;

            UndoManager.Instance.PushSnapshot(tabId, content);

            _onSaveCompleted?.Invoke(tabId);
        }
        catch (Exception ex)
        {
            LogService.Error($"AutosaveService flush failed for tab {tabId}", ex);
        }
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
    /// content to the database, and pushes an undo snapshot.
    /// </summary>
    private async void OnTimerTick(object? sender, EventArgs e)
    {
        _timer.Stop(); // Single-shot: don't fire again until next NotifyTextChanged

        if (!_isDirty || _contentProvider is null)
        {
            return;
        }

        var (tabId, content) = _contentProvider();
        if (tabId <= 0)
        {
            return;
        }

        try
        {
            await DatabaseService.UpdateNoteContentAsync(tabId, content);
            _lastWriteCompleted = DateTime.Now;
            _isDirty = false;

            UndoManager.Instance.PushSnapshot(tabId, content);

            _onSaveCompleted?.Invoke(tabId);
        }
        catch (Exception ex)
        {
            LogService.Error($"AutosaveService tick failed for tab {tabId}", ex);
        }
    }
}
