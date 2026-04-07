using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using JoJot.Resources;
using JoJot.Services;
using JoJot.Themes;

namespace JoJot.Controls;

/// <summary>
/// Modal overlay displaying all keyboard shortcuts, organized by section.
/// </summary>
public partial class HelpOverlay : UserControl
{
    private bool _built;

    public event EventHandler? CloseRequested;

    public HelpOverlay()
    {
        InitializeComponent();
    }

    public void Show()
    {
        if (!_built)
        {
            BuildContent();
            _built = true;
        }
        Visibility = Visibility.Visible;
    }

    public void Hide()
    {
        Visibility = Visibility.Collapsed;
    }

    private void BuildContent()
    {
        var shortcuts = new (string section, (string key, string desc)[] items)[]
        {
            (Strings.Help_SectionTabs, [
                ("Ctrl+T", Strings.Help_NewTab),
                ("Ctrl+W", Strings.Help_DeleteTab),
                ("Ctrl+Tab", Strings.Help_NextTab),
                ("Ctrl+Shift+Tab", Strings.Help_PreviousTab),
                ("F2", Strings.Help_RenameTab),
                ("Ctrl+P", Strings.Help_PinUnpin),
                ("Ctrl+K", Strings.Help_CloneTab)
            ]),
            (Strings.Help_SectionEditor, [
                ("Ctrl+Z", Strings.Help_Undo),
                ("Ctrl+Y", Strings.Help_Redo),
                ("Ctrl+Shift+Z", Strings.Help_RedoAlt),
                ("Ctrl+C", Strings.Help_Copy),
                ("Ctrl+V", Strings.Help_Paste),
                ("Ctrl+X", Strings.Help_Cut),
                ("Ctrl+A", Strings.Help_SelectAll),
                ("Ctrl+S", Strings.Help_SaveAsTxt),
                ("Ctrl+F", Strings.Help_Find)
            ]),
            (Strings.Help_SectionView, [
                ("Ctrl+=", Strings.Help_FontIncrease),
                ("Ctrl+-", Strings.Help_FontDecrease),
                ("Ctrl+0", Strings.Help_FontReset),
                ("Ctrl+Scroll", Strings.Help_Zoom)
            ]),
            (Strings.Help_SectionGlobal, [
                (HotkeyService.GetHotkeyDisplayString(), Strings.Help_FocusMinimize),
                ("Ctrl+Shift+/", Strings.Help_ShowHelp)
            ]),
        };

        foreach (var (section, items) in shortcuts)
        {
            var sectionHeader = new TextBlock
            {
                Text = section,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 12, 0, 6)
            };
            sectionHeader.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.TextMuted);
            HelpContent.Children.Add(sectionHeader);

            foreach (var (key, desc) in items)
            {
                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.Margin = new Thickness(0, 2, 0, 2);

                var keyBlock = new TextBlock
                {
                    Text = key,
                    FontSize = 12,
                    FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                    FontWeight = FontWeights.SemiBold
                };
                keyBlock.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.Accent);
                Grid.SetColumn(keyBlock, 0);
                row.Children.Add(keyBlock);

                var descBlock = new TextBlock
                {
                    Text = desc,
                    FontSize = 12
                };
                descBlock.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.TextPrimary);
                Grid.SetColumn(descBlock, 1);
                row.Children.Add(descBlock);

                HelpContent.Children.Add(row);
            }
        }
    }

    private void Backdrop_Click(object sender, MouseButtonEventArgs e)
    {
        Hide();
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Card_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
    }

    private void Close_Click(object sender, MouseButtonEventArgs e)
    {
        Hide();
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
