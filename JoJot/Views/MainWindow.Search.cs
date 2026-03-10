using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using JoJot.Controls;
using JoJot.Models;
using JoJot.Services;

namespace JoJot;

public partial class MainWindow
{
    // ─── Tab Search ─────────────────────────────────────────────────────────

    /// <summary>
    /// Real-time search filtering. Rebuilds the tab list hiding non-matches.
    /// </summary>
    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchText = SearchBox.Text;
        SearchPlaceholder.Visibility = SearchBox.Text.Length > 0
            ? Visibility.Collapsed
            : Visibility.Visible;
        RebuildTabList();
    }

    /// <summary>
    /// Escape clears search and returns focus to editor.
    /// </summary>
    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
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

    // ─── Find & Replace Panel ────────────────────────────────────────────────

    private List<int> _findMatches = [];
    private int _currentFindIndex = -1;
    private int _findQueryLength;

    // ─── Highlight adorner ────────────────────────────────────────────────────

    private TextBoxHighlightAdorner? _highlightAdorner;

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

        // Re-render adorner when TextBox scrolls so highlights track content position
        ContentEditor.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, _) =>
        {
            _highlightAdorner?.InvalidateVisual();
        }));

        // Re-render adorner when theme changes so brush colors update immediately
        ThemeService.ThemeChanged += (_, _) =>
        {
            _highlightAdorner?.InvalidateVisual();
        };
    }

    // ─── Panel show/hide ────────────────────────────────────────────────────

    private void ShowFindPanel(bool showReplace = false)
    {
        // Close other side panels first (preferences, cleanup, recovery)
        ViewModel.CloseAllSidePanels();

        _findPanelOpen = true;

        // Auto-populate find input from editor selection
        if (ContentEditor.SelectionLength > 0)
        {
            FindReplacePanel.SetFindText(ContentEditor.SelectedText);
        }

        FindReplacePanel.Show(showReplace);

        // If panel already has query text, trigger re-search now
        var query = FindReplacePanel.GetFindText();
        if (!string.IsNullOrEmpty(query))
        {
            RunSearch(query, FindReplacePanel.CaseSensitive, FindReplacePanel.WholeWord);
        }
    }

    private void HideFindPanel()
    {
        _findPanelOpen = false;
        FindReplacePanel.Hide();
        _findMatches.Clear();
        _currentFindIndex = -1;
        _findQueryLength = 0;
        RemoveHighlightAdorner();
        ContentEditor.Focus();
    }

    // ─── Find operations ────────────────────────────────────────────────────

    private void OnFindTextChanged(object? sender, FindChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Query) || _activeTab is null)
        {
            _findMatches.Clear();
            _findQueryLength = 0;
            _currentFindIndex = -1;
            FindReplacePanel.UpdateMatches(_findMatches, _currentFindIndex);
            _highlightAdorner?.Clear();
            return;
        }

        RunSearch(e.Query, e.CaseSensitive, e.WholeWord);
    }

    /// <summary>
    /// Runs a search on the current editor content and updates matches, adorner, and panel counter.
    /// </summary>
    private void RunSearch(string query, bool caseSensitive, bool wholeWord)
    {
        _findMatches = ViewModels.MainWindowViewModel.FindAllMatches(
            ContentEditor.Text, query, caseSensitive, wholeWord);
        _findQueryLength = query.Length;
        _currentFindIndex = _findMatches.Count > 0 ? 0 : -1;

        FindReplacePanel.UpdateMatches(_findMatches, _currentFindIndex);

        if (_findMatches.Count > 0)
        {
            // Scroll to and select first match
            int pos = _findMatches[0];
            ContentEditor.Select(pos, _findQueryLength);
            var lineIndex = ContentEditor.GetLineIndexFromCharacterIndex(pos);
            if (lineIndex >= 0) ContentEditor.ScrollToLine(lineIndex);
        }

        // Update adorner with all match positions
        EnsureHighlightAdorner().Update(_findMatches, _currentFindIndex, _findQueryLength);
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
    }

    // ─── Replace operations ────────────────────────────────────────────────

    private void PerformReplace()
    {
        if (_findMatches.Count == 0 || _currentFindIndex < 0 || _activeTab is null) return;

        string replacement = FindReplacePanel.GetReplaceText();
        int matchPos = _findMatches[_currentFindIndex];

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
            RunSearch(query, FindReplacePanel.CaseSensitive, FindReplacePanel.WholeWord);
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

        // Show replacement count feedback
        FindReplacePanel.ShowReplaceCount(count);

        // Re-run search to refresh (should find 0 if all replaced)
        if (!string.IsNullOrEmpty(query))
        {
            RunSearch(query, FindReplacePanel.CaseSensitive, FindReplacePanel.WholeWord);
        }
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
            RunSearch(query, FindReplacePanel.CaseSensitive, FindReplacePanel.WholeWord);
        }
    }
}
