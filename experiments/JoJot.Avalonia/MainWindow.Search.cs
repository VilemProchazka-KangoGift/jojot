using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using JoJot.Resources;
using JoJot.ViewModels;

namespace JoJot.AvaloniaExperiment;

public sealed partial class MainWindow
{
    private readonly DispatcherTimer _findTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private TextBox? _findQuery;
    private TextBox? _findReplace;
    private CheckBox? _findCase;
    private CheckBox? _findWhole;
    private TextBlock? _findCount;
    private FindHighlights? _findHighlights;
    private int _findGeneration;
    private List<int> _findMatches = [];
    private int _findIndex = -1;
    private int _findLength;

    private void OnFind(object? sender, RoutedEventArgs e) => ShowFindPanel();

    private void ShowFindPanel()
    {
        if (_active is null)
            return;
        if (_findQuery is null)
            CreateFindPanel();
        FindHost.IsVisible = true;
        if (Editor.SelectedText is { Length: > 0 } selection && !selection.Contains('\n') && !selection.Contains('\r'))
            _findQuery!.Text = selection;
        _findQuery!.Focus();
        _findQuery.SelectAll();
        ScheduleFindRefresh();
    }

    private void CreateFindPanel()
    {
        var query = new TextBox { Name = "FindQuery", PlaceholderText = Strings.Find_Placeholder, MinWidth = 70 };
        var replacement = new TextBox { Name = "FindReplacement", PlaceholderText = Strings.Find_ReplacePlaceholder, MinWidth = 70 };
        var matchCase = new CheckBox { Content = CoreLabel("Match case", "Rozlišovat velikost písmen") };
        var wholeWord = new CheckBox { Content = CoreLabel("Whole word", "Celá slova") };
        var count = new TextBlock { VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        _findQuery = query;
        _findReplace = replacement;
        _findCase = matchCase;
        _findWhole = wholeWord;
        _findCount = count;

        var inputs = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 5 };
        Grid.SetColumn(replacement, 1);
        inputs.Children.Add(query);
        inputs.Children.Add(replacement);
        var actions = new WrapPanel
        {
            Children =
            {
                matchCase, wholeWord,
                FindActionButton("↑", CoreLabel("Previous match", "Předchozí shoda"), () => NavigateFind(false)),
                FindActionButton("↓", CoreLabel("Next match", "Další shoda"), () => NavigateFind(true)),
                FindActionButton(Strings.Find_Replace, Strings.Find_Replace, () => ReplaceFind(false)),
                FindActionButton(Strings.Find_ReplaceAll, Strings.Find_ReplaceAll, () => ReplaceFind(true)),
                FindActionButton("×", CoreLabel("Close find", "Zavřít hledání"), HideFindPanel),
                count,
            },
        };
        FindHost.Child = new StackPanel { Spacing = 4, Children = { inputs, actions } };

        query.TextChanged += (_, _) => ScheduleFindRefresh();
        matchCase.IsCheckedChanged += (_, _) => ScheduleFindRefresh();
        wholeWord.IsCheckedChanged += (_, _) => ScheduleFindRefresh();
        _findTimer.Tick += async (_, _) =>
        {
            _findTimer.Stop();
            await RunFindAsync();
        };

        _findHighlights = new FindHighlights(Editor) { IsHitTestVisible = false, ClipToBounds = true };
        if (Editor.Parent is Grid grid)
        {
            Grid.SetRow(_findHighlights, Grid.GetRow(Editor));
            grid.Children.Add(_findHighlights);
        }
        Editor.AddHandler(ScrollViewer.ScrollChangedEvent, (_, _) => _findHighlights?.InvalidateVisual());
    }

    private static Button FindActionButton(string label, string help, Action action)
    {
        var button = new Button { Content = label, Padding = new Thickness(5, 2), Margin = new Thickness(0, 0, 4, 0) };
        ToolTip.SetTip(button, help);
        Avalonia.Automation.AutomationProperties.SetName(button, help);
        button.Click += (_, _) => action();
        return button;
    }

    private void HideFindPanel()
    {
        FindHost.IsVisible = false;
        _findTimer.Stop();
        _findGeneration++;
        _findMatches = [];
        _findIndex = -1;
        _findHighlights?.Update([], -1, 0);
        Editor.SelectionStart = Editor.CaretIndex;
        Editor.SelectionEnd = Editor.CaretIndex;
        FocusEditorIfIdle();
    }

