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
    /// Matches against DisplayLabel and Content.
    /// </summary>
    private bool MatchesSearch(NoteTab tab)
    {
        if (string.IsNullOrEmpty(_searchText)) return true;
        return tab.DisplayLabel.Contains(_searchText, StringComparison.OrdinalIgnoreCase)
            || tab.Content.Contains(_searchText, StringComparison.OrdinalIgnoreCase);
    }

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
        _findMatches.Clear();
        _currentFindIndex = -1;

        if (string.IsNullOrEmpty(query) || _activeTab is null)
        {
            EditorFindCount.Text = "";
            return;
        }

        // Case-insensitive search within current note
        string content = ContentEditor.Text;
        int index = 0;
        while ((index = content.IndexOf(query, index, StringComparison.OrdinalIgnoreCase)) != -1)
        {
            _findMatches.Add(index);
            index += query.Length;
        }

        if (_findMatches.Count > 0)
        {
            _currentFindIndex = 0;
            HighlightFindMatch();
        }

        EditorFindCount.Text = _findMatches.Count > 0
            ? $"{_currentFindIndex + 1}/{_findMatches.Count}"
            : "No matches";
    }

    private void HighlightFindMatch()
    {
        if (_currentFindIndex < 0 || _currentFindIndex >= _findMatches.Count) return;

        int pos = _findMatches[_currentFindIndex];
        ContentEditor.Select(pos, EditorFindInput.Text.Length);
        var lineIndex = ContentEditor.GetLineIndexFromCharacterIndex(pos);
        if (lineIndex >= 0) ContentEditor.ScrollToLine(lineIndex);

        EditorFindCount.Text = $"{_currentFindIndex + 1}/{_findMatches.Count}";
    }

    private void EditorFindNext_Click(object sender, MouseButtonEventArgs e)
    {
        if (_findMatches.Count == 0) return;
        _currentFindIndex = (_currentFindIndex + 1) % _findMatches.Count;
        HighlightFindMatch();
    }

    private void EditorFindPrevious_Click(object sender, MouseButtonEventArgs e)
    {
        if (_findMatches.Count == 0) return;
        _currentFindIndex = (_currentFindIndex - 1 + _findMatches.Count) % _findMatches.Count;
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
            if (Keyboard.Modifiers == ModifierKeys.Shift)
            {
                if (_findMatches.Count > 0)
                {
                    _currentFindIndex = (_currentFindIndex - 1 + _findMatches.Count) % _findMatches.Count;
                    HighlightFindMatch();
                }
            }
            else
            {
                if (_findMatches.Count > 0)
                {
                    _currentFindIndex = (_currentFindIndex + 1) % _findMatches.Count;
                    HighlightFindMatch();
                }
            }
            e.Handled = true;
        }
    }
}
