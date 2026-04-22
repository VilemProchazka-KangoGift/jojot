using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;
using JoJot.Controls;
using JoJot.Models;
using JoJot.Resources;
using JoJot.Services;

namespace JoJot;

public partial class MainWindow
{
    // ─── Tab Search ─────────────────────────────────────────────────────

    private DispatcherTimer? _searchDebounceTimer;

    /// <summary>
    /// Real-time search filtering with 100ms debounce to avoid rebuilding the
    /// entire tab list on every keystroke.
    /// </summary>
    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchText = SearchBox.Text;
        SearchPlaceholder.Visibility = SearchBox.Text.Length > 0
            ? Visibility.Collapsed
            : Visibility.Visible;

        if (_searchDebounceTimer is null)
        {
            _searchDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _searchDebounceTimer.Tick += (_, _) =>
            {
                _searchDebounceTimer.Stop();
                RebuildTabList();
            };
        }
        _searchDebounceTimer.Stop();
        _searchDebounceTimer.Start();
    }

    /// <summary>
    /// Escape clears search and returns focus to editor.
    /// </summary>
    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _searchDebounceTimer?.Stop();
            SearchBox.Text = "";
            _searchText = "";
            SearchPlaceholder.Visibility = Visibility.Visible;
            RebuildTabList();
            Keyboard.Focus(ContentEditor);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Tests whether a tab matches the current search text (case-insensitive).
    /// Delegates to ViewModel.
    /// </summary>
    private bool MatchesSearch(NoteTab tab) => ViewModel.MatchesSearch(tab);

    // ─── Find & Replace Panel ───────────────────────────────────────────

    private List<int> _findMatches = [];
    private int _currentFindIndex = -1;
    private int _findQueryLength;
    private DispatcherTimer? _findDebounceTimer;
    private DispatcherTimer? _findTextChangeDebounce;
    private CancellationTokenSource? _findCts;

    // ─── Highlight adorner ──────────────────────────────────────────────

    private TextBoxHighlightAdorner? _highlightAdorner;
    private DispatcherTimer? _scrollAdornerThrottle;

    private void OnThemeChangedAdornerRefresh(object? sender, EventArgs e)
    {
        _highlightAdorner?.RefreshBrushes();
        _highlightAdorner?.InvalidateVisual();
    }

    /// <summary>
    /// Returns the existing adorner or creates and attaches a new one to ContentEditor.
    /// </summary>
    private TextBoxHighlightAdorner EnsureHighlightAdorner()
    {
        if (_highlightAdorner is null)
        {
            _highlightAdorner = new TextBoxHighlightAdorner(ContentEditor);
            var layer = AdornerLayer.GetAdornerLayer(ContentEditor);
            layer?.Add(_highlightAdorner);
        }
        return _highlightAdorner;
    }

    /// <summary>
    /// Removes the adorner from the AdornerLayer and clears the reference.
    /// </summary>
    private void RemoveHighlightAdorner()
    {
        if (_highlightAdorner is not null)
        {
            var layer = AdornerLayer.GetAdornerLayer(ContentEditor);
            layer?.Remove(_highlightAdorner);
            _highlightAdorner = null;
        }
    }

    /// <summary>
    /// Wires FindReplacePanel events, scroll tracking, and theme-change invalidation for the adorner.
    /// Called from MainWindow constructor after InitializeComponent.
    /// </summary>
    internal void WireUpFindPanelEvents()
    {
        // Subscribe to FindReplacePanel events
        FindReplacePanel.CloseRequested += (_, _) => HideFindPanel();
        FindReplacePanel.FindTextChanged += OnFindTextChanged;
        FindReplacePanel.FindNextRequested += (_, _) => CycleFindMatch(forward: true);
        FindReplacePanel.FindPreviousRequested += (_, _) => CycleFindMatch(forward: false);
        FindReplacePanel.ReplaceRequested += (_, _) => PerformReplace();
        FindReplacePanel.ReplaceAllRequested += (_, _) => PerformReplaceAll();

        // Re-render adorner when TextBox scrolls — throttle to ~30fps to avoid expensive
        // GetRectFromCharacterIndex calls on every frame during rapid scrolling
        ContentEditor.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, _) =>
        {
            if (_highlightAdorner is null) return;

            if (_scrollAdornerThrottle is null)
            {
                _scrollAdornerThrottle = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(32) };
                _scrollAdornerThrottle.Tick += (_, _) =>
                {
                    _scrollAdornerThrottle!.Stop();
                    _highlightAdorner?.InvalidateVisual();
                };
            }

            if (!_scrollAdornerThrottle.IsEnabled)
                _scrollAdornerThrottle.Start();
        }));

        // Refresh cached brushes and re-render adorner when theme changes
        ThemeService.ThemeChanged += OnThemeChangedAdornerRefresh;
    }

    // ─── Panel show/hide ────────────────────────────────────────────────

    private void ShowFindPanel()
    {
        // Close other side panels first (preferences, cleanup, recovery)
        ViewModel.CloseAllSidePanels();

        _findPanelOpen = true;

        if (ContentEditor.SelectionLength > 0)
        {
            FindReplacePanel.SetFindText(ContentEditor.SelectedText);
        }

        FindReplacePanel.Show();

        var query = FindReplacePanel.GetFindText();
        if (!string.IsNullOrEmpty(query))
        {
            _ = RunSearchAsync(query, FindReplacePanel.CaseSensitive, FindReplacePanel.WholeWord);
            RestoreFindFocusAfterSelect();
        }
    }

    // ContentEditor.Select() steals focus — restore at Input priority so it runs after
    // the Normal-priority async continuation that called Select().
    private void RestoreFindFocusAfterSelect() =>
        Dispatcher.BeginInvoke(FindReplacePanel.FocusFindInput, DispatcherPriority.Input);

    private void HideFindPanel()
    {
        _findDebounceTimer?.Stop();
        _findTextChangeDebounce?.Stop();
        CancelPendingSearch();
        _findPanelOpen = false;
        FindReplacePanel.Hide();
        _findMatches.Clear();
        _currentFindIndex = -1;
        _findQueryLength = 0;
        RemoveHighlightAdorner();
        ContentEditor.Focus();
    }

    // ─── Find operations ────────────────────────────────────────────────

    private void OnFindTextChanged(object? sender, FindChangedEventArgs e)
    {
        _findDebounceTimer?.Stop();
        CancelPendingSearch();

        if (string.IsNullOrEmpty(e.Query) || _activeTab is null)
        {
            _findMatches.Clear();
            _findQueryLength = 0;
            _currentFindIndex = -1;
            FindReplacePanel.UpdateMatches(_findMatches, _currentFindIndex);
            _highlightAdorner?.Clear();
            return;
        }

        // Debounce short queries that produce many matches in large texts
        var delay = e.Query.Length switch
        {
            1 => TimeSpan.FromMilliseconds(400),
            2 => TimeSpan.FromMilliseconds(200),
            _ => TimeSpan.Zero
        };
        if (delay == TimeSpan.Zero)
        {
            _ = RunSearchAsync(e.Query, e.CaseSensitive, e.WholeWord);
            return;
        }

        if (_findDebounceTimer is null)
        {
            _findDebounceTimer = new DispatcherTimer();
            _findDebounceTimer.Tick += (_, _) =>
            {
                _findDebounceTimer.Stop();
                if (_findPanelOpen && _activeTab is not null)
                {
                    var q = FindReplacePanel.GetFindText();
                    if (!string.IsNullOrEmpty(q))
                        _ = RunSearchAsync(q, FindReplacePanel.CaseSensitive, FindReplacePanel.WholeWord);
                }
            };
        }
        _findDebounceTimer.Interval = delay;
        _findDebounceTimer.Start();
    }

    /// <summary>
    /// Cancels any in-flight background search.
    /// </summary>
    private void CancelPendingSearch()
    {
        _findCts?.Cancel();
        _findCts?.Dispose();
        _findCts = null;
    }

    /// <summary>
    /// Runs FindAllMatches on a background thread, then marshals results back to the UI thread.
    /// Cancels automatically if a newer search is started before this one completes.
    /// </summary>
    private async Task RunSearchAsync(string query, bool caseSensitive, bool wholeWord)
    {
        CancelPendingSearch();
        var cts = new CancellationTokenSource();
        _findCts = cts;

        // Capture text on UI thread before going to background
        string content = ContentEditor.Text;
        int queryLength = query.Length;

        List<int> matches;
        try
        {
            matches = await Task.Run(
                () => ViewModels.MainWindowViewModel.FindAllMatches(content, query, caseSensitive, wholeWord),
                cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        // Check cancellation after await — a newer search may have started
        if (cts.Token.IsCancellationRequested) return;

        _findMatches = matches;
        _findQueryLength = queryLength;
        _currentFindIndex = _findMatches.Count > 0 ? 0 : -1;

        FindReplacePanel.UpdateMatches(_findMatches, _currentFindIndex);

        if (_findMatches.Count > 0)
        {
            int pos = _findMatches[0];
            ContentEditor.Select(pos, _findQueryLength);
            var lineIndex = ContentEditor.GetLineIndexFromCharacterIndex(pos);
            if (lineIndex >= 0) ContentEditor.ScrollToLine(lineIndex);
        }

        // Defer adorner rendering — if user types another char before this fires, the
        // cancellation token prevents the now-stale render from executing
        var deferToken = cts.Token;
        _ = Dispatcher.BeginInvoke(() =>
        {
            if (!deferToken.IsCancellationRequested)
                EnsureHighlightAdorner().Update(_findMatches, _currentFindIndex, _findQueryLength);
        }, DispatcherPriority.Background);
    }

    private void CycleFindMatch(bool forward)
    {
        if (_findMatches.Count == 0) return;

        _currentFindIndex = ViewModels.MainWindowViewModel.CycleIndex(
            _currentFindIndex, _findMatches.Count, forward);

        int pos = _findMatches[_currentFindIndex];
        ContentEditor.Select(pos, _findQueryLength);
        var lineIndex = ContentEditor.GetLineIndexFromCharacterIndex(pos);
        if (lineIndex >= 0) ContentEditor.ScrollToLine(lineIndex);

        FindReplacePanel.UpdateMatches(_findMatches, _currentFindIndex);
        _highlightAdorner?.Update(_findMatches, _currentFindIndex, _findQueryLength);

        RestoreFindFocusAfterSelect();
    }

    // ─── Replace operations ─────────────────────────────────────────────

    private void PerformReplace()
    {
        if (_findMatches.Count == 0 || _currentFindIndex < 0 || _activeTab is null) return;

        string replacement = FindReplacePanel.GetReplaceText();
        int matchPos = _findMatches[_currentFindIndex];

        // Push undo snapshot before replacement
        var stack = UndoManager.Instance.GetOrCreateStack(_activeTab.Id);
        stack.PushSnapshot(ContentEditor.Text);

        string newContent = ViewModels.MainWindowViewModel.ReplaceSingle(
            ContentEditor.Text, matchPos, _findQueryLength, replacement);

        // Assign via ContentEditor.Text — triggers autosave and TextChanged handler
        _suppressTextChanged = true;
        ContentEditor.Text = newContent;
        _activeTab.Content = newContent;
        _suppressTextChanged = false;

        // Re-run search to update match positions after replacement
        var query = FindReplacePanel.GetFindText();
        if (!string.IsNullOrEmpty(query))
        {
            _ = RunSearchAsync(query, FindReplacePanel.CaseSensitive, FindReplacePanel.WholeWord);
        }
    }

    private void PerformReplaceAll()
    {
        if (_findMatches.Count == 0 || _activeTab is null) return;

        string query = FindReplacePanel.GetFindText();
        string replacement = FindReplacePanel.GetReplaceText();

        // Push undo snapshot BEFORE replacement so Replace All is a single undo action
        var stack = UndoManager.Instance.GetOrCreateStack(_activeTab.Id);
        stack.PushSnapshot(ContentEditor.Text);

        var (newContent, count) = ViewModels.MainWindowViewModel.ReplaceAll(
            ContentEditor.Text, query, replacement,
            FindReplacePanel.CaseSensitive, FindReplacePanel.WholeWord);

        // Assign via ContentEditor.Text — triggers autosave
        _suppressTextChanged = true;
        ContentEditor.Text = newContent;
        _activeTab.Content = newContent;
        _suppressTextChanged = false;

        // Show replacement count via toast with undo
        if (count > 0)
        {
            ShowReplaceAllToast(count);
        }

        // Re-run search to refresh (should find 0 if all replaced)
        if (!string.IsNullOrEmpty(query))
        {
            _ = RunSearchAsync(query, FindReplacePanel.CaseSensitive, FindReplacePanel.WholeWord);
        }
    }

    /// <summary>
    /// Shows a toast with replacement count and an undo button.
    /// </summary>
    private void ShowReplaceAllToast(int count)
    {
        var fmt = LanguageService.Plural(Strings.Toast_Replacements_One, Strings.Toast_Replacements_Few, Strings.Toast_Replacements, count);
        string msg = string.Format(fmt, count);
        _pendingToastUndoAction = () => PerformUndo();
        ShowUndoableToast(msg);
    }

    /// <summary>
    /// Re-searches in the new active tab content when the find panel is open.
    /// Call this from tab switch logic after loading new tab content.
    /// </summary>
    internal void RefreshFindIfPanelOpen()
    {
        if (!_findPanelOpen) return;

        var query = FindReplacePanel.GetFindText();
        if (!string.IsNullOrEmpty(query))
        {
            _ = RunSearchAsync(query, FindReplacePanel.CaseSensitive, FindReplacePanel.WholeWord);
        }
    }

    /// <summary>
    /// Re-runs find search when editor content changes (typing, undo/redo).
    /// Called from ContentEditor_TextChanged and PerformUndo/PerformRedo.
    /// Debounces at 150ms to avoid spawning a Task.Run per keystroke.
    /// </summary>
    internal void RefreshFindOnTextChange()
    {
        if (!_findPanelOpen) return;

        var query = FindReplacePanel.GetFindText();
        if (string.IsNullOrEmpty(query))
        {
            _findTextChangeDebounce?.Stop();
            CancelPendingSearch();
            _highlightAdorner?.Clear();
            FindReplacePanel.UpdateMatches([], -1);
            return;
        }

        if (_findTextChangeDebounce is null)
        {
            _findTextChangeDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            _findTextChangeDebounce.Tick += (_, _) =>
            {
                _findTextChangeDebounce.Stop();
                if (_findPanelOpen && _activeTab is not null)
                {
                    var q = FindReplacePanel.GetFindText();
                    if (!string.IsNullOrEmpty(q))
                        _ = RefreshFindOnTextChangeAsync(q);
                }
            };
        }
        _findTextChangeDebounce.Stop();
        _findTextChangeDebounce.Start();
    }

    private async Task RefreshFindOnTextChangeAsync(string query)
    {
        CancelPendingSearch();
        var cts = new CancellationTokenSource();
        _findCts = cts;

        string content = ContentEditor.Text;
        bool caseSensitive = FindReplacePanel.CaseSensitive;
        bool wholeWord = FindReplacePanel.WholeWord;
        int savedIndex = _currentFindIndex;

        List<int> matches;
        try
        {
            matches = await Task.Run(
                () => ViewModels.MainWindowViewModel.FindAllMatches(content, query, caseSensitive, wholeWord),
                cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (cts.Token.IsCancellationRequested) return;

        _findMatches = matches;
        _findQueryLength = query.Length;

        if (_findMatches.Count == 0)
            _currentFindIndex = -1;
        else if (savedIndex >= _findMatches.Count)
            _currentFindIndex = _findMatches.Count - 1;
        else if (savedIndex < 0)
            _currentFindIndex = 0;
        else
            _currentFindIndex = savedIndex;

        FindReplacePanel.UpdateMatches(_findMatches, _currentFindIndex);

        // Defer adorner update to after WPF layout pass (GetRectFromCharacterIndex needs updated layout)
        _ = Dispatcher.BeginInvoke(() =>
        {
            if (!cts.Token.IsCancellationRequested)
                EnsureHighlightAdorner().Update(_findMatches, _currentFindIndex, _findQueryLength);
        }, DispatcherPriority.Loaded);
    }
}
