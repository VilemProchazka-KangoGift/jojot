using System.Collections.ObjectModel;
using System.Collections.Specialized;
using JoJot.Models;

namespace JoJot.ViewModels;

/// <summary>
/// Core state for a per-desktop MainWindow.
/// Owns the tab collection, active tab, search text, and derived properties.
/// </summary>
public class MainWindowViewModel : ObservableObject
{
    private NoteTab? _activeTab;
    private string _searchText = "";
    private string _desktopGuid;
    private string? _desktopName;
    private int? _desktopIndex;

    public MainWindowViewModel(string desktopGuid)
    {
        _desktopGuid = desktopGuid;
        Tabs.CollectionChanged += OnTabsCollectionChanged;
    }

    /// <summary>
    /// All tabs for this desktop, ordered by pinned-first then sort_order.
    /// </summary>
    public ObservableCollection<NoteTab> Tabs { get; } = [];

    /// <summary>
    /// The currently selected tab, or null if none.
    /// </summary>
    public NoteTab? ActiveTab
    {
        get => _activeTab;
        set => SetProperty(ref _activeTab, value);
    }

    /// <summary>
    /// Virtual desktop GUID this window is bound to.
    /// </summary>
    public string DesktopGuid
    {
        get => _desktopGuid;
        set => SetProperty(ref _desktopGuid, value);
    }

