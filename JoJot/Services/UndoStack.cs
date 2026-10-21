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
public sealed class UndoStack(long tabId, IClock? clock = null)
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

    /// <summary>Default per-tab byte cap (16 MB). One very large note cannot hold more undo history
    /// than this, so it can never push the global budget over its collapse threshold on its own.</summary>
    public const long DefaultMaxBytes = 16L * 1024 * 1024;

    private readonly IClock _clock = clock ?? SystemClock.Instance;
    private readonly List<UndoEntry> _tier1 = []; // Fine-grained snapshots
    private readonly List<UndoEntry> _tier2 = []; // Coarse checkpoints
    private int _currentIndex = -1;            // Pointer into the combined logical sequence
    private DateTime _lastCheckpointTime = DateTime.MinValue;
    private long _estimatedBytes;              // Cached — updated incrementally on add/remove

    /// <summary>
    /// The <c>NoteTab.Id</c> this stack belongs to. Settable only by <see cref="UndoManager"/>
    /// (via <c>MigrateStack</c>) when an optimistic tab's temp id is swapped for its real DB id.
    /// </summary>
    public long TabId { get; internal set; } = tabId;

    /// <summary>Updated on any push, undo, or redo. Used by <see cref="UndoManager"/> for LRU collapse.</summary>
    public DateTime LastAccessTime { get; set; } = (clock ?? SystemClock.Instance).Now;

    /// <summary>
    /// Per-tab byte cap. When a push takes the stack above it, the oldest entries are dropped
    /// (never the current one). Defaults to <see cref="DefaultMaxBytes"/>.
    /// </summary>
    public long MaxBytes { get; set; } = DefaultMaxBytes;

    /// <summary>Total entries across both tiers.</summary>
    internal int TotalCount => _tier2.Count + _tier1.Count;

    /// <summary>Returns <c>true</c> if there is a previous state to undo to.</summary>
    public bool CanUndo => _currentIndex > 0;

    /// <summary>Returns <c>true</c> if there is a forward state to redo to.</summary>
    public bool CanRedo => _currentIndex < TotalCount - 1;

    /// <summary>
    /// Estimated memory usage in bytes (UTF-16: 2 bytes per character).
    /// Tracked incrementally — O(1) per access.
    /// </summary>
    public long EstimatedBytes => _estimatedBytes;

    private static long EntryBytes(UndoEntry entry) => (long)entry.Content.Length * 2;

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
        _estimatedBytes = 0;
        var entry = new UndoEntry(content, cursorPosition);
        _tier1.Add(entry);
        _estimatedBytes += EntryBytes(entry);
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
            var entry = new UndoEntry(content, cursorPosition);
            _tier1.Add(entry);
            _estimatedBytes += EntryBytes(entry);
            _currentIndex = 0;
            LastAccessTime = _clock.Now;
            return;
        }

        var current = GetEntryAtIndex(_currentIndex);
        if (current is not null
            && current.Value.Content.Length == content.Length
            && current.Value.Content == content)
        {
            return;
        }

        // Destroy redo future
        TruncateAfterCurrent();

        var newEntry = new UndoEntry(content, cursorPosition);
        _tier1.Add(newEntry);
        _estimatedBytes += EntryBytes(newEntry);

        if (_tier1.Count > MaxTier1)
        {
            PromoteOldestTier1Entries();
        }

        _currentIndex = TotalCount - 1;
        LastAccessTime = _clock.Now;
        EnforceByteCap();
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

        var entry = new UndoEntry(content, cursorPosition);
        _tier2.Add(entry);
        _estimatedBytes += EntryBytes(entry);

        if (_tier2.Count > MaxTier2)
        {
            _estimatedBytes -= EntryBytes(_tier2[0]);
            _tier2.RemoveAt(0);
            if (_currentIndex > 0)
            {
                _currentIndex--;
            }
        }

        _lastCheckpointTime = _clock.Now;
        LastAccessTime = _clock.Now;
        EnforceByteCap();
    }

    /// <summary>
    /// Drops the oldest entries (the front of the logical sequence: tier-2 first, then tier-1)
    /// while the stack is above <see cref="MaxBytes"/>. Never drops the current entry, so a single
    /// entry larger than the cap is kept.
    /// </summary>
    private void EnforceByteCap()
    {
        while (_estimatedBytes > MaxBytes && _currentIndex > 0)
        {
            if (_tier2.Count > 0)
            {
                _estimatedBytes -= EntryBytes(_tier2[0]);
                _tier2.RemoveAt(0);
            }
            else
            {
                _estimatedBytes -= EntryBytes(_tier1[0]);
                _tier1.RemoveAt(0);
            }
            _currentIndex--;
        }
    }

    /// <summary>
    /// Whether a memory-pressure collapse could free anything from this stack. An inactive stack
    /// can collapse tier-1 (2+ entries) or evict tier-2; the active stack only gives up tier-2
    /// entries older than its current position, so its recent fine-grained history is kept.
    /// </summary>
    /// <param name="isActive">True for the active tab's stack.</param>
    internal bool CanFreeMemory(bool isActive) =>
        isActive
            ? Math.Min(_tier2.Count, _currentIndex) > 0
            : _tier1.Count > 1 || _tier2.Count > 0;

    /// <summary>
    /// Evicts up to <paramref name="count"/> of the oldest tier-2 entries that lie strictly before
    /// the current position — used for the active tab, whose current entry must survive.
    /// </summary>
    /// <param name="count">Maximum number of entries to evict.</param>
    internal void EvictOldestTier2BeforeCurrent(int count)
    {
        int toRemove = Math.Min(count, Math.Min(_tier2.Count, _currentIndex));
        if (toRemove <= 0)
        {
            return;
        }

        for (int i = 0; i < toRemove; i++)
            _estimatedBytes -= EntryBytes(_tier2[i]);

        _tier2.RemoveRange(0, toRemove);
        _currentIndex -= toRemove;
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

        // Remove all tier-1 bytes from budget
        long tier1Bytes = 0;
        foreach (var e in _tier1) tier1Bytes += EntryBytes(e);
        _estimatedBytes -= tier1Bytes;

        // Sample every 5th entry into tier-2
        for (int i = 0; i < _tier1.Count; i += 5)
        {
            if (_tier2.Count < MaxTier2)
            {
                _tier2.Add(_tier1[i]);
                _estimatedBytes += EntryBytes(_tier1[i]);
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

        for (int i = 0; i < toRemove; i++)
            _estimatedBytes -= EntryBytes(_tier2[i]);

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

        // Remove batch bytes from budget, then add back sampled entries
        for (int i = 0; i < batchSize; i++)
            _estimatedBytes -= EntryBytes(_tier1[i]);

        // Sample every Nth entry from the batch into tier-2
        for (int i = 0; i < batchSize; i += PromotionSampleRate)
        {
            if (_tier2.Count < MaxTier2)
            {
                _tier2.Add(_tier1[i]);
                _estimatedBytes += EntryBytes(_tier1[i]);
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
                for (int i = tier1CurrentPos + 1; i < _tier1.Count; i++)
                    _estimatedBytes -= EntryBytes(_tier1[i]);
                _tier1.RemoveRange(tier1CurrentPos + 1, tier1EntriesAfterCurrent);
            }
        }
        else
        {
            // Current index is in tier-2; remove all tier-1 and truncate tier-2
            foreach (var e in _tier1) _estimatedBytes -= EntryBytes(e);
            _tier1.Clear();
            int tier2EntriesAfterCurrent = _tier2.Count - 1 - _currentIndex;
            if (tier2EntriesAfterCurrent > 0)
            {
                for (int i = _currentIndex + 1; i < _tier2.Count; i++)
                    _estimatedBytes -= EntryBytes(_tier2[i]);
                _tier2.RemoveRange(_currentIndex + 1, tier2EntriesAfterCurrent);
            }
        }
    }
}
