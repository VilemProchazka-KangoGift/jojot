namespace JoJot.Services;

/// <summary>
/// Per-tab in-memory undo/redo stack with two tiers.
/// Tier-1 holds up to 200 fine-grained snapshots pushed on every keystroke.
/// Tier-2 holds up to 20 coarse checkpoints saved every 5 minutes of active editing.
/// The two tiers form a seamless logical sequence for undo/redo traversal.
/// State is in-memory only and is discarded when the window closes.
/// </summary>
/// <param name="tabId">The <c>NoteTab.Id</c> this stack belongs to.</param>
/// <param name="clock">Clock abstraction for testability; defaults to system clock.</param>
public class UndoStack(long tabId, IClock? clock = null)
{
    /// <summary>A snapshot entry storing content and cursor position.</summary>
    public readonly record struct UndoEntry(string Content, int CursorPosition);

    /// <summary>Maximum number of tier-1 (fine-grained) snapshots.</summary>
    public const int MaxTier1 = 200;

    /// <summary>Maximum number of tier-2 (coarse checkpoint) entries.</summary>
    public const int MaxTier2 = 20;

    /// <summary>Number of oldest tier-1 entries to batch-promote when tier-1 overflows.</summary>
    internal const int PromotionBatchSize = 50;

    /// <summary>Sample rate within the promotion batch (every Nth entry is promoted to tier-2).</summary>
    internal const int PromotionSampleRate = 10;

    private readonly IClock _clock = clock ?? SystemClock.Instance;
    private readonly List<UndoEntry> _tier1 = []; // Fine-grained snapshots
    private readonly List<UndoEntry> _tier2 = []; // Coarse checkpoints
    private int _currentIndex = -1;            // Pointer into the combined logical sequence
    private DateTime _lastCheckpointTime = DateTime.MinValue;

    /// <summary>The <c>NoteTab.Id</c> this stack belongs to.</summary>
    public long TabId { get; } = tabId;

    /// <summary>Updated on any push, undo, or redo. Used by <see cref="UndoManager"/> for LRU collapse.</summary>
    public DateTime LastAccessTime { get; set; } = (clock ?? SystemClock.Instance).Now;

    /// <summary>Total entries across both tiers.</summary>
    private int TotalCount => _tier2.Count + _tier1.Count;

    /// <summary>Returns <c>true</c> if there is a previous state to undo to.</summary>
    public bool CanUndo => _currentIndex > 0;

    /// <summary>Returns <c>true</c> if there is a forward state to redo to.</summary>
    public bool CanRedo => _currentIndex < TotalCount - 1;

    /// <summary>
    /// Estimated memory usage in bytes (UTF-16: 2 bytes per character).
    /// </summary>
    public long EstimatedBytes =>
        _tier1.Sum(e => (long)e.Content.Length * 2) +
        _tier2.Sum(e => (long)e.Content.Length * 2);

    /// <summary>
    /// Initializes the stack with the content loaded from the database.
    /// This becomes the baseline state that Ctrl+Z can always restore to.
    /// </summary>
    /// <param name="content">The initial content from the database.</param>
    /// <param name="cursorPosition">The cursor position to store with the initial content.</param>
    public void PushInitialContent(string content, int cursorPosition = 0)
    {
        _tier1.Clear();
        _tier2.Clear();
        _tier1.Add(new UndoEntry(content, cursorPosition));
        _currentIndex = 0;
        _lastCheckpointTime = _clock.Now;
        LastAccessTime = _clock.Now;
    }

    /// <summary>
    /// Pushes a snapshot on every keystroke when content differs from the current state.
    /// Typing after undo destroys the redo future (linear stack model).
    /// </summary>
    /// <param name="content">The content snapshot to push.</param>
    /// <param name="cursorPosition">The cursor position at the time of the snapshot.</param>
    public void PushSnapshot(string content, int cursorPosition = 0)
    {
        if (TotalCount == 0)
        {
            _tier1.Add(new UndoEntry(content, cursorPosition));
            _currentIndex = 0;
            LastAccessTime = _clock.Now;
            return;
        }

        var current = GetEntryAtIndex(_currentIndex);
        if (current is not null && current.Value.Content == content)
        {
            return;
        }

        // Destroy redo future
        TruncateAfterCurrent();

        _tier1.Add(new UndoEntry(content, cursorPosition));

        if (_tier1.Count > MaxTier1)
        {
            PromoteOldestTier1Entries();
        }

        _currentIndex = TotalCount - 1;
        LastAccessTime = _clock.Now;
    }

    /// <summary>
    /// Adds a coarse checkpoint to tier-2 if content differs from the last checkpoint.
    /// Called by the 5-minute checkpoint timer.
    /// </summary>
    /// <param name="content">The content to checkpoint.</param>
    /// <param name="cursorPosition">The cursor position at the time of the checkpoint.</param>
    public void PushCheckpoint(string content, int cursorPosition = 0)
    {
        if (_tier2.Count > 0 && _tier2[^1].Content == content)
        {
            return;
        }

        _tier2.Add(new UndoEntry(content, cursorPosition));

        if (_tier2.Count > MaxTier2)
        {
            _tier2.RemoveAt(0);
            if (_currentIndex > 0)
            {
                _currentIndex--;
            }
        }

        _lastCheckpointTime = _clock.Now;
        LastAccessTime = _clock.Now;
    }

