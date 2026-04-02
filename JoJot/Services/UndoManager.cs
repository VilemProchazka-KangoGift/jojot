namespace JoJot.Services;

/// <summary>
/// Singleton that manages per-tab <see cref="UndoStack"/> instances under a global 50 MB
/// memory budget. When usage exceeds 80 %, the manager collapses the oldest inactive stacks
/// first (tier-1 into tier-2), then evicts the oldest tier-2 entries until usage drops to 60 %.
/// The active tab is never collapsed.
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

    /// <summary>Currently active tab, exempt from collapse.</summary>
    private long? _activeTabId;

    /// <summary>Serializes access between UI-thread pushes and background collapse.</summary>
    private readonly object _lock = new();

    /// <summary>Prevents overlapping collapse operations.</summary>
    private volatile bool _collapseInProgress;

    /// <summary>Creates a new UndoManager with the specified clock.</summary>
    internal UndoManager(IClock clock) { _clock = clock; }

    /// <summary>
    /// Returns the <see cref="UndoStack"/> for <paramref name="tabId"/>, creating one if needed.
    /// </summary>
    /// <param name="tabId">The tab identifier.</param>
    public UndoStack GetOrCreateStack(long tabId)
    {
        lock (_lock)
        {
            if (!_stacks.TryGetValue(tabId, out var stack))
            {
                stack = new UndoStack(tabId, _clock);
                _stacks[tabId] = stack;
            }
            return stack;
        }
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
    /// Sets the currently active tab. The active tab is never collapsed.
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
    /// Pushes a snapshot to the tab's undo stack and triggers memory-budget collapse if needed.
    /// </summary>
    /// <param name="tabId">The tab identifier.</param>
    /// <param name="content">The content snapshot to push.</param>
    /// <param name="cursorPosition">The cursor position at the time of the snapshot.</param>
    public void PushSnapshot(long tabId, string content, int cursorPosition = 0)
    {
        bool needsCollapse;
        lock (_lock)
        {
            if (!_stacks.TryGetValue(tabId, out var stack))
            {
                stack = new UndoStack(tabId, _clock);
                _stacks[tabId] = stack;
            }
            long bytesBefore = stack.EstimatedBytes;
            stack.PushSnapshot(content, cursorPosition);
            _cachedTotalBytes += stack.EstimatedBytes - bytesBefore;
            needsCollapse = !_collapseInProgress
                && _cachedTotalBytes > (long)(MaxBudgetBytes * CollapseThreshold);
        }

        if (needsCollapse)
        {
            _collapseInProgress = true;
            Task.Run(() =>
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
    public long TotalEstimatedBytes =>
        _stacks.Values.Sum(s => s.EstimatedBytes);

    /// <summary>
    /// Collapses the oldest inactive stacks to bring memory usage under the target.
    /// Phase 1 converts tier-1 snapshots into tier-2 checkpoints; phase 2 evicts
    /// the oldest tier-2 entries if still over budget.
    /// </summary>
    private void CollapseOldest()
    {
        long targetBytes = (long)(MaxBudgetBytes * CollapseTarget);

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
            LogService.Info("UndoManager: collapsed tier-1 for tab {TabId}, total={TotalKB}KB", stack.TabId, _cachedTotalBytes / 1024);
        }

        // Evict oldest tier-2 entries if still over target
        foreach (var stack in candidates)
        {
            if (_cachedTotalBytes <= targetBytes) break;

            long before = stack.EstimatedBytes;
            stack.EvictOldestTier2(5);
            _cachedTotalBytes += stack.EstimatedBytes - before;
            LogService.Info("UndoManager: evicted tier-2 entries for tab {TabId}, total={TotalKB}KB", stack.TabId, _cachedTotalBytes / 1024);
        }

        LogService.Info("UndoManager: collapse complete, total={TotalKB}KB (target={TargetKB}KB)", _cachedTotalBytes / 1024, targetBytes / 1024);
    }
}