    private void ScheduleFindRefresh()
    {
        if (!FindHost.IsVisible)
            return;
        _findGeneration++;
        _findMatches = [];
        _findIndex = -1;
        _findHighlights?.Update([], -1, 0);
        _findTimer.Stop();
        _findTimer.Start();
    }

    private async Task RunFindAsync()
    {
        if (!FindHost.IsVisible || _findQuery is null)
            return;
        var generation = ++_findGeneration;
        var text = Editor.Text ?? "";
        var query = _findQuery.Text ?? "";
        var matchCase = _findCase!.IsChecked == true;
        var wholeWord = _findWhole!.IsChecked == true;
        try
        {
            var matches = query.Length == 0 ? []
                : await Task.Run(() => MainWindowViewModel.FindAllMatches(text, query, matchCase, wholeWord));
            if (generation != _findGeneration || text != Editor.Text)
                return;
            ApplyFindResult(matches, query.Length);
        }
        catch (Exception ex) { ReportError(ex); }
    }

    private void ApplyFindResult(List<int> matches, int length)
    {
        _findMatches = matches;
        _findIndex = matches.Count > 0 ? 0 : -1;
        _findLength = length;
        ShowFindMatch();
    }

    private void NavigateFind(bool forward)
    {
        if (_findMatches.Count == 0)
            return;
        _findIndex = MainWindowViewModel.CycleIndex(_findIndex, _findMatches.Count, forward);
        ShowFindMatch();
    }

    private void ShowFindMatch()
    {
        if (_findIndex >= 0)
        {
            var start = _findMatches[_findIndex];
            Editor.CaretIndex = start + _findLength;
            Editor.SelectionStart = start;
            Editor.SelectionEnd = start + _findLength;
        }
        _findCount!.Text = MainWindowViewModel.FormatFindCountText(_findIndex, _findMatches.Count);
        _findHighlights?.Update(_findMatches, _findIndex, _findLength);
    }

    private void ReplaceFind(bool all)
    {
        if (_active is null || _findQuery?.Text is not { Length: > 0 } query)
            return;
        var before = Editor.Text ?? "";
        var replacement = _findReplace?.Text ?? "";
        var matchCase = _findCase!.IsChecked == true;
        var wholeWord = _findWhole!.IsChecked == true;
        var matches = MainWindowViewModel.FindAllMatches(before, query, matchCase, wholeWord);
        if (matches.Count == 0)
            return;
        CaptureEditorText();
        FlushUndoSnapshot();
        if (all)
        {
            var (after, count) = MainWindowViewModel.ReplaceAll(before, query, replacement, matchCase, wholeWord);
            Editor.Text = after;
            CaptureEditorText();
            FlushUndoSnapshot();
            if (_pendingDeletion is null)
                ShowToast(string.Format(PanelLanguage.Plural(Strings.Toast_Replacements_One,
                    Strings.Toast_Replacements_Few, Strings.Toast_Replacements, count), count), () => MoveHistory(false));
        }
        else
        {
            var current = Math.Clamp(_findIndex, 0, matches.Count - 1);
            Editor.Text = MainWindowViewModel.ReplaceSingle(before, matches[current], query.Length, replacement);
            CaptureEditorText();
            FlushUndoSnapshot();
        }
    }

    private sealed class FindHighlights(TextBox editor) : Control
    {
        private List<int> _matches = [];
        private int _selected;
        private int _length;

        public void Update(List<int> matches, int selected, int length)
        {
            _matches = matches;
            _selected = selected;
            _length = length;
            InvalidateVisual();
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            if (_matches.Count == 0 || _length == 0 ||
                editor.GetVisualDescendants().OfType<TextPresenter>().FirstOrDefault() is not { } presenter ||
                presenter.TranslatePoint(default, this) is not { } offset)
                return;
            var layout = presenter.TextLayout;
            var firstVisible = layout.HitTestPoint(new Point(0, -offset.Y)).TextPosition;
            var lastVisible = layout.HitTestPoint(new Point(layout.Width, Bounds.Height - offset.Y)).TextPosition;
            var index = _matches.BinarySearch(Math.Max(0, firstVisible - _length));
            if (index < 0)
                index = ~index;
            var color = editor.SelectionBrush is ISolidColorBrush selection ? selection.Color : Colors.Gold;
            var highlight = new SolidColorBrush(color, 0.28);
            for (; index < _matches.Count && _matches[index] <= lastVisible; index++)
            {
                if (index == _selected)
                    continue;
                foreach (var rect in layout.HitTestTextRange(_matches[index], _length))
                    context.DrawRectangle(highlight, null, rect.Translate(new Vector(offset.X, offset.Y)));
            }
        }
    }
}
