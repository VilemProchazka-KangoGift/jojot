using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using JoJot.Models;
using JoJot.Resources;
using JoJot.Services;

namespace JoJot;

public partial class MainWindow
{
    // ─── Deletion Engine ────────────────────────────────────────────────

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
            await NoteStore.DeleteNoteAsync(tab.Id);
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
        var focusTarget = ViewModel.RemoveTab(tab);
        RebuildTabList();

        if (focusTarget is not null)
            await ApplyFocusCascadeAsync(focusTarget);
        else if (_tabs.Count == 0)
            await CreateNewTabAsync();

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
        SaveCurrentTabContent();
        await CommitPendingDeletionAsync();

        var (removed, originalIndexes, focusTarget) = ViewModel.RemoveMultiple(candidates);
        if (removed.Count == 0) return;

        RebuildTabList();

        if (focusTarget is not null)
            await ApplyFocusCascadeAsync(focusTarget);
        else if (_tabs.Count == 0)
            await CreateNewTabAsync();

        var cts = new CancellationTokenSource();
        _pendingDeletion = new PendingDeletion(removed, originalIndexes, cts);
        ShowToast(isBulk: true, count: removed.Count);
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
    private Task UndoDeleteAsync()
    {
        if (_pendingDeletion is null) return Task.CompletedTask;

        var pending = _pendingDeletion;
        _pendingDeletion = null;

        pending.Cts.Cancel();
        pending.Cts.Dispose();

        ViewModel.RestoreTabs(pending.Tabs, pending.OriginalIndexes);
        RebuildTabList();
        SelectTabByNote(pending.Tabs[0]);
        HideToast();

        return Task.CompletedTask;
    }

    /// <summary>
    /// Applies focus to the cascade target after a deletion.
    /// If the target is null and search is active, clears search and retries.
    /// If no tabs exist at all, creates a new empty tab.
    /// </summary>
    private async Task ApplyFocusCascadeAsync(NoteTab? target)
    {
        if (target is not null)
        {
            SelectTabByNote(target);
            return;
        }

        if (!string.IsNullOrEmpty(_searchText))
        {
            // Search is hiding everything — clear it and retry
            SearchBox.Text = "";
            _searchText = "";
            SearchPlaceholder.Visibility = Visibility.Visible;
            RebuildTabList();

            var retryTarget = ViewModel.GetFocusCascadeTarget(0);
            await ApplyFocusCascadeAsync(retryTarget);
            return;
        }

        // No tabs at all — auto-create
        await CreateNewTabAsync();
    }

    // ─── Toast Overlay ──────────────────────────────────────────────────

    /// <summary>
    /// Sets toast text for a single-tab deletion: e.g. "Note name" deleted
    /// with the name portion in italic. Truncates raw label to 45 chars.
    /// </summary>
    private void UpdateToastContent(string rawLabel)
    {
        string truncated = rawLabel.Length > 45 ? rawLabel[..45] : rawLabel;

        ToastMessageBlock.Inlines.Clear();
        ToastMessageBlock.Inlines.Add(new System.Windows.Documents.Run("\u201C"));
        var italicRun = new System.Windows.Documents.Run(truncated) { FontStyle = FontStyles.Italic };
        ToastMessageBlock.Inlines.Add(italicRun);
        var suffix = Strings.Toast_Deleted.Replace("{0}", "");
        ToastMessageBlock.Inlines.Add(new System.Windows.Documents.Run("\u201D" + suffix));
    }

    /// <summary>
    /// Sets toast text for a bulk deletion: "{count} notes deleted".
    /// </summary>
    private void UpdateToastContentBulk(int count)
    {
        ToastMessageBlock.Inlines.Clear();
        var fmt = LanguageService.Plural(Strings.Toast_NotesDeleted_One, Strings.Toast_NotesDeleted_Few, Strings.Toast_NotesDeleted, count);
        ToastMessageBlock.Inlines.Add(new System.Windows.Documents.Run(string.Format(fmt, count)));
    }

    /// <summary>
    /// Slides the toast in if not already visible.
    /// Returns true if the toast was newly shown, false if it was already visible (content-only update).
    /// Caller must set ToastMessageBlock content and UndoButton visibility before calling.
    /// </summary>
    private bool AnimateToastIn()
    {
        if (ToastBorder.Visibility == Visibility.Visible)
            return false;

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
        return true;
    }

    /// <summary>
    /// Shows the toast with a slide-up animation.
    /// If toast is already visible: only updates content — no re-animation.
    /// </summary>
    private void ShowToast(bool isBulk, string? label = null, int count = 0)
    {
        _pendingToastUndoAction = null;

        if (isBulk)
            UpdateToastContentBulk(count);
        else
            UpdateToastContent(label ?? "");

        AnimateToastIn();
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
    /// Undo button click handler — handles both deletion undo and replace-all undo.
    /// </summary>
    private void UndoToast_Click(object sender, MouseButtonEventArgs e)
    {
        if (_pendingToastUndoAction is not null)
        {
            var action = _pendingToastUndoAction;
            _pendingToastUndoAction = null;
            HideToast();
            action();
            return;
        }

        _ = UndoDeleteAsync();
    }

    /// <summary>
    /// Shows a toast with a message and an undo button that invokes the pending undo action.
    /// Auto-dismisses after 4 seconds.
    /// </summary>
    private void ShowUndoableToast(string message)
    {
        ToastMessageBlock.Inlines.Clear();
        ToastMessageBlock.Inlines.Add(new System.Windows.Documents.Run(message));
        UndoButton.Visibility = Visibility.Visible;

        if (!AnimateToastIn()) return;

        _ = Task.Delay(4000).ContinueWith(_ =>
        {
            Dispatcher.Invoke(() =>
            {
                _pendingToastUndoAction = null;
                HideToast();
            });
        });
    }
}
