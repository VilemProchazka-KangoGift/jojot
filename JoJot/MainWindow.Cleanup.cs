using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using JoJot.Models;
using JoJot.Services;

namespace JoJot;

public partial class MainWindow
{
    // ─── Cleanup Panel ────────────

    /// <summary>
    /// Opens the cleanup side panel and populates it with the default filter.
    /// Toggles closed if already open.
    /// </summary>
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

        // Reset filter to defaults
        CleanupAgeInput.Text = "7";
        CleanupUnitCombo.SelectedIndex = 0; // "days"
        CleanupIncludePinned.IsChecked = false;

        _cleanupPanelOpen = true;
        CleanupPanel.Visibility = Visibility.Visible;

        // Populate preview list with default filter
        RefreshCleanupPreview();

        var anim = new DoubleAnimation
        {
            From = 320, To = 0,
            Duration = TimeSpan.FromMilliseconds(250),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        CleanupPanelTransform.BeginAnimation(TranslateTransform.XProperty, anim);
    }

    private void HideCleanupPanel()
    {
        if (!_cleanupPanelOpen) return;
        _cleanupPanelOpen = false;

        var anim = new DoubleAnimation
        {
            From = 0, To = 320,
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        anim.Completed += (_, _) =>
        {
            CleanupPanel.Visibility = Visibility.Collapsed;
            CleanupPanelTransform.BeginAnimation(TranslateTransform.XProperty, null);
            CleanupPanelTransform.X = 320;
        };
        CleanupPanelTransform.BeginAnimation(TranslateTransform.XProperty, anim);
    }

    private void CleanupClose_Click(object sender, MouseButtonEventArgs e)
    {
        HideCleanupPanel();
    }

    private void CleanupDelete_Click(object sender, MouseButtonEventArgs e)
    {
        var candidates = GetCleanupCandidates();
        if (candidates.Count == 0) return;

        // Build confirmation message
        int pinnedCount = candidates.Count(t => t.Pinned);
        string pinnedNote = pinnedCount > 0 ? $" (including {pinnedCount} pinned)" : "";
        string message = $"This will permanently delete {candidates.Count} tab{(candidates.Count == 1 ? "" : "s")}{pinnedNote}. This cannot be undone.";

        ShowConfirmation(
            "Clean up tabs",
            message,
            () => _ = ExecuteCleanupDeleteAsync(candidates)
        );
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
            await DatabaseService.DeleteNoteAsync(tab.Id);
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
        ShowInfoToast($"{deleted} tab{(deleted == 1 ? "" : "s")} cleaned up");
    }

    private void CleanupAgeInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_cleanupPanelOpen) RefreshCleanupPreview();
    }

    private void CleanupUnitCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_cleanupPanelOpen) RefreshCleanupPreview();
    }

    private void CleanupIncludePinned_Changed(object sender, RoutedEventArgs e)
    {
        if (_cleanupPanelOpen) RefreshCleanupPreview();
    }

    /// <summary>
    /// Parses the age input and unit from UI controls, delegates to ViewModel.
    /// </summary>
    private DateTime? GetCleanupCutoffDate()
    {
        if (!int.TryParse(CleanupAgeInput.Text, out int age))
            return null;

        return ViewModels.MainWindowViewModel.GetCleanupCutoffDate(age, CleanupUnitCombo.SelectedIndex, DateTime.Now);
    }

    /// <summary>
    /// Returns tabs matching the current cleanup filter, delegating to ViewModel.
    /// </summary>
    private List<NoteTab> GetCleanupCandidates()
    {
        var cutoff = GetCleanupCutoffDate();
        if (cutoff is null) return new List<NoteTab>();

        bool includePinned = CleanupIncludePinned.IsChecked == true;
        return ViewModel.GetCleanupCandidates(cutoff.Value, includePinned);
    }

    /// <summary>
    /// Rebuilds the cleanup preview list UI based on current filter criteria.
    /// Called from ShowCleanupPanel and all filter change handlers.
    /// </summary>
    private void RefreshCleanupPreview()
    {
        CleanupPreviewList.Children.Clear();

        var candidates = GetCleanupCandidates();

        // Update delete button text and enabled state
        if (candidates.Count > 0)
        {
            CleanupDeleteText.Text = $"Delete {candidates.Count} tab{(candidates.Count == 1 ? "" : "s")}";
            CleanupDeleteButton.IsEnabled = true;
            CleanupDeleteButton.Opacity = 1.0;
        }
        else
        {
            CleanupDeleteText.Text = "Delete 0 tabs";
            CleanupDeleteButton.IsEnabled = false;
            CleanupDeleteButton.Opacity = 0.5;
        }

        // Empty state
        if (candidates.Count == 0)
        {
            var emptyBlock = new TextBlock
            {
                Text = "No tabs match this filter",
                FontSize = 13,
                FontStyle = FontStyles.Italic,
                Margin = new Thickness(0, 8, 0, 0)
            };
            emptyBlock.SetResourceReference(TextBlock.ForegroundProperty, "c-text-muted");
            CleanupPreviewList.Children.Add(emptyBlock);
            return;
        }

        // Build preview rows
        for (int i = 0; i < candidates.Count; i++)
        {
            var tab = candidates[i];
            bool isLast = (i == candidates.Count - 1);
            CleanupPreviewList.Children.Add(CreateCleanupPreviewRow(tab, isLast));
        }
    }

    /// <summary>
    /// Creates a single cleanup preview row showing tab title, content excerpt, and relative age.
    /// </summary>
    private FrameworkElement CreateCleanupPreviewRow(NoteTab tab, bool isLast)
    {
        var container = new StackPanel { Margin = new Thickness(0, 6, 0, 6) };

        // Title line with optional pin icon
        var titleBlock = new TextBlock
        {
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        // Pin icon prefix for pinned tabs
        if (tab.Pinned)
        {
            var pinRun = new System.Windows.Documents.Run("\uE718 ")
            {
                FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                FontSize = 10
            };
            pinRun.SetResourceReference(System.Windows.Documents.Run.ForegroundProperty, "c-text-muted");
            titleBlock.Inlines.Add(pinRun);
        }

        // Tab name
        string displayName = tab.DisplayLabel;
        titleBlock.Inlines.Add(new System.Windows.Documents.Run(displayName)
        {
            FontWeight = FontWeights.Normal
        });

        // Content excerpt suffix (em-dash + italic)
        string excerpt = GetCleanupExcerpt(tab);
        if (!string.IsNullOrEmpty(excerpt))
        {
            var excerptRun = new System.Windows.Documents.Run($" \u2014 {excerpt}")
            {
                FontStyle = FontStyles.Italic
            };
            excerptRun.SetResourceReference(System.Windows.Documents.Run.ForegroundProperty, "c-text-muted");
            titleBlock.Inlines.Add(excerptRun);
        }

        titleBlock.SetResourceReference(TextBlock.ForegroundProperty, "c-text-primary");
        container.Children.Add(titleBlock);

        // Date row: created (left) + updated (right) — matches tab item layout
        var dateRow = new Grid { Margin = new Thickness(0, 2, 0, 0) };
        var createdBlock = new TextBlock
        {
            Text = tab.CreatedDisplay,
            FontSize = 10,
            HorizontalAlignment = HorizontalAlignment.Left,
            ToolTip = NoteTab.CreatedTooltip(tab.CreatedAt)
        };
        createdBlock.SetResourceReference(TextBlock.ForegroundProperty, "c-text-muted");
        dateRow.Children.Add(createdBlock);
        var updatedBlock = new TextBlock
        {
            Text = tab.UpdatedDisplay,
            FontSize = 10,
            HorizontalAlignment = HorizontalAlignment.Right,
            ToolTip = NoteTab.UpdatedTooltip(tab.UpdatedAt)
        };
        updatedBlock.SetResourceReference(TextBlock.ForegroundProperty, "c-text-muted");
        dateRow.Children.Add(updatedBlock);
        container.Children.Add(dateRow);

        // Divider (unless last item)
        if (!isLast)
        {
            var wrapper = new StackPanel();
            wrapper.Children.Add(container);
            var divider = new Separator
            {
                Margin = new Thickness(0, 2, 0, 0)
            };
            divider.SetResourceReference(Separator.BackgroundProperty, "c-border");
            wrapper.Children.Add(divider);
            return wrapper;
        }

        return container;
    }

    /// <summary>
    /// Extracts ~50 char content excerpt for display in cleanup preview rows.
    /// Delegates to ViewModel.
    /// </summary>
    private static string GetCleanupExcerpt(NoteTab tab) =>
        ViewModels.MainWindowViewModel.GetCleanupExcerpt(tab);
}
