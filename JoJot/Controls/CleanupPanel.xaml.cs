using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using JoJot.Models;
using JoJot.Resources;
using JoJot.Services;
using JoJot.Themes;
using JoJot.ViewModels;

namespace JoJot.Controls;

/// <summary>
/// Side panel for bulk-deleting old or unused tabs based on age and pin-status filters.
/// </summary>
public partial class CleanupPanel : UserControl
{
    private List<NoteTab> _currentCandidates = [];

    public event EventHandler? CloseRequested;
    public event EventHandler<List<NoteTab>>? DeleteRequested;
    public event EventHandler? FilterChanged;

    public string AgeText => AgeInput.Text;
    public int UnitIndex => UnitCombo.SelectedIndex;
    public bool IncludePinned => IncludePinnedCheckbox.IsChecked == true;

    public CleanupPanel()
    {
        InitializeComponent();
    }

    public void Show()
    {
        Visibility = Visibility.Visible;
        var anim = new DoubleAnimation
        {
            From = 320, To = 0,
            Duration = TimeSpan.FromMilliseconds(250),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        PanelTransform.BeginAnimation(TranslateTransform.XProperty, anim);
    }

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

    public void ResetFilters()
    {
        AgeInput.Text = "7";
        UnitCombo.SelectedIndex = 0;
        IncludePinnedCheckbox.IsChecked = false;
    }

    public void RefreshPreview(List<NoteTab> candidates)
    {
        _currentCandidates = candidates;
        PreviewList.Children.Clear();

        if (candidates.Count > 0)
        {
            var fmt = LanguageService.Plural(Strings.Cleanup_DeleteN_One, Strings.Cleanup_DeleteN_Few, Strings.Cleanup_DeleteN, candidates.Count);
            DeleteText.Text = string.Format(fmt, candidates.Count);
            DeleteButton.IsEnabled = true;
            DeleteButton.Opacity = 1.0;
        }
        else
        {
            DeleteText.Text = Strings.Cleanup_Delete0;
            DeleteButton.IsEnabled = false;
            DeleteButton.Opacity = 0.5;
        }

        if (candidates.Count == 0)
        {
            var emptyBlock = new TextBlock
            {
                Text = Strings.Cleanup_NoMatch,
                FontSize = 13,
                FontStyle = FontStyles.Italic,
                Margin = new Thickness(0, 8, 0, 0)
            };
            emptyBlock.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.TextMuted);
            PreviewList.Children.Add(emptyBlock);
            return;
        }

        for (int i = 0; i < candidates.Count; i++)
        {
            var tab = candidates[i];
            bool isLast = (i == candidates.Count - 1);
            PreviewList.Children.Add(CreatePreviewRow(tab, isLast));
        }
    }

    private FrameworkElement CreatePreviewRow(NoteTab tab, bool isLast)
    {
        var container = new StackPanel { Margin = new Thickness(0, 6, 0, 6) };

        var titleBlock = new TextBlock
        {
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        if (tab.Pinned)
        {
            var pinRun = new System.Windows.Documents.Run("\uE718 ")
            {
                FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                FontSize = 10
            };
            pinRun.SetResourceReference(System.Windows.Documents.Run.ForegroundProperty, ThemeKeys.TextMuted);
            titleBlock.Inlines.Add(pinRun);
        }

        string displayName = tab.DisplayLabel;
        titleBlock.Inlines.Add(new System.Windows.Documents.Run(displayName)
        {
            FontWeight = FontWeights.Normal
        });

        string excerpt = MainWindowViewModel.GetCleanupExcerpt(tab);
        if (!string.IsNullOrEmpty(excerpt))
        {
            var excerptRun = new System.Windows.Documents.Run($" \u2014 {excerpt}")
            {
                FontStyle = FontStyles.Italic
            };
            excerptRun.SetResourceReference(System.Windows.Documents.Run.ForegroundProperty, ThemeKeys.TextMuted);
            titleBlock.Inlines.Add(excerptRun);
        }

        titleBlock.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.TextPrimary);
        container.Children.Add(titleBlock);

        var dateRow = new Grid { Margin = new Thickness(0, 2, 0, 0) };
        var createdBlock = new TextBlock
        {
            Text = tab.CreatedDisplay,
            FontSize = 10,
            HorizontalAlignment = HorizontalAlignment.Left,
            ToolTip = NoteTab.CreatedTooltip(tab.CreatedAt)
        };
        createdBlock.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.TextMuted);
        dateRow.Children.Add(createdBlock);
        var updatedBlock = new TextBlock
        {
            Text = tab.UpdatedDisplay,
            FontSize = 10,
            HorizontalAlignment = HorizontalAlignment.Right,
            ToolTip = NoteTab.UpdatedTooltip(tab.UpdatedAt)
        };
        updatedBlock.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.TextMuted);
        dateRow.Children.Add(updatedBlock);
        container.Children.Add(dateRow);

        if (!isLast)
        {
            var wrapper = new StackPanel();
            wrapper.Children.Add(container);
            var divider = new Separator
            {
                Margin = new Thickness(0, 2, 0, 0)
            };
            divider.SetResourceReference(BackgroundProperty, ThemeKeys.Border);
            wrapper.Children.Add(divider);
            return wrapper;
        }

        return container;
    }

    private void Close_Click(object sender, MouseButtonEventArgs e)
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Delete_Click(object sender, MouseButtonEventArgs e)
    {
        if (_currentCandidates.Count == 0) return;
        DeleteRequested?.Invoke(this, _currentCandidates);
    }

    private void AgeInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        FilterChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UnitCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        FilterChanged?.Invoke(this, EventArgs.Empty);
    }

    private void IncludePinned_Changed(object sender, RoutedEventArgs e)
    {
        FilterChanged?.Invoke(this, EventArgs.Empty);
    }
}
