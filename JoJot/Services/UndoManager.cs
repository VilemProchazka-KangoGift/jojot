namespace JoJot.Services;

/// <summary>
/// Singleton that manages per-tab <see cref="UndoStack"/> instances under a global 50 MB
/// memory budget. When usage exceeds 80 %, the manager collapses the oldest inactive stacks
/// first (tier-1 into tier-2), then evicts the oldest tier-2 entries until usage drops to 60 %.
/// The active tab is never collapsed.
/// </summary>
public class UndoManager
{
    private static readonly Lazy<UndoManager> _instance = new(() => new UndoManager());

    /// <summary>Singleton instance.</summary>
    public static UndoManager Instance => _instance.Value;

    private readonly Dictionary<long, UndoStack> _stacks = [];

    /// <summary>Global memory budget (50 MB, not configurable).</summary>
    private const long MaxBudgetBytes = 50L * 1024 * 1024;

    /// <summary>Trigger collapse at 80 % of budget.</summary>
    private const double CollapseThreshold = 0.80;

    /// <summary>Target 60 % of budget after collapse.</summary>
    private const double CollapseTarget = 0.60;

    /// <summary>Currently active tab, exempt from collapse.</summary>
    private long? _activeTabId;

    private UndoManager() { }

    /// <summary>
    /// Returns the <see cref="UndoStack"/> for <paramref name="tabId"/>, creating one if needed.
    /// </summary>
    /// <param name="tabId">The tab identifier.</param>
    public UndoStack GetOrCreateStack(long tabId)
    {
        if (!_stacks.TryGetValue(tabId, out var stack))
        {
            stack = new UndoStack(tabId);
            _stacks[tabId] = stack;
        }
        return stack;
    }

    /// <summary>
    /// Returns the <see cref="UndoStack"/> for <paramref name="tabId"/>, or <c>null</c> if none exists.
    /// </summary>
    /// <param name="tabId">The tab identifier.</param>
    public UndoStack? GetStack(long tabId) => _stacks.GetValueOrDefault(tabId);

    /// <summary>
    /// Sets the currently active tab. The active tab is never collapsed.
    /// </summary>
    /// <param name="tabId">The active tab identifier, or <c>null</c> if none.</param>
    public void SetActiveTab(long? tabId)
    {
        _activeTabId = tabId;
    }

    /// <summary>
    /// Removes the <see cref="UndoStack"/> for a tab that has been permanently deleted.
    /// </summary>
    /// <param name="tabId">The tab identifier to remove.</param>
    public void RemoveStack(long tabId)
    {
        _stacks.Remove(tabId);
    }

    /// <summary>
    /// Pushes a snapshot to the tab's undo stack and triggers memory-budget collapse if needed.
    /// </summary>
    /// <param name="tabId">The tab identifier.</param>
    /// <param name="content">The content snapshot to push.</param>
    public void PushSnapshot(long tabId, string content)
    {
        var stack = GetOrCreateStack(tabId);
        stack.PushSnapshot(content);

        if (TotalEstimatedBytes > (long)(MaxBudgetBytes * CollapseThreshold))
        {
            CollapseOldest();
        }
    }

    /// <summary>
    /// Undoes one step. Returns the previous content, or <c>null</c> if at the beginning.
    /// </summary>
    /// <param name="tabId">The tab identifier.</param>
    public string? Undo(long tabId)
    {
        var stack = GetStack(tabId);
        return stack?.Undo();
    }

    /// <summary>
    /// Redoes one step. Returns the next content, or <c>null</c> if at the end.
    /// </summary>
    /// <param name="tabId">The tab identifier.</param>
    public string? Redo(long tabId)
    {
        var stack = GetStack(tabId);
        return stack?.Redo();
    }

    /// <summary>Returns <c>true</c> if the tab has a previous state to undo to.</summary>
    /// <param name="tabId">The tab identifier.</param>
    public bool CanUndo(long tabId)
    {
        var stack = GetStack(tabId);
        return stack?.CanUndo ?? false;
    }

    /// <summary>Returns <c>true</c> if the tab has a forward state to redo to.</summary>
    /// <param name="tabId">The tab identifier.</param>
    public bool CanRedo(long tabId)
    {
        var stack = GetStack(tabId);
        return stack?.CanRedo ?? false;
    }

    /// <summary>
    /// Total estimated memory usage across all undo stacks, in bytes.
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
            if (TotalEstimatedBytes <= targetBytes)
            {
                break;
            }

            stack.CollapseTier1IntoTier2();
            LogService.Info($"UndoManager: collapsed tier-1 for tab {stack.TabId}, total={TotalEstimatedBytes / 1024}KB");
        }

        // Evict oldest tier-2 entries if still over target
        foreach (var stack in candidates)
        {
            if (TotalEstimatedBytes <= targetBytes)
            {
                break;
            }

            stack.EvictOldestTier2(5);
            LogService.Info($"UndoManager: evicted tier-2 entries for tab {stack.TabId}, total={TotalEstimatedBytes / 1024}KB");
        }

        LogService.Info($"UndoManager: collapse complete, total={TotalEstimatedBytes / 1024}KB (target={targetBytes / 1024}KB)");
    }
}
