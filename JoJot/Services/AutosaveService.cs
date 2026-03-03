using System.Windows.Threading;

namespace JoJot.Services
{
    /// <summary>
    /// Per-window autosave service with reset-on-keystroke debounce (EDIT-02, EDIT-03).
    /// Uses DispatcherTimer for UI-thread-safe timer management.
    /// The timer resets on every keystroke; save fires after DebounceMs of inactivity.
    /// Write frequency cap: new write cannot be scheduled sooner than DebounceMs after
    /// previous write completed.
    /// </summary>
    public class AutosaveService
    {
        private readonly DispatcherTimer _timer;
        private int _debounceMs = 500;
        private DateTime _lastWriteCompleted = DateTime.MinValue;
        private Func<(long TabId, string Content)>? _contentProvider;
        private Action<long>? _onSaveCompleted;

        public AutosaveService()
        {
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(_debounceMs)
            };
            _timer.Tick += OnTimerTick;
        }

        /// <summary>
        /// Configurable debounce interval in milliseconds. Default 500ms.
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
        /// Configures the service with a content provider and completion callback.
        /// contentProvider: returns (TabId, Content) for the currently active tab.
        /// onSaveCompleted: called after a successful save with the tab ID.
        /// </summary>
        public void Configure(
            Func<(long TabId, string Content)> contentProvider,
            Action<long> onSaveCompleted)
        {
            _contentProvider = contentProvider;
            _onSaveCompleted = onSaveCompleted;
        }

        /// <summary>
        /// Called on every user-initiated TextChanged. Resets the debounce timer.
        /// EDIT-03: Write frequency cap — if within cooldown AND timer already running,
        /// don't reset (let existing timer fire on schedule).
        /// </summary>
        public void NotifyTextChanged()
        {
            var elapsed = DateTime.Now - _lastWriteCompleted;
            if (elapsed.TotalMilliseconds < _debounceMs && _timer.IsEnabled)
            {
                // Within cooldown and timer already scheduled — don't reset
                return;
            }

            // Reset-on-keystroke: stop and restart timer
            _timer.Stop();
            _timer.Interval = TimeSpan.FromMilliseconds(_debounceMs);
            _timer.Start();
        }

        /// <summary>
        /// Flushes any pending content to the database immediately.
        /// Called on tab switch and app close. Blocks until save completes.
        /// </summary>
        public async Task FlushAsync()
        {
            _timer.Stop();

            if (_contentProvider == null) return;

            var (tabId, content) = _contentProvider();
            if (tabId <= 0) return;

            try
            {
                await DatabaseService.UpdateNoteContentAsync(tabId, content);
                _lastWriteCompleted = DateTime.Now;

                // Push undo snapshot on flush
                UndoManager.Instance.PushSnapshot(tabId, content);

                _onSaveCompleted?.Invoke(tabId);
            }
            catch (Exception ex)
            {
                // EDIT-02: Save failures handled with silent retry on next cycle; error logged only
                LogService.Error($"AutosaveService flush failed for tab {tabId}", ex);
            }
        }

        /// <summary>
        /// Stops the autosave timer. Called on tab switch before context changes.
        /// </summary>
        public void Stop()
        {
            _timer.Stop();
        }

        /// <summary>
        /// Timer tick handler — fires after debounce interval of inactivity.
        /// Saves content to database and pushes undo snapshot.
        /// </summary>
        private async void OnTimerTick(object? sender, EventArgs e)
        {
            _timer.Stop(); // Single-shot: don't fire again until next NotifyTextChanged

            if (_contentProvider == null) return;

            var (tabId, content) = _contentProvider();
            if (tabId <= 0) return;

            try
            {
                await DatabaseService.UpdateNoteContentAsync(tabId, content);
                _lastWriteCompleted = DateTime.Now;

                // Push undo snapshot after successful save
                UndoManager.Instance.PushSnapshot(tabId, content);

                _onSaveCompleted?.Invoke(tabId);
            }
            catch (Exception ex)
            {
                // EDIT-02: Save failures handled with silent retry on next debounce cycle
                LogService.Error($"AutosaveService tick failed for tab {tabId}", ex);
            }
        }
    }
}