    /// <summary>
    /// Tab search filter text. Setting this recomputes FilteredTabs.
    /// </summary>
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
                OnPropertyChanged(nameof(FilteredTabs));
        }
    }

    /// <summary>
    /// Tabs filtered by SearchText. Matches against DisplayLabel and Content (case-insensitive).
    /// Returns all tabs when SearchText is empty.
    /// </summary>
    public IReadOnlyList<NoteTab> FilteredTabs
    {
        get
        {
            if (string.IsNullOrEmpty(_searchText))
                return Tabs;

            return Tabs.Where(t => MatchesSearch(t)).ToList();
        }
    }

    /// <summary>
    /// Window title derived from desktop name/index.
    /// </summary>
    public string WindowTitle => FormatWindowTitle(_desktopName, _desktopIndex);

    /// <summary>
    /// Updates the desktop identity and recomputes WindowTitle.
    /// </summary>
    public void UpdateDesktopInfo(string? desktopName, int? desktopIndex)
    {
        _desktopName = desktopName;
        _desktopIndex = desktopIndex;
        OnPropertyChanged(nameof(WindowTitle));
    }

    /// <summary>
    /// Tests whether a tab matches the current search text (case-insensitive).
    /// Matches against DisplayLabel and Content.
    /// </summary>
    internal bool MatchesSearch(NoteTab tab)
    {
        if (string.IsNullOrEmpty(_searchText)) return true;
        return tab.DisplayLabel.Contains(_searchText, StringComparison.OrdinalIgnoreCase)
            || tab.Content.Contains(_searchText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Formats window title per VDSK-06: "JoJot — Name", "JoJot — Desktop N", or "JoJot".
    /// </summary>
    internal static string FormatWindowTitle(string? desktopName, int? desktopIndex)
    {
        if (!string.IsNullOrEmpty(desktopName))
            return $"JoJot \u2014 {desktopName}";

        if (desktopIndex.HasValue)
            return $"JoJot \u2014 Desktop {desktopIndex.Value + 1}";

        return "JoJot";
    }

    // ─── Tab CRUD Logic ────────────────────────────────────────────

    /// <summary>
    /// Computes where a new tab should be inserted and what sort_order to use.
    /// New tabs go right after the last pinned tab (first unpinned position).
    /// If the first unpinned tab is already a placeholder, returns it instead of creating a new one.
    /// </summary>
    /// <returns>
    /// (existingPlaceholder, insertIndex, sortOrder) — if existingPlaceholder is not null,
    /// the caller should focus it instead of creating a new tab.
    /// </returns>
    internal (NoteTab? existingPlaceholder, int insertIndex, int sortOrder) GetNewTabPosition()
    {
        int pinnedCount = Tabs.Count(t => t.Pinned);

        // If first unpinned tab is an empty placeholder, reuse it
        if (pinnedCount < Tabs.Count)
        {
            var firstUnpinned = Tabs[pinnedCount];
            if (firstUnpinned.IsPlaceholder)
                return (firstUnpinned, -1, 0);
        }

        int minUnpinnedSort = Tabs.Where(t => !t.Pinned)
            .Select(t => t.SortOrder).DefaultIfEmpty(0).Min();

        return (null, pinnedCount, minUnpinnedSort - 1);
    }

    /// <summary>
    /// Adds a newly-created tab at the correct position in the collection.
    /// </summary>
    internal void InsertNewTab(NoteTab tab, int insertIndex)
    {
        if (insertIndex >= Tabs.Count)
            Tabs.Add(tab);
        else
            Tabs.Insert(insertIndex, tab);
    }

    /// <summary>
    /// Removes a tab from the collection and returns the focus cascade target.
    /// Does NOT delete from database — caller handles that.
    /// </summary>
    /// <returns>The tab to select next, or null if the collection is now empty.</returns>
    internal NoteTab? RemoveTab(NoteTab tab)
    {
        int originalIndex = Tabs.IndexOf(tab);
        bool wasActive = (ActiveTab?.Id == tab.Id);

        Tabs.Remove(tab);

        if (!wasActive) return null; // No focus change needed

        return GetFocusCascadeTarget(originalIndex);
    }

    /// <summary>
    /// Removes multiple tabs (skipping pinned) and returns the focus cascade target.
    /// </summary>
    /// <returns>
    /// (removed tabs, original indexes, focus target) — focus target is null if active tab wasn't among removed.
    /// </returns>
    internal (List<NoteTab> removed, List<int> originalIndexes, NoteTab? focusTarget) RemoveMultiple(IEnumerable<NoteTab> candidates)
    {
        var toDelete = candidates.Where(t => !t.Pinned).ToList();
        if (toDelete.Count == 0) return ([], [], null);

        var originalIndexes = toDelete.Select(t => Tabs.IndexOf(t)).ToList();
        bool wasActive = ActiveTab is not null && toDelete.Any(t => t.Id == ActiveTab.Id);
        int activeOriginalIndex = wasActive ? Tabs.IndexOf(ActiveTab!) : 0;

        foreach (var tab in toDelete)
            Tabs.Remove(tab);

        NoteTab? focusTarget = wasActive ? GetFocusCascadeTarget(activeOriginalIndex) : null;
        return (toDelete, originalIndexes, focusTarget);
    }

    /// <summary>
    /// Restores previously deleted tabs to their original positions.
    /// </summary>
    internal void RestoreTabs(List<NoteTab> tabs, List<int> originalIndexes)
    {
        var pairs = tabs.Zip(originalIndexes, (tab, idx) => (tab, idx))
                        .OrderBy(p => p.idx)
                        .ToList();

        foreach (var (tab, originalIndex) in pairs)
        {
            int insertAt = Math.Min(originalIndex, Tabs.Count);
            Tabs.Insert(insertAt, tab);
        }
    }

    /// <summary>
    /// Focus cascade after deleting the active tab:
    /// 1. First visible tab at or below the deleted position
    /// 2. Last visible tab (if no tab below)
    /// 3. Null if no visible tabs exist (caller should clear search or create new tab)
    /// </summary>
    internal NoteTab? GetFocusCascadeTarget(int deletedIndex)
    {
        var visible = Tabs.Where(t => MatchesSearch(t)).ToList();
        if (visible.Count == 0) return null;

        // First visible tab whose position >= deletedIndex
        foreach (var t in visible)
        {
            if (Tabs.IndexOf(t) >= deletedIndex)
                return t;
        }

        // Fallback to last visible tab
        return visible[^1];
    }

    /// <summary>
    /// Re-sorts the collection after toggling a tab's pin state.
    /// Pinned tabs sort to top, then by sort_order.
    /// Reassigns sort_order values to match new positions.
    /// </summary>
    internal void ReorderAfterPinToggle()
    {
        var sorted = Tabs.OrderByDescending(t => t.Pinned).ThenBy(t => t.SortOrder).ToList();
        Tabs.Clear();
        foreach (var t in sorted) Tabs.Add(t);

        for (int i = 0; i < Tabs.Count; i++)
            Tabs[i].SortOrder = i;
    }

    /// <summary>
    /// Computes insert position and sort_order for cloning a tab.
    /// Clone goes immediately after the source. Shifts subsequent sort_orders.
    /// </summary>
    internal (int insertIndex, int sortOrder) GetClonePosition(NoteTab source)
    {
        int newSortOrder = source.SortOrder + 1;

        // Shift sort_order of tabs at or after the clone position
        foreach (var tab in Tabs.Where(t => t.SortOrder >= newSortOrder))
            tab.SortOrder++;

        int insertIndex = Tabs.IndexOf(source) + 1;
        return (insertIndex, newSortOrder);
    }

    // ─── Tab Reorder ───────────────────────────────────────────────

    /// <summary>
    /// Moves a tab from oldIndex to newIndex in the collection, respecting pin zones.
    /// Adjusts for removal-shift (when moving forward, target index decrements by 1).
    /// Reassigns SortOrder values to match new positions.
    /// Returns true if the move was performed.
    /// </summary>
    internal bool MoveTab(int oldIndex, int newIndex)
    {
        if (oldIndex < 0 || oldIndex >= Tabs.Count) return false;
        if (newIndex < 0 || newIndex > Tabs.Count) return false;
        if (oldIndex == newIndex) return false;

        var tab = Tabs[oldIndex];

        // Pin-zone enforcement: can't move pinned into unpinned zone or vice versa
        int targetCheckIndex = newIndex > oldIndex ? newIndex - 1 : newIndex;
        if (targetCheckIndex >= 0 && targetCheckIndex < Tabs.Count)
        {
            var targetZoneTab = Tabs[targetCheckIndex];
            if (targetZoneTab.Pinned != tab.Pinned)
                return false;
        }

        // Adjust for removal shift
        if (newIndex > oldIndex) newIndex--;

        if (oldIndex == newIndex) return false;

        Tabs.Move(oldIndex, newIndex);

        // Reassign sort_order to match new positions
        for (int i = 0; i < Tabs.Count; i++)
            Tabs[i].SortOrder = i;

        return true;
    }

    // ─── Find Engine ────────────────────────────────────────────────

    /// <summary>
    /// Finds all occurrences of <paramref name="query"/> in <paramref name="content"/>.
    /// Returns a list of starting indices. Non-overlapping matches only.
    /// Default behavior is case-insensitive with no word-boundary enforcement.
    /// </summary>
    internal static List<int> FindAllMatches(string content, string query, bool caseSensitive = false, bool wholeWord = false)
    {
        var matches = new List<int>();
        if (string.IsNullOrEmpty(query) || string.IsNullOrEmpty(content))
            return matches;

        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        int index = 0;
        while ((index = content.IndexOf(query, index, comparison)) != -1)
        {
            if (!wholeWord || IsWholeWordMatch(content, index, query.Length))
                matches.Add(index);

            index += query.Length;
        }
        return matches;
    }

    /// <summary>
    /// Tests whether a match at <paramref name="matchIndex"/> of <paramref name="matchLength"/> characters
    /// in <paramref name="content"/> falls on word boundaries (non-alphanumeric or string edge on both sides).
    /// </summary>
    private static bool IsWholeWordMatch(string content, int matchIndex, int matchLength)
    {
        bool startOk = matchIndex == 0 || !char.IsLetterOrDigit(content[matchIndex - 1]);
        bool endOk = (matchIndex + matchLength) >= content.Length || !char.IsLetterOrDigit(content[matchIndex + matchLength]);
        return startOk && endOk;
    }

    /// <summary>
    /// Replaces all occurrences of <paramref name="query"/> in <paramref name="content"/> with
    /// <paramref name="replacement"/>. Supports case-sensitive and whole-word options.
    /// Returns the new content and the number of replacements made.
    /// </summary>
    internal static (string NewContent, int Count) ReplaceAll(string content, string query, string replacement, bool caseSensitive = false, bool wholeWord = false)
    {
        var positions = FindAllMatches(content, query, caseSensitive, wholeWord);
        if (positions.Count == 0)
            return (content, 0);

        // Build result by iterating forward through positions
        var sb = new System.Text.StringBuilder(content.Length);
        int lastEnd = 0;
        foreach (int pos in positions)
        {
            sb.Append(content, lastEnd, pos - lastEnd);
            sb.Append(replacement);
            lastEnd = pos + query.Length;
        }
        sb.Append(content, lastEnd, content.Length - lastEnd);

        return (sb.ToString(), positions.Count);
    }

    /// <summary>
    /// Replaces the match at the given index with <paramref name="replacement"/>.
    /// Simple string surgery: content[..matchIndex] + replacement + content[(matchIndex + queryLength)..].
    /// </summary>
    internal static string ReplaceSingle(string content, int matchIndex, int queryLength, string replacement)
    {
        return content[..matchIndex] + replacement + content[(matchIndex + queryLength)..];
    }

    /// <summary>
    /// Cycles a match index forward or backward with wrapping.
    /// Returns -1 if total is 0.
    /// </summary>
    internal static int CycleIndex(int current, int total, bool forward)
    {
        if (total == 0) return -1;
        return forward
            ? (current + 1) % total
            : (current - 1 + total) % total;
    }

    /// <summary>
    /// Formats the find bar count text (e.g. "1/5" or "No matches").
    /// </summary>
    internal static string FormatFindCountText(int currentIndex, int totalMatches)
    {
        return totalMatches > 0
            ? $"{currentIndex + 1}/{totalMatches}"
            : "No matches";
    }

    // ─── Font Size ──────────────────────────────────────────────────

    internal const int FontSizeMin = 8;
    internal const int FontSizeMax = 32;
    internal const int FontSizeDefault = 13;

    /// <summary>
    /// Parses a stored font size preference string, clamping to [8, 32].
    /// Returns 13 (default) if the value is null, empty, or non-numeric.
    /// </summary>
    internal static int ParseFontSize(string? saved) =>
        int.TryParse(saved, out var fs) ? Math.Clamp(fs, FontSizeMin, FontSizeMax) : FontSizeDefault;

    /// <summary>
    /// Computes a new font size by applying a delta, clamped to [8, 32].
    /// </summary>
    internal static int ClampFontSize(int current, int delta) =>
        Math.Clamp(current + delta, FontSizeMin, FontSizeMax);

    // ─── Editor State ──────────────────────────────────────────────

    private bool _isRestoringContent;

    /// <summary>
    /// True when programmatically setting editor content (suppresses autosave triggers).
    /// Replaces the old _suppressTextChanged field.
    /// </summary>
    public bool IsRestoringContent
    {
        get => _isRestoringContent;
        set => SetProperty(ref _isRestoringContent, value);
    }

    /// <summary>
    /// Saves current editor state to the active tab model before switching away.
    /// Returns false if there's no active tab.
    /// </summary>
    internal bool SaveEditorStateToTab(string editorContent, int caretIndex, int scrollOffset)
    {
        if (ActiveTab is null) return false;

        bool contentChanged = ActiveTab.Content != editorContent;
        ActiveTab.Content = editorContent;
        ActiveTab.CursorPosition = caretIndex;
        ActiveTab.EditorScrollOffset = scrollOffset;
        if (contentChanged)
            ActiveTab.UpdatedAt = DateTime.Now;

        return contentChanged;
    }

    /// <summary>
    /// Generates a default filename for Save As.
    /// Priority: tab name → first 30 chars of content → "JoJot note YYYY-MM-DD".
    /// </summary>
    internal static string GetDefaultFilename(NoteTab tab)
    {
        if (!string.IsNullOrWhiteSpace(tab.Name))
            return SanitizeFilename(tab.Name) + ".txt";

        if (!string.IsNullOrWhiteSpace(tab.Content))
        {
            string preview = tab.Content.Trim();
            if (preview.Length > 30)
                preview = preview[..30];
            return SanitizeFilename(preview) + ".txt";
        }

        return $"JoJot note {DateTime.Now:yyyy-MM-dd}.txt";
    }

    /// <summary>
    /// Removes characters illegal in Windows filenames.
    /// </summary>
    internal static string SanitizeFilename(string name)
    {
        char[] invalid = System.IO.Path.GetInvalidFileNameChars();
        var sanitized = new System.Text.StringBuilder(name.Length);
        foreach (char c in name)
        {
            if (Array.IndexOf(invalid, c) < 0)
                sanitized.Append(c);
            else
                sanitized.Append('_');
        }
        string result = sanitized.ToString().TrimEnd('.', ' ');
        return string.IsNullOrWhiteSpace(result) ? "JoJot note" : result;
    }

    private void OnTabsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(FilteredTabs));
    }

    // ─── Panel State ────────────────────────────────────────────────

    private bool _isPreferencesOpen;
    private bool _isCleanupOpen;
    private bool _isRecoveryOpen;
    private bool _isHelpOpen;
    private bool _isFindPanelOpen;

    /// <summary>
    /// Whether the preferences side panel is open.
    /// </summary>
    public bool IsPreferencesOpen
    {
        get => _isPreferencesOpen;
        set => SetProperty(ref _isPreferencesOpen, value);
    }

    /// <summary>
    /// Whether the cleanup side panel is open.
    /// </summary>
    public bool IsCleanupOpen
    {
        get => _isCleanupOpen;
        set => SetProperty(ref _isCleanupOpen, value);
    }

    /// <summary>
    /// Whether the recovery side panel is open.
    /// </summary>
    public bool IsRecoveryOpen
    {
        get => _isRecoveryOpen;
        set => SetProperty(ref _isRecoveryOpen, value);
    }

    /// <summary>
    /// Whether the help overlay is visible.
    /// </summary>
    public bool IsHelpOpen
    {
        get => _isHelpOpen;
        set => SetProperty(ref _isHelpOpen, value);
    }

    /// <summary>
    /// Whether the find/replace side panel is open.
    /// </summary>
    public bool IsFindPanelOpen
    {
        get => _isFindPanelOpen;
        set => SetProperty(ref _isFindPanelOpen, value);
    }

    /// <summary>
    /// Closes all side panels (preferences, cleanup, recovery, find).
    /// Help overlay is not a side panel and is excluded.
    /// </summary>
    internal void CloseAllSidePanels()
    {
        IsPreferencesOpen = false;
        IsCleanupOpen = false;
        IsRecoveryOpen = false;
        IsFindPanelOpen = false;
    }

    // ─── Cleanup Logic ──────────────────────────────────────────────

    /// <summary>
    /// Computes the cutoff DateTime for cleanup filtering.
    /// Returns null if age is less than 1.
    /// </summary>
    /// <param name="age">Number of time units.</param>
    /// <param name="unitIndex">0=days, 1=hours, 2=weeks, 3=months.</param>
    /// <param name="now">Current time (pass DateTime.Now; explicit for testability).</param>
    internal static DateTime? GetCleanupCutoffDate(int age, int unitIndex, DateTime now)
    {
        if (age < 1) return null;

        var span = unitIndex switch
        {
            0 => TimeSpan.FromDays(age),        // days
            1 => TimeSpan.FromHours(age),       // hours
            2 => TimeSpan.FromDays(age * 7),    // weeks
            3 => TimeSpan.FromDays(age * 30),   // months (approximate)
            _ => TimeSpan.FromDays(age)
        };

        return now - span;
    }

    /// <summary>
    /// Returns tabs matching the cleanup filter criteria.
    /// </summary>
    internal List<NoteTab> GetCleanupCandidates(DateTime cutoff, bool includePinned)
    {
        return Tabs
            .Where(t => t.UpdatedAt < cutoff && (includePinned || !t.Pinned))
            .ToList();
    }

    /// <summary>
    /// Extracts a ~50 char content excerpt for cleanup preview rows.
    /// Returns empty if tab has no custom name (DisplayLabel already shows content).
    /// </summary>
    internal static string GetCleanupExcerpt(NoteTab tab)
    {
        if (string.IsNullOrWhiteSpace(tab.Content))
            return "";

        string content = tab.Content.Trim().Replace('\n', ' ').Replace('\r', ' ');

        // If tab has a custom name, show content excerpt
        if (!string.IsNullOrWhiteSpace(tab.Name))
        {
            return content.Length > 50 ? content[..50] + "..." : content;
        }

        // No custom name — DisplayLabel already shows first 30 chars of content
        return "";
    }

    // ─── Desktop Drag State ─────────────────────────────────────────

    private bool _isDragOverlayActive;
    private string? _dragFromDesktopGuid;
    private string? _dragToDesktopGuid;
    private string? _dragToDesktopName;
    private bool _isMisplaced;

    /// <summary>
    /// Whether the drag resolution overlay is currently showing.
    /// </summary>
    public bool IsDragOverlayActive
    {
        get => _isDragOverlayActive;
        set => SetProperty(ref _isDragOverlayActive, value);
    }

    /// <summary>
    /// Origin desktop GUID when a window drag is in progress.
    /// </summary>
    public string? DragFromDesktopGuid
    {
        get => _dragFromDesktopGuid;
        set => SetProperty(ref _dragFromDesktopGuid, value);
    }

    /// <summary>
    /// Target desktop GUID when a window drag is in progress.
    /// </summary>
    public string? DragToDesktopGuid
    {
        get => _dragToDesktopGuid;
        set => SetProperty(ref _dragToDesktopGuid, value);
    }

    /// <summary>
    /// Target desktop display name when a window drag is in progress.
    /// </summary>
    public string? DragToDesktopName
    {
        get => _dragToDesktopName;
        set => SetProperty(ref _dragToDesktopName, value);
    }

    /// <summary>
    /// Whether this window's stored desktop GUID doesn't match where it's currently located.
    /// </summary>
    public bool IsMisplaced
    {
        get => _isMisplaced;
        set => SetProperty(ref _isMisplaced, value);
    }

    /// <summary>
    /// Actions the UI should take when a window drag is detected.
    /// </summary>
    internal enum DragAction
    {
        /// <summary>Window moved back to original desktop — dismiss overlay.</summary>
        Dismiss,
        /// <summary>Same target as current — do nothing.</summary>
        NoOp,
        /// <summary>Different target while overlay is already showing — update in place.</summary>
        UpdateTarget,
        /// <summary>First drag detection — show overlay.</summary>
        ShowNew
    }

    /// <summary>
    /// Evaluates what action to take when a window drag to a new desktop is detected.
    /// Pure logic — no side effects.
    /// </summary>
    internal DragAction EvaluateDrag(string toGuid)
    {
        if (IsDragOverlayActive)
        {
            if (toGuid.Equals(DragFromDesktopGuid, StringComparison.OrdinalIgnoreCase))
                return DragAction.Dismiss;
            if (toGuid.Equals(DragToDesktopGuid, StringComparison.OrdinalIgnoreCase))
                return DragAction.NoOp;
            return DragAction.UpdateTarget;
        }
        return DragAction.ShowNew;
    }

    /// <summary>
    /// Sets initial drag state when a new drag begins.
    /// </summary>
    internal void BeginDrag(string fromGuid, string toGuid, string toName)
    {
        IsDragOverlayActive = true;
        DragFromDesktopGuid = fromGuid;
        DragToDesktopGuid = toGuid;
        DragToDesktopName = toName;
    }

    /// <summary>
    /// Updates the drag target when the window is moved to a different desktop
    /// while the overlay is already showing.
    /// </summary>
    internal void UpdateDragTarget(string toGuid, string toName)
    {
        DragToDesktopGuid = toGuid;
        DragToDesktopName = toName;
    }

    /// <summary>
    /// Clears all drag state after resolution (keep/merge/cancel/dismiss).
    /// </summary>
    internal void ResetDragState()
    {
        IsDragOverlayActive = false;
        DragFromDesktopGuid = null;
        DragToDesktopGuid = null;
        DragToDesktopName = null;
    }

    /// <summary>
    /// Tests whether this window is on a different desktop than expected.
    /// </summary>
    internal bool IsMisplacedOnDesktop(string currentDesktopGuid)
    {
        return !currentDesktopGuid.Equals(DesktopGuid, StringComparison.OrdinalIgnoreCase);
    }
}
