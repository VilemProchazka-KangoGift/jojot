using System.Windows;
using JoJot.Models;
using JoJot.Resources;
using JoJot.Services;

namespace JoJot;

public partial class MainWindow
{
    // ─── File Drop ──────────────────────────────────────────────────────

    /// <summary>
    /// DragEnter handler — shows drop overlay when files are dragged over the content area.
    /// </summary>
    private void OnFileDragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            _fileDragEnterCount++;
            e.Effects = DragDropEffects.Copy;
            FileDropOverlay.Visibility = Visibility.Visible;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    /// <summary>
    /// DragOver handler — maintains copy cursor while dragging over content area.
    /// </summary>
    private void OnFileDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
            e.Effects = DragDropEffects.Copy;
        else
            e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>
    /// DragLeave handler — hides drop overlay when drag leaves the content area.
    /// Only hides when the mouse truly leaves the content area bounds.
    /// </summary>
    private void OnFileDragLeave(object sender, DragEventArgs e)
    {
        // Enter/leave counter for reliable overlay dismiss across child boundaries
        _fileDragEnterCount--;
        if (_fileDragEnterCount <= 0)
        {
            _fileDragEnterCount = 0;
            FileDropOverlay.Visibility = Visibility.Collapsed;
        }
        e.Handled = true;
    }

    /// <summary>
    /// Drop handler — processes dropped files and creates tabs for valid text files.
    /// </summary>
    private void OnFileDrop(object sender, DragEventArgs e)
    {
        _fileDragEnterCount = 0;
        FileDropOverlay.Visibility = Visibility.Collapsed;
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            _ = ProcessDroppedFilesAsync(files);
        }
        e.Handled = true;
    }

    /// <summary>
    /// Processes dropped files: validates, creates tabs, shows errors via toast.
    /// </summary>
    private async Task ProcessDroppedFilesAsync(string[] filePaths)
    {
        var summary = await FileDropService.ProcessDroppedFilesAsync(filePaths);

        if (summary.ValidFiles.Count > 0)
        {
            // Insert at first position below pinned tabs
            int pinnedCount = _tabs.Count(t => t.Pinned);

            // Shift all unpinned tabs' sort orders down to make room
            for (int i = pinnedCount; i < _tabs.Count; i++)
                _tabs[i].SortOrder += summary.ValidFiles.Count;

            if (_tabs.Count > pinnedCount)
            {
                _ = NoteStore.UpdateNoteSortOrdersAsync(
                    _tabs.Skip(pinnedCount).Select(t => (t.Id, t.SortOrder)));
            }

            int insertOffset = 0;
            foreach (var result in summary.ValidFiles)
            {
                int sortOrder = pinnedCount + insertOffset;
                long newId = await NoteStore.InsertNoteAsync(
                    _desktopGuid, result.FileName, result.Content!, false, sortOrder, result.FilePath);

                var newTab = new NoteTab
                {
                    Id = newId,
                    DesktopGuid = _desktopGuid,
                    Name = result.FileName,
                    Content = result.Content!,
                    Pinned = false,
                    SortOrder = sortOrder,
                    FilePath = result.FilePath,
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                };

                _tabs.Insert(pinnedCount + insertOffset, newTab);
                insertOffset++;
            }

            // Rebuild and select the first dropped tab
            RebuildTabList();
            var firstDropped = _tabs.FirstOrDefault(t =>
                t.Name == summary.ValidFiles[0].FileName && !t.Pinned);
            if (firstDropped is not null)
                SelectTabByNote(firstDropped);
        }

        // Show error toast for invalid files
        if (summary.ErrorCount > 0 && summary.CombinedErrorMessage is not null)
        {
            ShowInfoToast(summary.CombinedErrorMessage);
        }
    }

    /// <summary>
    /// Shows an info-only toast (no undo button) that auto-dismisses after 4 seconds.
    /// Used for file drop errors and hotkey conflict notifications.
    /// </summary>
    public void ShowInfoToast(string message)
    {
        _pendingToastUndoAction = null;

        ToastMessageBlock.Text = message;
        UndoButton.Visibility = Visibility.Collapsed;

        if (!AnimateToastIn()) return;

        _ = Task.Delay(4000).ContinueWith(_ =>
        {
            Dispatcher.Invoke(() =>
            {
                HideToast();
                UndoButton.Visibility = Visibility.Visible;
            });
        });
    }

    /// <summary>
    /// Shows a toast for hotkey registration failure on startup.
    /// </summary>
    public void ShowHotkeyConflictToast()
    {
        var combo = HotkeyService.GetHotkeyDisplayString();
        ShowInfoToast(string.Format(Strings.Toast_HotkeyConflict, combo));
    }
}
