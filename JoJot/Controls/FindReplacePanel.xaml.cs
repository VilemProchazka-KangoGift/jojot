using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using JoJot.Themes;

namespace JoJot.Controls;

/// <summary>
/// EventArgs carrying the current find query and option flags.
/// </summary>
public sealed class FindChangedEventArgs : EventArgs
{
    public string Query { get; init; } = "";
    public bool CaseSensitive { get; init; }
    public bool WholeWord { get; init; }
}

/// <summary>
/// Side panel providing find and replace functionality.
/// Displayed inline (shrinks editor area) rather than overlaying it.
/// </summary>
public partial class FindReplacePanel : UserControl
{
    private bool _caseSensitive;
    private bool _wholeWord;
    private bool _suppressFindChanged;
    private List<int> _matches = [];
    private int _currentMatchIndex = -1;

    // ─── Public events ──────────────────────────────────────────────────

    public event EventHandler? CloseRequested;
    public event EventHandler<FindChangedEventArgs>? FindTextChanged;
    public event EventHandler? FindNextRequested;
    public event EventHandler? FindPreviousRequested;
    public event EventHandler? ReplaceRequested;
    public event EventHandler? ReplaceAllRequested;

    // ─── Public properties ──────────────────────────────────────────────

    public bool CaseSensitive => _caseSensitive;
    public bool WholeWord => _wholeWord;

    public FindReplacePanel()
    {
        InitializeComponent();

        // Wire placeholder and clear button visibility to TextBox text changes
        FindInput.TextChanged += (_, _) =>
        {
            bool empty = string.IsNullOrEmpty(FindInput.Text);
            FindPlaceholder.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            FindClearButton.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        };

        ReplaceInput.TextChanged += (_, _) =>
        {
            bool empty = string.IsNullOrEmpty(ReplaceInput.Text);
            ReplacePlaceholder.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            ReplaceClearButton.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        };
    }

    // ─── Public methods ─────────────────────────────────────────────────

    /// <summary>
    /// Shows the panel and focuses the find input.
    /// </summary>
    public void Show()
    {
        Visibility = Visibility.Visible;
        // On first open (Collapsed → Visible), FindInput isn't focusable until after
        // a layout pass. Input priority runs below Normal, so queued async continuations
        // (e.g. Select() calls) complete before this focus takes.
        Dispatcher.BeginInvoke(() =>
        {
            FindInput.Focus();
            FindInput.SelectAll();
        }, DispatcherPriority.Input);
    }

    /// <summary>
    /// Hides the panel.
    /// </summary>
    public void Hide()
    {
        Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Pre-populates the find input (e.g. from selected editor text).
    /// Does not raise FindTextChanged — caller is responsible for triggering search.
    /// </summary>
    public void SetFindText(string text)
    {
        _suppressFindChanged = true;
        try
        {
            FindInput.Text = text;
            FindInput.CaretIndex = text.Length;
        }
        finally
        {
            _suppressFindChanged = false;
        }
    }

    /// <summary>
    /// Updates the internal match list and current index, refreshing the counter display.
    /// </summary>
    public void UpdateMatches(List<int> matches, int currentIndex)
    {
        _matches = matches;
        _currentMatchIndex = currentIndex;
        UpdateMatchCountDisplay();
    }

    /// <summary>Returns the current text in the find input.</summary>
    public string GetFindText() => FindInput.Text;

    /// <summary>Returns the current text in the replace input.</summary>
    public string GetReplaceText() => ReplaceInput.Text;

    /// <summary>Moves focus to the find input.</summary>
    public void FocusFindInput() => FindInput.Focus();

    // ─── Private helpers ────────────────────────────────────────────────

    private void UpdateMatchCountDisplay()
    {
        if (_matches.Count == 0)
        {
            MatchCountText.Text = string.IsNullOrEmpty(FindInput.Text) ? "" : "No matches";
        }
        else
        {
            MatchCountText.Text = $"{_currentMatchIndex + 1}/{_matches.Count}";
        }
    }

    private void UpdateToggleVisual(Border toggle, bool active)
    {
        toggle.Background = active
            ? (System.Windows.Media.Brush)FindResource(ThemeKeys.Accent)
            : System.Windows.Media.Brushes.Transparent;

        // Use white text on accent background for readability
        var textBlock = (TextBlock)toggle.Child;
        textBlock.Foreground = active
            ? System.Windows.Media.Brushes.White
            : (System.Windows.Media.Brush)FindResource(ThemeKeys.TextPrimary);
    }

    // ─── Click & input handlers ─────────────────────────────────────────

    private void Close_Click(object sender, MouseButtonEventArgs e)
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void CaseToggle_Click(object sender, MouseButtonEventArgs e)
    {
        _caseSensitive = !_caseSensitive;
        UpdateToggleVisual(CaseToggle, _caseSensitive);
        RaiseFindChanged();
    }

    private void WholeWordToggle_Click(object sender, MouseButtonEventArgs e)
    {
        _wholeWord = !_wholeWord;
        UpdateToggleVisual(WholeWordToggle, _wholeWord);
        RaiseFindChanged();
    }

    private void FindInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressFindChanged) return;
        RaiseFindChanged();
    }

    private void FindInput_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) != 0)
        {
            FindPreviousRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            FindNextRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
    }

    private void PreviousMatch_Click(object sender, MouseButtonEventArgs e)
    {
        FindPreviousRequested?.Invoke(this, EventArgs.Empty);
    }

    private void NextMatch_Click(object sender, MouseButtonEventArgs e)
    {
        FindNextRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Replace_Click(object sender, MouseButtonEventArgs e)
    {
        ReplaceRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ReplaceAll_Click(object sender, MouseButtonEventArgs e)
    {
        ReplaceAllRequested?.Invoke(this, EventArgs.Empty);
    }

    private void FindClear_Click(object sender, MouseButtonEventArgs e)
    {
        FindInput.Text = "";
        FindInput.Focus();
    }

    private void ReplaceClear_Click(object sender, MouseButtonEventArgs e)
    {
        ReplaceInput.Text = "";
        ReplaceInput.Focus();
    }

    private void RaiseFindChanged()
    {
        FindTextChanged?.Invoke(this, new FindChangedEventArgs
        {
            Query = FindInput.Text,
            CaseSensitive = _caseSensitive,
            WholeWord = _wholeWord
        });

        // Keep placeholder visibility in sync
        FindPlaceholder.Visibility = string.IsNullOrEmpty(FindInput.Text)
            ? Visibility.Visible : Visibility.Collapsed;
    }
}
