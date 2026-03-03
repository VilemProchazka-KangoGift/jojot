namespace JoJot.Services
{
    /// <summary>
    /// Per-tab in-memory undo/redo stack with two tiers (UNDO-01, UNDO-02, UNDO-03, UNDO-04).
    /// Tier-1: up to 50 fine-grained snapshots, pushed on every debounced autosave.
    /// Tier-2: up to 20 coarse checkpoints, saved every 5 minutes of active editing.
    /// The two tiers form a seamless logical sequence for undo/redo traversal.
    /// In-memory only — not persisted, discarded on window close (UNDO-08).
    /// </summary>
    public class UndoStack
    {
        public const int MaxTier1 = 50;
        public const int MaxTier2 = 20;

        private readonly List<string> _tier1 = new(); // Fine-grained snapshots
        private readonly List<string> _tier2 = new(); // Coarse checkpoints
        private int _currentIndex = -1; // Pointer into combined logical sequence
        private DateTime _lastCheckpointTime = DateTime.MinValue;

        /// <summary>The NoteTab.Id this stack belongs to.</summary>
        public long TabId { get; }

        /// <summary>Updated on any push/undo/redo. Used by UndoManager for LRU collapse.</summary>
        public DateTime LastAccessTime { get; set; } = DateTime.Now;

        public UndoStack(long tabId)
        {
            TabId = tabId;
        }

        /// <summary>Total entries across both tiers.</summary>
        private int TotalCount => _tier2.Count + _tier1.Count;

        /// <summary>True if there is a previous state to undo to.</summary>
        public bool CanUndo => _currentIndex > 0;

        /// <summary>True if there is a forward state to redo to.</summary>
        public bool CanRedo => _currentIndex < TotalCount - 1;

        /// <summary>
        /// Estimated memory usage in bytes. Uses sizeof(char) = 2 for .NET UTF-16 strings.
        /// </summary>
        public long EstimatedBytes =>
            _tier1.Sum(s => (long)s.Length * 2) +
            _tier2.Sum(s => (long)s.Length * 2);

        /// <summary>
        /// Called once when a tab is loaded from DB. This is the "floor" —
        /// Ctrl+Z can always restore to this loaded state.
        /// Clears both tiers and sets content as the first snapshot.
        /// </summary>
        public void PushInitialContent(string content)
        {
            _tier1.Clear();
            _tier2.Clear();
            _tier1.Add(content);
            _currentIndex = 0;
            _lastCheckpointTime = DateTime.Now;
            LastAccessTime = DateTime.Now;
        }

        /// <summary>
        /// Called on every debounced autosave if content differs from current snapshot.
        /// Typing after undo destroys the redo future (linear stack).
        /// </summary>
        public void PushSnapshot(string content)
        {
            if (TotalCount == 0)
            {
                // First snapshot ever — just add it
                _tier1.Add(content);
                _currentIndex = 0;
                LastAccessTime = DateTime.Now;
                return;
            }

            // Check if content is same as current position
            string? current = GetContentAtIndex(_currentIndex);
            if (current != null && current == content) return;

            // Destroy redo future: truncate everything after current index
            TruncateAfterCurrent();

            // Add to tier-1
            _tier1.Add(content);

            // Enforce tier-1 max: remove oldest tier-1 entry if over limit
            if (_tier1.Count > MaxTier1)
            {
                _tier1.RemoveAt(0);
            }

            _currentIndex = TotalCount - 1;
            LastAccessTime = DateTime.Now;
        }

        /// <summary>
        /// Called by the 5-minute checkpoint timer if content differs from last checkpoint.
        /// Adds a coarse checkpoint to tier-2.
        /// </summary>
        public void PushCheckpoint(string content)
        {
            // Don't duplicate if same as last checkpoint
            if (_tier2.Count > 0 && _tier2[^1] == content) return;

            _tier2.Add(content);

            // Enforce tier-2 max: remove oldest
            if (_tier2.Count > MaxTier2)
            {
                _tier2.RemoveAt(0);
                // Adjust current index since tier-2 prefix shrank
                if (_currentIndex > 0) _currentIndex--;
            }

            _lastCheckpointTime = DateTime.Now;
            LastAccessTime = DateTime.Now;
        }

        /// <summary>
        /// Moves backward in the undo history. Returns the content at the new position,
        /// or null if already at the beginning.
        /// </summary>
        public string? Undo()
        {
            if (!CanUndo) return null;
            _currentIndex--;
            LastAccessTime = DateTime.Now;
            return GetContentAtIndex(_currentIndex);
        }

        /// <summary>
        /// Moves forward in the redo history. Returns the content at the new position,
        /// or null if already at the end.
        /// </summary>
        public string? Redo()
        {
            if (!CanRedo) return null;
            _currentIndex++;
            LastAccessTime = DateTime.Now;
            return GetContentAtIndex(_currentIndex);
        }

        /// <summary>
        /// Returns true if more than 5 minutes have elapsed since the last checkpoint.
        /// </summary>
        public bool ShouldCreateCheckpoint()
        {
            return (DateTime.Now - _lastCheckpointTime).TotalMinutes >= 5;
        }

        /// <summary>
        /// Collapses tier-1 into tier-2 by sampling every 5th entry.
        /// Called by UndoManager during memory pressure (UNDO-06).
        /// </summary>
        public void CollapseTier1IntoTier2()
        {
            if (_tier1.Count == 0) return;

            // Sample every 5th tier-1 entry as a checkpoint
            for (int i = 0; i < _tier1.Count; i += 5)
            {
                if (_tier2.Count < MaxTier2)
                {
                    _tier2.Add(_tier1[i]);
                }
            }

            _tier1.Clear();
            _currentIndex = TotalCount > 0 ? TotalCount - 1 : -1;
        }

        /// <summary>
        /// Evicts the oldest tier-2 entries. Called during extreme memory pressure.
        /// </summary>
        public void EvictOldestTier2(int count)
        {
            int toRemove = Math.Min(count, _tier2.Count);
            if (toRemove <= 0) return;

            _tier2.RemoveRange(0, toRemove);
            _currentIndex = Math.Max(0, _currentIndex - toRemove);

            if (TotalCount == 0) _currentIndex = -1;
        }

        /// <summary>
        /// Gets the content string at the given logical index across both tiers.
        /// Tier-2 occupies indices [0, _tier2.Count - 1].
        /// Tier-1 occupies indices [_tier2.Count, TotalCount - 1].
        /// </summary>
        private string? GetContentAtIndex(int index)
        {
            if (index < 0 || index >= TotalCount) return null;

            if (index < _tier2.Count)
                return _tier2[index];

            return _tier1[index - _tier2.Count];
        }

        /// <summary>
        /// Truncates everything after _currentIndex (destroys redo future).
        /// Handles the case where _currentIndex spans across tier-2 and tier-1.
        /// </summary>
        private void TruncateAfterCurrent()
        {
            int totalAfterCurrent = TotalCount - 1 - _currentIndex;
            if (totalAfterCurrent <= 0) return;

            // How many tier-1 entries are after current?
            int tier1Start = _tier2.Count;
            int tier1EntriesAfterCurrent = 0;

            if (_currentIndex >= tier1Start)
            {
                // Current index is in tier-1
                int tier1CurrentPos = _currentIndex - tier1Start;
                tier1EntriesAfterCurrent = _tier1.Count - 1 - tier1CurrentPos;
                if (tier1EntriesAfterCurrent > 0)
                {
                    _tier1.RemoveRange(tier1CurrentPos + 1, tier1EntriesAfterCurrent);
                }
            }
            else
            {
                // Current index is in tier-2 — remove all tier-1 entries
                // and truncate tier-2 after current
                _tier1.Clear();
                int tier2EntriesAfterCurrent = _tier2.Count - 1 - _currentIndex;
                if (tier2EntriesAfterCurrent > 0)
                {
                    _tier2.RemoveRange(_currentIndex + 1, tier2EntriesAfterCurrent);
                }
            }
        }
    }
}
