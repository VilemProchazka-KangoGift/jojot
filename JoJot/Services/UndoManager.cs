namespace JoJot.Services
{
    /// <summary>
    /// Singleton managing all per-tab UndoStacks with a global 50MB memory budget (UNDO-05).
    /// Collapse strategy: oldest inactive tabs first, tier-1 into tier-2,
    /// then evict oldest tier-2. Active tab is never collapsed (UNDO-06).
    /// </summary>
    public class UndoManager
    {
        private static readonly Lazy<UndoManager> _instance = new(() => new UndoManager());

        /// <summary>Singleton instance.</summary>
        public static UndoManager Instance => _instance.Value;

        private readonly Dictionary<long, UndoStack> _stacks = new();

        /// <summary>50MB hardcoded budget — not configurable (user decision).</summary>
        private const long MaxBudgetBytes = 50L * 1024 * 1024;

        /// <summary>Trigger collapse at 80% of budget.</summary>
        private const double CollapseThreshold = 0.80;

        /// <summary>Target 60% of budget after collapse.</summary>
        private const double CollapseTarget = 0.60;

        /// <summary>Currently active tab — never collapsed.</summary>
        private long? _activeTabId;

        private UndoManager() { }

        /// <summary>
        /// Gets the UndoStack for a tab, creating one if it doesn't exist.
        /// </summary>
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
        /// Gets the UndoStack for a tab, or null if none exists.
        /// </summary>
        public UndoStack? GetStack(long tabId)
        {
            return _stacks.GetValueOrDefault(tabId);
        }

        /// <summary>
        /// Sets the currently active tab. The active tab is never collapsed.
        /// </summary>
        public void SetActiveTab(long? tabId)
        {
            _activeTabId = tabId;
        }

        /// <summary>
        /// Removes the UndoStack for a tab. Called when tab is permanently deleted.
        /// </summary>
        public void RemoveStack(long tabId)
        {
            _stacks.Remove(tabId);
        }

        /// <summary>
        /// Pushes a snapshot to the tab's undo stack and checks memory budget.
        /// Called after each debounced autosave.
        /// </summary>
        public void PushSnapshot(long tabId, string content)
        {
            var stack = GetOrCreateStack(tabId);
            stack.PushSnapshot(content);

            // Check memory budget after push
            if (TotalEstimatedBytes > (long)(MaxBudgetBytes * CollapseThreshold))
            {
                CollapseOldest();
            }
        }

        /// <summary>
        /// Undoes one step in the tab's undo stack.
        /// Returns the previous content, or null if nothing to undo.
        /// </summary>
        public string? Undo(long tabId)
        {
            var stack = GetStack(tabId);
            return stack?.Undo();
        }

        /// <summary>
        /// Redoes one step in the tab's undo stack.
        /// Returns the next content, or null if nothing to redo.
        /// </summary>
        public string? Redo(long tabId)
        {
            var stack = GetStack(tabId);
            return stack?.Redo();
        }

        /// <summary>True if the tab has a previous state to undo to.</summary>
        public bool CanUndo(long tabId)
        {
            var stack = GetStack(tabId);
            return stack?.CanUndo ?? false;
        }

        /// <summary>True if the tab has a forward state to redo to.</summary>
        public bool CanRedo(long tabId)
        {
            var stack = GetStack(tabId);
            return stack?.CanRedo ?? false;
        }

        /// <summary>
        /// Total estimated memory usage across all undo stacks.
        /// </summary>
        public long TotalEstimatedBytes =>
            _stacks.Values.Sum(s => s.EstimatedBytes);

        /// <summary>
        /// Collapses oldest inactive tabs to reduce memory usage.
        /// Strategy: sort tabs by LastAccessTime ascending, skip active tab,
        /// collapse tier-1 into tier-2, then evict oldest tier-2 if still over budget.
        /// </summary>
        private void CollapseOldest()
        {
            long targetBytes = (long)(MaxBudgetBytes * CollapseTarget);

            // Get stacks sorted by LastAccessTime (oldest first), excluding active tab
            var candidates = _stacks.Values
                .Where(s => s.TabId != _activeTabId)
                .OrderBy(s => s.LastAccessTime)
                .ToList();

            // Phase 1: Collapse tier-1 into tier-2 for oldest tabs
            foreach (var stack in candidates)
            {
                if (TotalEstimatedBytes <= targetBytes) break;
                stack.CollapseTier1IntoTier2();
                LogService.Info($"UndoManager: collapsed tier-1 for tab {stack.TabId}, total={TotalEstimatedBytes / 1024}KB");
            }

            // Phase 2: Evict oldest tier-2 entries if still over target
            foreach (var stack in candidates)
            {
                if (TotalEstimatedBytes <= targetBytes) break;
                stack.EvictOldestTier2(5);
                LogService.Info($"UndoManager: evicted tier-2 entries for tab {stack.TabId}, total={TotalEstimatedBytes / 1024}KB");
            }

            LogService.Info($"UndoManager: collapse complete, total={TotalEstimatedBytes / 1024}KB (target={targetBytes / 1024}KB)");
        }
    }
}
