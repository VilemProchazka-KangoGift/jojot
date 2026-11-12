using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.VisualTree;
using JoJot.Resources;

namespace JoJot.AvaloniaExperiment;

public sealed partial class MainWindow
{
    private void ShowHelp()
    {
        var body = TogglePanel("help", Strings.Help_Title);
        if (body is null)
            return;

        PanelHost.HorizontalAlignment = HorizontalAlignment.Center;
        PanelHost.VerticalAlignment = VerticalAlignment.Center;
        PanelHost.MaxHeight = Math.Max(240, Bounds.Height - 84);
        AddHandler(PointerPressedEvent, OnHelpOutsidePressed, RoutingStrategies.Tunnel);

        void AddSection(string title, params (string Keys, string Description)[] shortcuts)
        {
            body.Children.Add(PanelSection(title));
            foreach (var (keys, description) in shortcuts)
            {
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("115,*"), ColumnSpacing = 6 };
                var keyText = PanelText(keys);
                var descriptionText = PanelText(description);
                Grid.SetColumn(descriptionText, 1);
                row.Children.Add(keyText);
                row.Children.Add(descriptionText);
                body.Children.Add(row);
            }
        }

        AddSection(Strings.Help_SectionTabs,
            ("Ctrl+T", Strings.Help_NewTab), ("Ctrl+W", Strings.Help_DeleteTab),
            ("Ctrl+Tab", Strings.Help_NextTab), ("Ctrl+Shift+Tab", Strings.Help_PreviousTab),
            ("F2", Strings.Help_RenameTab), ("Ctrl+P", Strings.Help_PinUnpin), ("Ctrl+K", Strings.Help_CloneTab));
        AddSection(Strings.Help_SectionEditor,
            ("Ctrl+Z", Strings.Help_Undo), ("Ctrl+Y", Strings.Help_Redo), ("Ctrl+Shift+Z", Strings.Help_RedoAlt),
            ("Ctrl+C", Strings.Help_Copy), ("Ctrl+V", Strings.Help_Paste), ("Ctrl+X", Strings.Help_Cut),
            ("Ctrl+A", Strings.Help_SelectAll), ("Ctrl+S", Strings.Help_Save), ("Ctrl+Shift+S", Strings.Help_SaveAs),
            ("Ctrl+F", Strings.Help_Find));
        AddSection(Strings.Help_SectionView,
            ("Ctrl+=", Strings.Help_FontIncrease), ("Ctrl+-", Strings.Help_FontDecrease),
            ("Ctrl+0", Strings.Help_FontReset), ("Ctrl+Scroll", Strings.Help_Zoom));
        AddSection(Strings.Help_SectionGlobal,
            (CurrentGlobalShortcut?.Invoke() ?? "Win+Shift+N", Strings.Help_FocusMinimize),
            ("Ctrl+Shift+/", Strings.Help_ShowHelp));
        body.Children.Add(PanelText(Strings.Help_DismissHint));
    }

    private void OnHelpOutsidePressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Visual source && source != PanelHost && !PanelHost.IsVisualAncestorOf(source))
            DismissSidePanel();
    }
}
