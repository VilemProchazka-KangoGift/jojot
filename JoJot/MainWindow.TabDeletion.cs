using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using JoJot.Models;
using JoJot.Services;

namespace JoJot;

public partial class MainWindow
{
    // ─── Deletion Engine ─

    /// <summary>
    /// Hard-deletes all tabs in the current pending deletion from the database.
    /// Called before creating a new pending deletion or after the 4s timer fires.
    /// Safe to call when _pendingDeletion is null.
    /// </summary>
    private async Task CommitPendingDeletionAsync()
    {
        if (_pendingDeletion is null) return;

        // Capture and clear before any awaits to prevent double-dispose races
        var pending = _pendingDeletion;
        _pendingDeletion = null;

        pending.Cts.Cancel();
        pending.Cts.Dispose();

        foreach (var tab in pending.Tabs)
        {
            await DatabaseService.DeleteNoteAsync(tab.Id);
            // Remove undo stack on permanent deletion
            UndoManager.Instance.RemoveStack(tab.Id);
        }
    }

    /// <summary>
    /// Soft-deletes a single tab: removes from UI immediately, shows undo toast,
    /// and schedules a hard-delete after 4 seconds. Commits any previous pending deletion first.
    /// </summary>
    private async Task DeleteTabAsync(NoteTab tab)
    {
        SaveCurrentTabContent();
        await CommitPendingDeletionAsync();

        int originalIndex = _tabs.IndexOf(tab);
        bool wasActive = (_activeTab?.Id == tab.Id);

        _tabs.Remove(tab);
        RebuildTabList();

        if (wasActive)
            await ApplyFocusCascadeAsync(originalIndex);

        var cts = new CancellationTokenSource();
        _pendingDeletion = new PendingDeletion([tab], [originalIndex], cts);
        ShowToast(isBulk: false, label: tab.DisplayLabel);
        _ = StartDismissTimerAsync(cts.Token);
    }

    /// <summary>
    /// Soft-deletes multiple tabs at once, skipping pinned tabs.
    /// Shows "N notes deleted" toast and schedules a bulk hard-delete after 4 seconds.
    /// </summary>
    private async Task DeleteMultipleAsync(IEnumerable<NoteTab> candidates)
    {
        var toDelete = candidates.Where(t => !t.Pinned).ToList();
        if (toDelete.Count == 0) return;

        SaveCurrentTabContent();
        await CommitPendingDeletionAsync();

        // Capture original indexes before any removal
        var originalIndexes = toDelete.Select(t => _tabs.IndexOf(t)).ToList();

        bool wasActive = _activeTab is not null && toDelete.Any(t => t.Id == _activeTab.Id);
        int activeOriginalIndex = wasActive ? _tabs.IndexOf(_activeTab!) : 0;

        foreach (var tab in toDelete)
            _tabs.Remove(tab);

        RebuildTabList();

        if (wasActive)
            await ApplyFocusCascadeAsync(activeOriginalIndex);

        var cts = new CancellationTokenSource();
        _pendingDeletion = new PendingDeletion(toDelete, originalIndexes, cts);
        ShowToast(isBulk: true, count: toDelete.Count);
        _ = StartDismissTimerAsync(cts.Token);
    }