    /// <summary>
    /// Moves backward in the undo history.
    /// Returns the entry at the new position, or <c>null</c> if already at the beginning.
    /// </summary>
    public UndoEntry? Undo()
    {
        if (!CanUndo)
        {
            return null;
        }

        _currentIndex--;
        LastAccessTime = _clock.Now;
        return GetEntryAtIndex(_currentIndex);
    }

    /// <summary>
    /// Moves forward in the redo history.
    /// Returns the entry at the new position, or <c>null</c> if already at the end.
    /// </summary>
    public UndoEntry? Redo()
    {
        if (!CanRedo)
        {
            return null;
        }

        _currentIndex++;
        LastAccessTime = _clock.Now;
        return GetEntryAtIndex(_currentIndex);
    }

    /// <summary>
    /// Returns <c>true</c> if more than 5 minutes have elapsed since the last checkpoint.
    /// </summary>
    public bool ShouldCreateCheckpoint() => (_clock.Now - _lastCheckpointTime).TotalMinutes >= 5;

    /// <summary>
    /// Collapses tier-1 into tier-2 by sampling every 5th entry.
    /// Called by <see cref="UndoManager"/> during memory pressure.
    /// </summary>
    public void CollapseTier1IntoTier2()
    {
        if (_tier1.Count == 0)
        {
            return;
        }

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
    /// <param name="count">Maximum number of entries to evict.</param>
    public void EvictOldestTier2(int count)
    {
        int toRemove = Math.Min(count, _tier2.Count);
        if (toRemove <= 0)
        {
            return;
        }

        _tier2.RemoveRange(0, toRemove);
        _currentIndex = Math.Max(0, _currentIndex - toRemove);

        if (TotalCount == 0)
        {
            _currentIndex = -1;
        }
    }

    /// <summary>
    /// When tier-1 overflows, batch-promotes the oldest entries by sampling every
    /// <see cref="PromotionSampleRate"/>th entry into tier-2, then removes the batch.
    /// This creates a smooth degradation gradient instead of discarding the oldest entry.
    /// </summary>
    internal void PromoteOldestTier1Entries()
    {
        int batchSize = Math.Min(PromotionBatchSize, _tier1.Count);
        if (batchSize <= 0)
        {
            return;
        }

        // Sample every Nth entry from the batch into tier-2
        for (int i = 0; i < batchSize; i += PromotionSampleRate)
        {
            if (_tier2.Count < MaxTier2)
            {
                _tier2.Add(_tier1[i]);
            }
        }

        // Remove the batch from tier-1
        _tier1.RemoveRange(0, batchSize);

        // Adjust currentIndex: the removed entries shifted all indices down
        _currentIndex = Math.Max(0, _currentIndex - batchSize);

        // Compensate: promoted entries added to tier-2 shift tier-1 indices up
        // The new tier-2 entries occupy the front of the logical sequence,
        // so currentIndex needs to account for the new tier-2 count
        _currentIndex = TotalCount > 0 ? Math.Min(_currentIndex + _tier2.Count, TotalCount - 1) : -1;
    }

    /// <summary>
    /// Returns the entry at the given logical index across both tiers.
    /// Tier-2 occupies indices <c>[0, _tier2.Count - 1]</c>;
    /// tier-1 occupies indices <c>[_tier2.Count, TotalCount - 1]</c>.
    /// </summary>
    private UndoEntry? GetEntryAtIndex(int index)
    {
        if (index < 0 || index >= TotalCount)
        {
            return null;
        }

        if (index < _tier2.Count)
        {
            return _tier2[index];
        }

        return _tier1[index - _tier2.Count];
    }

    /// <summary>
    /// Truncates everything after the current index, destroying the redo future.
    /// Handles the boundary where the current index spans across tier-2 and tier-1.
    /// </summary>
    private void TruncateAfterCurrent()
    {
        int totalAfterCurrent = TotalCount - 1 - _currentIndex;
        if (totalAfterCurrent <= 0)
        {
            return;
        }

        int tier1Start = _tier2.Count;

        if (_currentIndex >= tier1Start)
        {
            // Current index is in tier-1
            int tier1CurrentPos = _currentIndex - tier1Start;
            int tier1EntriesAfterCurrent = _tier1.Count - 1 - tier1CurrentPos;
            if (tier1EntriesAfterCurrent > 0)
            {
                _tier1.RemoveRange(tier1CurrentPos + 1, tier1EntriesAfterCurrent);
            }
        }
        else
        {
            // Current index is in tier-2; remove all tier-1 and truncate tier-2
            _tier1.Clear();
            int tier2EntriesAfterCurrent = _tier2.Count - 1 - _currentIndex;
            if (tier2EntriesAfterCurrent > 0)
            {
                _tier2.RemoveRange(_currentIndex + 1, tier2EntriesAfterCurrent);
            }
        }
    }
}
