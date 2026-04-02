using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using JoJot.Services;

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
            ("TABS", [
                ("Ctrl+T", "New tab"),
                ("Ctrl+W", "Delete tab"),
                ("Ctrl+Tab", "Next tab"),
                ("Ctrl+Shift+Tab", "Previous tab"),
                ("F2", "Rename tab"),
                ("Ctrl+P", "Pin / Unpin"),
                ("Ctrl+K", "Clone tab")
            ]),
            ("EDITOR", [
                ("Ctrl+Z", "Undo"),
                ("Ctrl+Y", "Redo"),
                ("Ctrl+Shift+Z", "Redo (alt)"),
                ("Ctrl+C", "Copy (all if no selection)"),
                ("Ctrl+V", "Paste"),
                ("Ctrl+X", "Cut"),
                ("Ctrl+A", "Select all"),
                ("Ctrl+S", "Save as TXT"),
                ("Ctrl+F", "Find in editor / Search tabs")
            ]),
            ("VIEW", [
                ("Ctrl+=", "Increase font size"),
                ("Ctrl+-", "Decrease font size"),
                ("Ctrl+0", "Reset font size (100%)"),
                ("Ctrl+Scroll", "Zoom (over editor)")
            ]),
            ("GLOBAL", [
                (HotkeyService.GetHotkeyDisplayString(), "Focus / minimize JoJot"),
                ("Ctrl+Shift+/", "Show this help")
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
            sectionHeader.SetResourceReference(TextBlock.ForegroundProperty, "c-text-muted");
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
                keyBlock.SetResourceReference(TextBlock.ForegroundProperty, "c-accent");
                Grid.SetColumn(keyBlock, 0);
                row.Children.Add(keyBlock);

                var descBlock = new TextBlock
                {
                    Text = desc,
                    FontSize = 12
                };
                descBlock.SetResourceReference(TextBlock.ForegroundProperty, "c-text-primary");
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