    /// <summary>
    /// Waits 4 seconds then commits the pending deletion and hides the toast.
    /// Cancellation (via undo or new deletion) silently aborts the timer.
    /// </summary>
    private async Task StartDismissTimerAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(4000, token);
            // Timer fired — commit deletion and slide down the toast
            await CommitPendingDeletionAsync();
            HideToast();
        }
        catch (OperationCanceledException)
        {
            // Cancelled by undo or new deletion — do nothing
        }
    }

    /// <summary>
    /// Undo handler: re-inserts the pending tabs at their original positions.
    /// Cancels the dismiss timer so no hard-delete occurs.
    /// </summary>
    private async Task UndoDeleteAsync()
    {
        if (_pendingDeletion is null) return;

        var pending = _pendingDeletion;
        _pendingDeletion = null;

        // Cancel and dispose the timer CTS — no hard-delete will occur
        pending.Cts.Cancel();
        pending.Cts.Dispose();

        // Re-insert in ascending index order with clamping to handle shifted indexes
        var pairs = pending.Tabs.Zip(pending.OriginalIndexes, (tab, idx) => (tab, idx))
                                .OrderBy(p => p.idx)
                                .ToList();

        foreach (var (tab, originalIndex) in pairs)
        {
            int insertAt = Math.Min(originalIndex, _tabs.Count);
            _tabs.Insert(insertAt, tab);
        }

        RebuildTabList();

        // Select the first restored tab
        SelectTabByNote(pending.Tabs[0]);

        HideToast();

        await Task.CompletedTask; // Satisfies async contract; logic is synchronous
    }

    /// <summary>
    /// Focus cascade after deleting the active tab:
    /// 1. First visible tab at or below the deleted position
    /// 2. Last visible tab (if no tab below)
    /// 3. Clear search and recurse if search is hiding all tabs
    /// 4. Create a new empty tab if no tabs exist at all
    /// </summary>
    private async Task ApplyFocusCascadeAsync(int deletedIndex)
    {
        var visible = _tabs.Where(t => MatchesSearch(t)).ToList();

        if (visible.Count > 0)
        {
            // Find the first visible tab whose _tabs position >= deletedIndex
            NoteTab? target = null;
            foreach (var t in visible)
            {
                if (_tabs.IndexOf(t) >= deletedIndex)
                {
                    target = t;
                    break;
                }
            }
            // Fallback to last visible tab
            target ??= visible[^1];
            SelectTabByNote(target);
        }
        else if (!string.IsNullOrEmpty(_searchText))
        {
            // Search is active and hiding everything — clear it and recurse
            SearchBox.Text = "";
            _searchText = "";
            SearchPlaceholder.Visibility = Visibility.Visible;
            RebuildTabList();
            await ApplyFocusCascadeAsync(0);
        }
        else
        {
            // No tabs at all — auto-create an empty tab
            await CreateNewTabAsync();
        }
    }

    // ─── Toast Overlay ──────────────────

    /// <summary>
    /// Sets toast text for a single-tab deletion: e.g. "Note name" deleted
    /// with the name portion in italic. Truncates raw label to 30 chars.
    /// </summary>
    private void UpdateToastContent(string rawLabel)
    {
        string truncated = rawLabel.Length > 30 ? rawLabel[..30] : rawLabel;

        ToastMessageBlock.Inlines.Clear();
        ToastMessageBlock.Inlines.Add(new System.Windows.Documents.Run("\u201C"));
        var italicRun = new System.Windows.Documents.Run(truncated) { FontStyle = FontStyles.Italic };
        ToastMessageBlock.Inlines.Add(italicRun);
        ToastMessageBlock.Inlines.Add(new System.Windows.Documents.Run("\u201D deleted"));
    }

    /// <summary>
    /// Sets toast text for a bulk deletion: "{count} notes deleted".
    /// </summary>
    private void UpdateToastContentBulk(int count)
    {
        ToastMessageBlock.Inlines.Clear();
        ToastMessageBlock.Inlines.Add(new System.Windows.Documents.Run($"{count} notes deleted"));
    }

    /// <summary>
    /// Shows the toast with a slide-up animation.
    /// If toast is already visible: only updates content — no re-animation.
    /// </summary>
    private void ShowToast(bool isBulk, string? label = null, int count = 0)
    {
        if (isBulk)
            UpdateToastContentBulk(count);
        else
            UpdateToastContent(label ?? "");

        // If already visible, content swap only — do not re-animate
        if (ToastBorder.Visibility == Visibility.Visible)
            return;

        // Slide up from bottom: Y from 36 to 0 over 150ms with cubic ease-out
        ToastTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        ToastTranslate.Y = 36;
        ToastBorder.Visibility = Visibility.Visible;

        var anim = new DoubleAnimation
        {
            From = 36,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(150),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        ToastTranslate.BeginAnimation(TranslateTransform.YProperty, anim);
    }

    /// <summary>
    /// Hides the toast with a slide-down animation.
    /// Sets Visibility = Collapsed and resets Y = 36 on completion.
    /// </summary>
    private void HideToast()
    {
        if (ToastBorder.Visibility != Visibility.Visible) return;

        var anim = new DoubleAnimation
        {
            From = 0,
            To = 36,
            Duration = TimeSpan.FromMilliseconds(150),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        anim.Completed += (_, _) =>
        {
            ToastBorder.Visibility = Visibility.Collapsed;
            ToastTranslate.BeginAnimation(TranslateTransform.YProperty, null);
            ToastTranslate.Y = 36;
        };
        ToastTranslate.BeginAnimation(TranslateTransform.YProperty, anim);
    }

    /// <summary>
    /// Undo button click handler — restores the pending deletion.
    /// </summary>
    private void UndoToast_Click(object sender, MouseButtonEventArgs e)
    {
        _ = UndoDeleteAsync();
    }
}
