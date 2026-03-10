using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace JoJot.Controls;

/// <summary>
/// EventArgs carrying the current find query and option flags.
/// </summary>
public class FindChangedEventArgs : EventArgs
{
    public string Query { get; init; } = "";
    public bool CaseSensitive { get; init; }
    public bool WholeWord { get; init; }
}

/// <summary>
/// Side panel providing find and optional find/replace functionality.
/// Follows the PreferencesPanel show/hide animation pattern.
/// </summary>
public partial class FindReplacePanel : UserControl
{
    private bool _caseSensitive;
    private bool _wholeWord;
    private bool _replaceVisible;
    private List<int> _matches = [];
    private int _currentMatchIndex = -1;

    // ── Public events ──

    public event EventHandler? CloseRequested;
    public event EventHandler<FindChangedEventArgs>? FindTextChanged;
    public event EventHandler? FindNextRequested;
    public event EventHandler? FindPreviousRequested;
    public event EventHandler? ReplaceRequested;
    public event EventHandler? ReplaceAllRequested;

    // ── Public properties ──

    public bool CaseSensitive => _caseSensitive;
    public bool WholeWord => _wholeWord;
    public bool IsReplaceVisible => _replaceVisible;

    public FindReplacePanel()
    {
        InitializeComponent();

        // Wire placeholder visibility to TextBox text changes
        FindInput.TextChanged += (_, _) =>
            FindPlaceholder.Visibility = string.IsNullOrEmpty(FindInput.Text)
                ? Visibility.Visible : Visibility.Collapsed;

        ReplaceInput.TextChanged += (_, _) =>
            ReplacePlaceholder.Visibility = string.IsNullOrEmpty(ReplaceInput.Text)
                ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── Public methods ──

    /// <summary>
    /// Shows the panel, optionally revealing the replace row.
    /// Runs slide-in animation (320 -> 0, 250ms, CubicEase EaseOut) and focuses the find input.
    /// </summary>
    public void Show(bool showReplace = false)
    {
        _replaceVisible = showReplace;
        UpdateReplaceRowVisibility();

        PanelTitle.Text = showReplace ? "Find & Replace" : "Find";

        Visibility = Visibility.Visible;

        var anim = new DoubleAnimation
        {
            From = 320, To = 0,
            Duration = TimeSpan.FromMilliseconds(250),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        PanelTransform.BeginAnimation(TranslateTransform.XProperty, anim);

        FindInput.Focus();
        FindInput.SelectAll();
    }

    /// <summary>
    /// Slides the panel out (0 -> 320, 200ms, CubicEase EaseIn), then collapses it.
    /// </summary>
    public void Hide()
    {
        var anim = new DoubleAnimation
        {
            From = 0, To = 320,
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        anim.Completed += (_, _) =>
        {
            Visibility = Visibility.Collapsed;
            PanelTransform.BeginAnimation(TranslateTransform.XProperty, null);
            PanelTransform.X = 320;
        };
        PanelTransform.BeginAnimation(TranslateTransform.XProperty, anim);
    }

    /// <summary>
    /// Pre-populates the find input (e.g. from selected editor text).
    /// Does not raise FindTextChanged — caller is responsible for triggering search.
    /// </summary>
    public void SetFindText(string text)
    {
        FindInput.Text = text;
        FindInput.CaretIndex = text.Length;
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

    // ── Private helpers ──

    private void UpdateReplaceRowVisibility()
    {
        var vis = _replaceVisible ? Visibility.Visible : Visibility.Collapsed;
        ReplaceInputRow.Visibility = vis;
        ReplaceButtonsRow.Visibility = vis;
    }

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
            ? (System.Windows.Media.Brush)FindResource("c-accent")
            : new SolidColorBrush(Colors.Transparent);

        // Use white text on accent background for readability
        var textBlock = (TextBlock)toggle.Child;
        textBlock.Foreground = active
            ? new SolidColorBrush(Colors.White)
            : (System.Windows.Media.Brush)FindResource("c-text-primary");
    }

    // ── Click & input handlers ──

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
