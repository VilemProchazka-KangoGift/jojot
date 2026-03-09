using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using JoJot.Models;

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

    // ─── In-Editor Find Bar ────────────────────────────────────────

    private void ShowEditorFindBar()
    {
        EditorFindBar.Visibility = Visibility.Visible;
        EditorFindInput.Focus();
        EditorFindInput.SelectAll();
    }

    private void HideEditorFindBar()
    {
        EditorFindBar.Visibility = Visibility.Collapsed;
        _findMatches.Clear();
        _currentFindIndex = -1;
        EditorFindCount.Text = "";
        ContentEditor.Focus();
    }

    private void EditorFindInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        string query = EditorFindInput.Text;
        _currentFindIndex = -1;

        if (string.IsNullOrEmpty(query) || _activeTab is null)
        {
            _findMatches.Clear();
            EditorFindCount.Text = "";
            return;
        }

        _findMatches = ViewModels.MainWindowViewModel.FindAllMatches(ContentEditor.Text, query);

        if (_findMatches.Count > 0)
        {
            _currentFindIndex = 0;
            HighlightFindMatch();
        }

        EditorFindCount.Text = ViewModels.MainWindowViewModel.FormatFindCountText(_currentFindIndex, _findMatches.Count);
    }

    private void HighlightFindMatch()
    {
        if (_currentFindIndex < 0 || _currentFindIndex >= _findMatches.Count) return;

        int pos = _findMatches[_currentFindIndex];
        ContentEditor.Select(pos, EditorFindInput.Text.Length);
        var lineIndex = ContentEditor.GetLineIndexFromCharacterIndex(pos);
        if (lineIndex >= 0) ContentEditor.ScrollToLine(lineIndex);

        EditorFindCount.Text = ViewModels.MainWindowViewModel.FormatFindCountText(_currentFindIndex, _findMatches.Count);
    }

    private void EditorFindNext_Click(object sender, MouseButtonEventArgs e)
    {
        if (_findMatches.Count == 0) return;
        _currentFindIndex = ViewModels.MainWindowViewModel.CycleIndex(_currentFindIndex, _findMatches.Count, forward: true);
        HighlightFindMatch();
    }

    private void EditorFindPrevious_Click(object sender, MouseButtonEventArgs e)
    {
        if (_findMatches.Count == 0) return;
        _currentFindIndex = ViewModels.MainWindowViewModel.CycleIndex(_currentFindIndex, _findMatches.Count, forward: false);
        HighlightFindMatch();
    }

    private void EditorFindClose_Click(object sender, MouseButtonEventArgs e) => HideEditorFindBar();

    private void EditorFindInput_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            HideEditorFindBar();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            if (_findMatches.Count > 0)
            {
                bool forward = Keyboard.Modifiers != ModifierKeys.Shift;
                _currentFindIndex = ViewModels.MainWindowViewModel.CycleIndex(_currentFindIndex, _findMatches.Count, forward);
                HighlightFindMatch();
            }
            e.Handled = true;
        }
    }
}
