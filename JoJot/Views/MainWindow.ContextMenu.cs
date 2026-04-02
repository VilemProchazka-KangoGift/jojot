using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using JoJot.Models;
using JoJot.Themes;

namespace JoJot;

public partial class MainWindow
{
    // ─── Tab Context Menu ──────────────────────

    private static readonly System.Windows.Media.FontFamily IconFontFamily =
        new("Segoe Fluent Icons, Segoe MDL2 Assets");

    /// <summary>
    /// Builds a themed context menu Popup for a tab. Matches hamburger menu styling.
    /// Uses Popup instead of WPF ContextMenu for consistent theming.
    /// </summary>
    private Popup BuildTabContextMenu(NoteTab tab, ListBoxItem item)
    {
        // Close any existing context popup
        if (_activeContextMenu is not null) _activeContextMenu.IsOpen = false;

        var popup = new Popup
        {
            StaysOpen = false,
            AllowsTransparency = true,
            Placement = PlacementMode.MousePoint,
            HorizontalOffset = 0,
            VerticalOffset = 0
        };

        var border = new Border
        {
            Background = GetBrush(ThemeKeys.SidebarBackground),
            BorderBrush = GetBrush(ThemeKeys.Border),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(4),
            MinWidth = 200,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 12, Opacity = 0.25, ShadowDepth = 4, Direction = 270
            }
        };

        var stack = new StackPanel();

        // Local helper to create a styled menu item Border
        Border CreateCtxItem(string icon, string text, string? shortcut, Action onClick)
        {
            var b = new Border
            {
                Padding = new Thickness(8, 6, 8, 6),
                Cursor = Cursors.Hand,
                Background = System.Windows.Media.Brushes.Transparent
            };
            b.MouseEnter += MenuItem_MouseEnter;
            b.MouseLeave += MenuItem_MouseLeave;

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            if (shortcut is not null)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var iconBlock = new TextBlock
            {
                Text = icon,
                FontFamily = IconFontFamily,
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center
            };
            iconBlock.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.ToolbarIcon);
            Grid.SetColumn(iconBlock, 0);
            grid.Children.Add(iconBlock);

            var textBlock = new TextBlock
            {
                Text = text,
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center
            };
            textBlock.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.TextPrimary);
            Grid.SetColumn(textBlock, 1);
            grid.Children.Add(textBlock);

            if (shortcut is not null)
            {
                var shortcutBlock = new TextBlock
                {
                    Text = shortcut,
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(16, 0, 0, 0)
                };
                shortcutBlock.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.TextMuted);
                Grid.SetColumn(shortcutBlock, 2);
                grid.Children.Add(shortcutBlock);
            }

            b.Child = grid;
            b.MouseLeftButtonDown += (s, e) => { popup.IsOpen = false; onClick(); };
            return b;
        }

        // Rename
        stack.Children.Add(CreateCtxItem("\uE8AC", "Rename", "F2", () =>
        {
            StartRename(item, tab);
        }));

        // Pin/Unpin (dynamic text based on tab state)
        string pinText = tab.Pinned ? "Unpin" : "Pin";
        string pinIcon = tab.Pinned ? "\uE77A" : "\uE718";
        stack.Children.Add(CreateCtxItem(pinIcon, pinText, "Ctrl+P", () =>
        {
            _activeTab = tab;
            SelectTabByNote(tab);
            ToolbarPin_Click(this, new RoutedEventArgs());
        }));

        // Clone to new tab
        stack.Children.Add(CreateCtxItem("\uF413", "Clone to new tab", "Ctrl+K", () =>
        {
            _activeTab = tab;
            SelectTabByNote(tab);
            ToolbarClone_Click(this, new RoutedEventArgs());
        }));

        // Save as TXT
        stack.Children.Add(CreateCtxItem("\uE74E", "Save as TXT", "Ctrl+S", () =>
        {
            _activeTab = tab;
            SelectTabByNote(tab);
            ToolbarSave_Click(this, new RoutedEventArgs());
        }));

        // Separator
        var sep = new Separator { Margin = new Thickness(4, 2, 4, 2) };
        sep.SetResourceReference(BackgroundProperty, ThemeKeys.Border);
        stack.Children.Add(sep);

        // Delete
        stack.Children.Add(CreateCtxItem("\uE74D", "Delete", "Ctrl+W", () =>
        {
            _ = DeleteTabAsync(tab);
        }));

        // Delete all below
        stack.Children.Add(CreateCtxItem("\uE75C", "Delete all below", null, () =>
        {
            int tabIndex = _tabs.IndexOf(tab);
            if (tabIndex < 0) return;
            var belowTabs = _tabs.Skip(tabIndex + 1).ToList();
            if (belowTabs.Count == 0) return;
            _ = DeleteMultipleAsync(belowTabs); // Skips pinned tabs internally
        }));

        border.Child = stack;
        popup.Child = border;
        _activeContextMenu = popup;
        return popup;
    }

    /// <summary>
    /// Starts inline rename for a tab item via context menu action.
    /// Delegates to BeginRename (same as F2 / double-click).
    /// </summary>
    private void StartRename(ListBoxItem item, NoteTab tab)
    {
        BeginRename(item, tab);
    }
}
