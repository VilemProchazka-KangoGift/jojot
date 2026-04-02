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
    /// Starts inline rename for a tab. Hides the label TextBlock and shows a TextBox.
    /// </summary>
    private void BeginRename(ListBoxItem item, NoteTab tab)
    {
        if (_activeRename is not null) CommitRename();
        if (_isDragging) return;

        // Find the rename TextBox and label by name in the DataTemplate
        var renameBox = FindNamedDescendant<TextBox>(item, "RenameBox");
        var labelBlock = FindNamedDescendant<TextBlock>(item, "TitleBlock");
        if (renameBox is null || labelBlock is null) return;

        labelBlock.Visibility = Visibility.Collapsed;
        renameBox.Text = tab.Name ?? "";
        renameBox.Visibility = Visibility.Visible;
        renameBox.SelectAll();
        Keyboard.Focus(renameBox);

        _activeRename = (item, tab, renameBox, labelBlock);

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
        var (item, tab, box, labelBlock) = _activeRename.Value;

        box.PreviewKeyDown -= RenameBox_PreviewKeyDown;
        box.LostFocus -= RenameBox_LostFocus;

        string newName = box.Text.Trim();
        tab.Name = string.IsNullOrWhiteSpace(newName) ? null : newName;

        box.Visibility = Visibility.Collapsed;
        labelBlock.Visibility = Visibility.Visible;

        _activeRename = null;

        _ = NoteStore.UpdateNoteNameAsync(tab.Id, tab.Name);
    }

    /// <summary>
    /// Cancels the rename, restoring the original label without saving.
    /// </summary>
    private void CancelRename()
    {
        if (_activeRename is null) return;
        var (_, _, box, labelBlock) = _activeRename.Value;

        box.PreviewKeyDown -= RenameBox_PreviewKeyDown;
        box.LostFocus -= RenameBox_LostFocus;

        box.Visibility = Visibility.Collapsed;
        labelBlock.Visibility = Visibility.Visible;

        _activeRename = null;
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
