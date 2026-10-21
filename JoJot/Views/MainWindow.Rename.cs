using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using JoJot.Models;
using JoJot.Services;

namespace JoJot;

public partial class MainWindow
{
    // ─── Inline Rename ──────────────────────────────────────────────────

    /// <summary>
    /// Starts inline rename for a tab given its data item. Under virtualization the tab may be
    /// scrolled off-screen (no container), so scroll it into view and rename once the container
    /// is realized — otherwise the keystroke would be swallowed with nothing happening.
    /// </summary>
    private void BeginRenameByTab(NoteTab tab)
    {
        SafeScrollIntoView(tab);
        if (ContainerFor(tab) is ListBoxItem item)
        {
            BeginRename(item, tab);
            return;
        }
        Dispatcher.BeginInvoke(() =>
        {
            if (ContainerFor(tab) is ListBoxItem c)
                BeginRename(c, tab);
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    // Single rename TextBox shared by all tab items, created on first rename. Tab items used to
    // each carry a Collapsed TextBox in TabItemTemplate, which cost a full TextBox per realized
    // container even though at most one rename is ever active.
    private TextBox? _renameBox;

    private TextBox GetRenameBox() => _renameBox ??= new TextBox
    {
        FontSize = 13,
        MinWidth = 80,
        Padding = new Thickness(2, 0, 2, 0),
        VerticalAlignment = VerticalAlignment.Center
    };

    /// <summary>
    /// Starts inline rename for a tab. Hides the item's title panel and inserts the shared
    /// rename TextBox into the same title-row grid column.
    /// </summary>
    private void BeginRename(ListBoxItem item, NoteTab tab)
    {
        if (tab.IsFileBacked) return;
        if (_activeRename is not null) CommitRename();
        if (_isDragging) return;

        // Find the title panel by name in the DataTemplate; its parent is the title-row grid.
        var titlePanel = FindNamedDescendant<StackPanel>(item, "TitlePanel");
        if (titlePanel?.Parent is not Grid titleRow) return;

        var renameBox = GetRenameBox();
        (renameBox.Parent as Panel)?.Children.Remove(renameBox); // defensive: never in two items

        // The pinned DataTrigger moves TitlePanel to column 1 — follow wherever it is now.
        Grid.SetColumn(renameBox, Grid.GetColumn(titlePanel));
        renameBox.Text = tab.Name ?? "";
        titlePanel.Visibility = Visibility.Collapsed;
        titleRow.Children.Add(renameBox);
        renameBox.SelectAll();
        Keyboard.Focus(renameBox);

        _activeRename = (item, tab, renameBox, titlePanel);

        renameBox.PreviewKeyDown += RenameBox_PreviewKeyDown;
        renameBox.LostFocus += RenameBox_LostFocus;
    }

    /// <summary>
    /// Commits the rename: updates the model, persists to database, refreshes display.
    /// Empty/whitespace clears custom name, reverts to content fallback.
    /// </summary>
    private void CommitRename()
    {
        if (_activeRename is null) return;
        var (_, tab, box, title) = _activeRename.Value;

        box.PreviewKeyDown -= RenameBox_PreviewKeyDown;
        box.LostFocus -= RenameBox_LostFocus;

        string newName = box.Text.Trim();
        tab.Name = string.IsNullOrWhiteSpace(newName) ? null : newName;

        EndRenameVisuals(box, title);

        _activeRename = null;

        _ = NoteStore.UpdateNoteNameAsync(tab.Id, tab.Name);
    }

    /// <summary>
    /// Cancels the rename, restoring the original label without saving.
    /// </summary>
    private void CancelRename()
    {
        if (_activeRename is null) return;
        var (_, _, box, title) = _activeRename.Value;

        box.PreviewKeyDown -= RenameBox_PreviewKeyDown;
        box.LostFocus -= RenameBox_LostFocus;

        EndRenameVisuals(box, title);

        _activeRename = null;
    }

    /// <summary>
    /// Detaches the shared rename TextBox from its item and shows the title panel again.
    /// Handlers must already be unhooked: removing a focused box raises LostFocus.
    /// </summary>
    private static void EndRenameVisuals(TextBox box, FrameworkElement title)
    {
        (box.Parent as Panel)?.Children.Remove(box);
        title.Visibility = Visibility.Visible;
    }

    private void RenameBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Return)
        {
            CommitRename();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CancelRename();
            e.Handled = true;
        }
    }

    private void RenameBox_LostFocus(object sender, RoutedEventArgs e)
    {
        CommitRename();
    }
}
