using JoJot.Models;
using JoJot.Resources;
using JoJot.Services;

namespace JoJot;

public partial class MainWindow
{
    // ─── Cleanup Panel ──────────────────────────────────────────────────

    private void ShowCleanupPanel()
    {
        if (_cleanupPanelOpen)
        {
            HideCleanupPanel();
            return;
        }

        // One-panel-at-a-time
        if (_preferencesOpen) HidePreferencesPanel();
        if (_recoveryPanelOpen) HideRecoveryPanel();
        if (_findPanelOpen) HideFindPanel();

        CleanupPanel.ResetFilters();
        _cleanupPanelOpen = true;
        CleanupPanel.Show();
        RefreshCleanupPreview();
    }

    private void HideCleanupPanel()
    {
        if (!_cleanupPanelOpen) return;
        _cleanupPanelOpen = false;
        CleanupPanel.Hide();
    }

    private void RefreshCleanupPreview()
    {
        CleanupPanel.RefreshPreview(GetCleanupCandidates());
    }

    /// <summary>
    /// Parses the age input and unit from UserControl, delegates to ViewModel.
    /// </summary>
    private DateTime? GetCleanupCutoffDate()
    {
        if (!int.TryParse(CleanupPanel.AgeText, out int age))
            return null;

        return ViewModels.MainWindowViewModel.GetCleanupCutoffDate(age, CleanupPanel.UnitIndex, DateTime.Now);
    }

    /// <summary>
    /// Returns tabs matching the current cleanup filter, delegating to ViewModel.
    /// </summary>
    private List<NoteTab> GetCleanupCandidates()
    {
        var cutoff = GetCleanupCutoffDate();
        if (cutoff is null) return [];

        return ViewModel.GetCleanupCandidates(cutoff.Value, CleanupPanel.IncludePinned);
    }

    /// <summary>
    /// Permanently deletes all matched cleanup candidates (hard-delete, no soft-delete/undo toast).
    /// After deletion, rebuilds the tab list, handles active tab fallback, and refreshes the cleanup panel.
    /// </summary>
    private async Task ExecuteCleanupDeleteAsync(List<NoteTab> candidates)
    {
        // Save current content before deletion
        SaveCurrentTabContent();

        // Commit any existing pending soft-deletion first (from single-tab delete)
        await CommitPendingDeletionAsync();

        bool wasActiveDeleted = _activeTab is not null && candidates.Any(t => t.Id == _activeTab.Id);
        int activeOriginalIndex = wasActiveDeleted ? _tabs.IndexOf(_activeTab!) : 0;

        // Remove from in-memory collection
        foreach (var tab in candidates)
            _tabs.Remove(tab);

        // Hard-delete from database (permanent, no undo)
        foreach (var tab in candidates)
        {
            await NoteStore.DeleteNoteAsync(tab.Id);
            UndoManager.Instance.RemoveStack(tab.Id);
        }

        // Rebuild tab list UI
        RebuildTabList();

        // Handle active tab fallback
        if (wasActiveDeleted)
        {
            var focusTarget = ViewModel.GetFocusCascadeTarget(activeOriginalIndex);
            await ApplyFocusCascadeAsync(focusTarget);
        }

        // Refresh the cleanup panel preview list (panel stays open)
        if (_cleanupPanelOpen)
        {
            RefreshCleanupPreview();
        }

        // Show confirmation toast (no undo — cleanup deletion is permanent)
        int deleted = candidates.Count;
        var fmt = LanguageService.Plural(Strings.Toast_CleanedUp_One, Strings.Toast_CleanedUp_Few, Strings.Toast_CleanedUp, deleted);
        ShowInfoToast(string.Format(fmt, deleted));
    }

    /// <summary>
    /// Extracts ~50 char content excerpt for display in cleanup preview rows.
    /// Delegates to ViewModel.
    /// </summary>
    private static string GetCleanupExcerpt(NoteTab tab) =>
        ViewModels.MainWindowViewModel.GetCleanupExcerpt(tab);
}
