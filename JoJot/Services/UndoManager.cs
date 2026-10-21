namespace JoJot.Services;

/// <summary>
/// Singleton that manages per-tab <see cref="UndoStack"/> instances under a global 50 MB
/// memory budget, with a per-tab cap (<see cref="UndoStack.DefaultMaxBytes"/>) enforced by each
/// stack. When usage exceeds 80 %, the manager collapses the oldest inactive stacks first
/// (tier-1 into tier-2), then evicts the oldest tier-2 entries, and finally the active tab's
/// tier-2 entries older than its current position, until usage drops to 60 %. The active tab's
/// fine-grained (tier-1) history is never collapsed. A collapse is only started when it can
/// actually free something.
/// All pushes go through this class so its cached byte total stays in sync with the stacks.
/// </summary>
public sealed class UndoManager
{
    private static UndoManager _instance = new(SystemClock.Instance);

    /// <summary>Singleton instance.</summary>
    public static UndoManager Instance => _instance;

    /// <summary>Replaces the singleton instance. For testing only.</summary>
    internal static void SetInstance(UndoManager manager) => _instance = manager;

    private readonly IClock _clock;
    private readonly Dictionary<long, UndoStack> _stacks = [];
    /// <summary>Cached total bytes for fast budget checks in the per-keystroke hot path.</summary>
    private long _cachedTotalBytes;

    /// <summary>Global memory budget (50 MB, not configurable).</summary>
    private const long MaxBudgetBytes = 50L * 1024 * 1024;

    /// <summary>Trigger collapse at 80 % of budget.</summary>
    private const double CollapseThreshold = 0.80;

    /// <summary>Target 60 % of budget after collapse.</summary>
    private const double CollapseTarget = 0.60;

    private readonly long _budgetBytes;
    private readonly long _perTabMaxBytes;

    /// <summary>Currently active tab, whose tier-1 history is exempt from collapse.</summary>
    private long? _activeTabId;

    /// <summary>Serializes access between UI-thread pushes and background collapse.</summary>
    private readonly object _lock = new();

    /// <summary>Prevents overlapping collapse operations.</summary>
    private volatile bool _collapseInProgress;

    private int _collapsesStarted;

    /// <summary>Creates a new UndoManager with the specified clock.</summary>
    internal UndoManager(IClock clock) : this(clock, MaxBudgetBytes, UndoStack.DefaultMaxBytes) { }

    /// <summary>Creates a new UndoManager with explicit limits. For testing.</summary>
    /// <param name="clock">Clock abstraction.</param>
    /// <param name="budgetBytes">Global budget; collapse starts above 80 % of it.</param>
    /// <param name="perTabMaxBytes">Byte cap applied to every stack this manager creates.</param>
    internal UndoManager(IClock clock, long budgetBytes, long perTabMaxBytes)
    {
        _clock = clock;
        _budgetBytes = budgetBytes;
        _perTabMaxBytes = perTabMaxBytes;
    }

    /// <summary>Number of background collapses started so far. Diagnostic/testing only.</summary>
    internal int CollapsesStarted => Volatile.Read(ref _collapsesStarted);

    /// <summary>The most recently started collapse (completed when none ran). Testing only.</summary>
    internal Task LastCollapse { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Returns the <see cref="UndoStack"/> for <paramref name="tabId"/>, creating one if needed.
    /// Mutate it through the manager's push methods so byte accounting stays correct.
    /// </summary>
    /// <param name="tabId">The tab identifier.</param>
    public UndoStack GetOrCreateStack(long tabId)
    {
        lock (_lock) return GetOrCreateStackLocked(tabId);
    }

    private UndoStack GetOrCreateStackLocked(long tabId)
    {
        if (!_stacks.TryGetValue(tabId, out var stack))
        {
            stack = new UndoStack(tabId, _clock) { MaxBytes = _perTabMaxBytes };
            _stacks[tabId] = stack;
        }
        return stack;
    }

    /// <summary>
    /// Returns the <see cref="UndoStack"/> for <paramref name="tabId"/>, or <c>null</c> if none exists.
    /// </summary>
    /// <param name="tabId">The tab identifier.</param>
    public UndoStack? GetStack(long tabId)
    {
        lock (_lock) return _stacks.GetValueOrDefault(tabId);
    }

    /// <summary>
    /// Sets the currently active tab. The active tab's tier-1 history is never collapsed.
    /// </summary>
    /// <param name="tabId">The active tab identifier, or <c>null</c> if none.</param>
    public void SetActiveTab(long? tabId)
    {
        lock (_lock) _activeTabId = tabId;
    }

    /// <summary>
    /// Removes the <see cref="UndoStack"/> for a tab that has been permanently deleted.
    /// </summary>
    /// <param name="tabId">The tab identifier to remove.</param>
    public void RemoveStack(long tabId)
    {
        lock (_lock)
        {
            if (_stacks.TryGetValue(tabId, out var stack))
                _cachedTotalBytes -= stack.EstimatedBytes;
            _stacks.Remove(tabId);
        }
    }

    /// <summary>
    /// Re-keys a tab's undo stack after an optimistic creation receives its database id.
    /// If the tab is currently active, active-tab tracking follows the new id.
    /// </summary>
    /// <param name="oldTabId">The temporary (negative) id the stack is currently keyed under.</param>
    /// <param name="newTabId">The real database id to re-key the stack under.</param>
    public void MigrateStack(long oldTabId, long newTabId)
    {
        lock (_lock)
        {
            if (_stacks.Remove(oldTabId, out var stack))
            {
                // Re-key the stack's own TabId too, not just the dictionary key — CollapseOldest
                // compares stack.TabId with _activeTabId for its collapse exemption, and logs print
                // TabId. A stale temp id would strip the active tab's exemption and log the wrong id.
                stack.TabId = newTabId;
                _stacks[newTabId] = stack;
            }
            if (_activeTabId == oldTabId)
                _activeTabId = newTabId;
        }
    }

    /// <summary>
    /// Pushes a snapshot to the tab's undo stack and triggers memory-budget collapse if needed.
    /// </summary>
    /// <param name="tabId">The tab identifier.</param>
    /// <param name="content">The content snapshot to push.</param>
    /// <param name="cursorPosition">The cursor position at the time of the snapshot.</param>
    public void PushSnapshot(long tabId, string content, int cursorPosition = 0) =>
        Mutate(tabId, stack => stack.PushSnapshot(content, cursorPosition));

    /// <summary>
    /// Resets the tab's stack to <paramref name="content"/> as its baseline (see
    /// <see cref="UndoStack.PushInitialContent"/>), keeping the byte accounting in sync.
    /// </summary>
    /// <param name="tabId">The tab identifier.</param>
    /// <param name="content">The baseline content.</param>
    /// <param name="cursorPosition">The cursor position to store with it.</param>
    public void PushInitialContent(long tabId, string content, int cursorPosition = 0) =>
        Mutate(tabId, stack => stack.PushInitialContent(content, cursorPosition));

    /// <summary>
    /// Seeds the tab's stack with <paramref name="content"/> when it has no undo/redo history yet
    /// (empty or a single entry) — the baseline Ctrl+Z restores to. No-op otherwise.
    /// </summary>
    /// <param name="tabId">The tab identifier.</param>
    /// <param name="content">The content loaded into the editor.</param>
    public void EnsureInitialContent(long tabId, string content) =>
        Mutate(tabId, stack =>
        {
            if (!stack.CanUndo && !stack.CanRedo)
                stack.PushInitialContent(content);
        });

    /// <summary>
    /// Adds a coarse tier-2 checkpoint (see <see cref="UndoStack.PushCheckpoint"/>), keeping the
    /// byte accounting in sync.
    /// </summary>
    /// <param name="tabId">The tab identifier.</param>
    /// <param name="content">The content to checkpoint.</param>
    /// <param name="cursorPosition">The cursor position at the time of the checkpoint.</param>
    public void PushCheckpoint(long tabId, string content, int cursorPosition = 0) =>
        Mutate(tabId, stack => stack.PushCheckpoint(content, cursorPosition));

    /// <summary>
    /// True when the tab has a stack and its last checkpoint is at least 5 minutes old. Lets the
    /// caller skip materializing the editor text when no checkpoint is due.
    /// </summary>
    /// <param name="tabId">The tab identifier.</param>
    public bool IsCheckpointDue(long tabId)
    {
        lock (_lock) return _stacks.GetValueOrDefault(tabId)?.ShouldCreateCheckpoint() ?? false;
    }

    /// <summary>
    /// Applies <paramref name="mutation"/> to the tab's stack under the lock, folds its byte delta
    /// into the cached total, and starts a background collapse when over the threshold and
    /// something can actually be freed.
    /// </summary>
    private void Mutate(long tabId, Action<UndoStack> mutation)
    {
        bool needsCollapse;
        lock (_lock)
        {
            var stack = GetOrCreateStackLocked(tabId);
            long bytesBefore = stack.EstimatedBytes;
            mutation(stack);
            _cachedTotalBytes += stack.EstimatedBytes - bytesBefore;
            needsCollapse = !_collapseInProgress
                && _cachedTotalBytes > (long)(_budgetBytes * CollapseThreshold)
                && AnyStackCanFreeLocked();
            if (needsCollapse)
                _collapseInProgress = true;
        }

        if (needsCollapse)
        {
            Interlocked.Increment(ref _collapsesStarted);
            LastCollapse = Task.Run(() =>
            {
                try
                {
                    lock (_lock) { CollapseOldest(); }
                }
                finally { _collapseInProgress = false; }
            });
        }
    }

    /// <summary>
    /// Undoes one step. Returns the previous entry, or <c>null</c> if at the beginning.
    /// </summary>
    /// <param name="tabId">The tab identifier.</param>
    public UndoStack.UndoEntry? Undo(long tabId)
    {
        lock (_lock) return _stacks.GetValueOrDefault(tabId)?.Undo();
    }

    /// <summary>
    /// Redoes one step. Returns the next entry, or <c>null</c> if at the end.
    /// </summary>
    /// <param name="tabId">The tab identifier.</param>
    public UndoStack.UndoEntry? Redo(long tabId)
    {
        lock (_lock) return _stacks.GetValueOrDefault(tabId)?.Redo();
    }

    /// <summary>Returns <c>true</c> if the tab has a previous state to undo to.</summary>
    /// <param name="tabId">The tab identifier.</param>
    public bool CanUndo(long tabId)
    {
        lock (_lock) return _stacks.GetValueOrDefault(tabId)?.CanUndo ?? false;
    }

    /// <summary>Returns <c>true</c> if the tab has a forward state to redo to.</summary>
    /// <param name="tabId">The tab identifier.</param>
    public bool CanRedo(long tabId)
    {
        lock (_lock) return _stacks.GetValueOrDefault(tabId)?.CanRedo ?? false;
    }

    /// <summary>
    /// Total estimated memory usage across all undo stacks, in bytes.
    /// Recomputed from stack caches — each stack tracks its own bytes incrementally.
    /// </summary>
    public long TotalEstimatedBytes
    {
        get { lock (_lock) return _stacks.Values.Sum(s => s.EstimatedBytes); }
    }

    /// <summary>The cached byte total the collapse threshold is checked against. Testing only.</summary>
    internal long CachedTotalBytes
    {
        get { lock (_lock) return _cachedTotalBytes; }
    }

    private bool AnyStackCanFreeLocked()
    {
        foreach (var stack in _stacks.Values)
        {
            if (stack.CanFreeMemory(isActive: stack.TabId == _activeTabId))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Brings memory usage under the target. Phase 1 converts tier-1 snapshots of the oldest
    /// inactive stacks into tier-2 checkpoints; phase 2 evicts their oldest tier-2 entries;
    /// phase 3 evicts the active stack's tier-2 entries older than its current position.
    /// Must be called under <see cref="_lock"/>.
    /// </summary>
    private void CollapseOldest()
    {
        // Re-sync the cache with the stacks first: a stack mutated directly (not through this
        // manager) would otherwise leave the cached total drifting from reality.
        _cachedTotalBytes = _stacks.Values.Sum(s => s.EstimatedBytes);

        long targetBytes = (long)(_budgetBytes * CollapseTarget);

        var candidates = _stacks.Values
            .Where(s => s.TabId != _activeTabId)
            .OrderBy(s => s.LastAccessTime)
            .ToList();

        // Collapse tier-1 into tier-2 for the oldest stacks first
        foreach (var stack in candidates)
        {
            if (_cachedTotalBytes <= targetBytes) break;

            long before = stack.EstimatedBytes;
            stack.CollapseTier1IntoTier2();
            _cachedTotalBytes += stack.EstimatedBytes - before;
            LogService.Debug("UndoManager: collapsed tier-1 for tab {TabId}, total={TotalKB}KB", stack.TabId, _cachedTotalBytes / 1024);
        }

        // Evict oldest tier-2 entries if still over target
        foreach (var stack in candidates)
        {
            if (_cachedTotalBytes <= targetBytes) break;

            long before = stack.EstimatedBytes;
            stack.EvictOldestTier2(5);
            _cachedTotalBytes += stack.EstimatedBytes - before;
            LogService.Debug("UndoManager: evicted tier-2 entries for tab {TabId}, total={TotalKB}KB", stack.TabId, _cachedTotalBytes / 1024);
        }

        // Last resort: the active tab's older checkpoints (never its current entry or tier-1).
        if (_cachedTotalBytes > targetBytes
            && _activeTabId is long activeId
            && _stacks.TryGetValue(activeId, out var active))
        {
            long before = active.EstimatedBytes;
            active.EvictOldestTier2BeforeCurrent(5);
            _cachedTotalBytes += active.EstimatedBytes - before;
            LogService.Debug("UndoManager: evicted active-tab tier-2 entries for tab {TabId}, total={TotalKB}KB", activeId, _cachedTotalBytes / 1024);
        }

        LogService.Debug("UndoManager: collapse complete, total={TotalKB}KB (target={TargetKB}KB)", _cachedTotalBytes / 1024, targetBytes / 1024);
    }
}
