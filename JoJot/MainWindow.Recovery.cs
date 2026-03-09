using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using JoJot.Models;
using JoJot.Services;

namespace JoJot;

public partial class MainWindow
{
    // ─── Recovery Flyout Panel ────────────

    /// <summary>
    /// Opens the recovery flyout panel and populates it with orphaned session cards.
    /// Toggles closed if already open.
    /// </summary>
    private async void ShowRecoveryPanel()
    {
        try
        {
            if (_recoveryPanelOpen)
            {
                HideRecoveryPanel();
                return;
            }

            // One-panel-at-a-time — close preferences if open
            if (_preferencesOpen)
            {
                HidePreferencesPanel();
            }

            if (_cleanupPanelOpen)
            {
                HideCleanupPanel();
            }

            var orphanGuids = VirtualDesktopService.OrphanedSessionGuids;
            if (orphanGuids.Count == 0)
            {
                return;
            }

            var orphanInfos = await SessionStore.GetOrphanedSessionInfoAsync(orphanGuids);
            RecoveryPanel.SessionList_.Children.Clear();

            var orphanList = orphanInfos.ToList();
            for (int i = 0; i < orphanList.Count; i++)
            {
                var (guid, desktopName, tabCount, lastUpdated) = orphanList[i];
                var tabPreviews = await NoteStore.GetNotePreviewsForDesktopAsync(guid, 5);
                var totalCount = await NoteStore.GetNoteCountForDesktopAsync(guid);
                bool isLast = (i == orphanList.Count - 1);
                RecoveryPanel.SessionList_.Children.Add(CreateRecoveryRow(guid, desktopName, tabCount, lastUpdated, tabPreviews, totalCount, isLast));
            }

            if (RecoveryPanel.SessionList_.Children.Count == 0)
            {
                return;
            }

            _recoveryPanelOpen = true;
            RecoveryPanel.Show();
        }
        catch (Exception ex)
        {
            LogService.Warn("Failed to show recovery panel: {ErrorMessage}", ex.Message);
        }
    }

    private void HideRecoveryPanel()
    {
        if (!_recoveryPanelOpen) return;
        _recoveryPanelOpen = false;
        RecoveryPanel.Hide();
    }

    /// <summary>
    /// Creates a flat row for an orphaned session in the recovery panel.
    /// Shows desktop name (bold), tab count + date (muted), individual tab previews
    /// (name + excerpt), "+N more" if excess, and Adopt/Delete buttons.
    /// </summary>
    private FrameworkElement CreateRecoveryRow(string guid, string? desktopName, int tabCount,
        DateTime lastUpdated, List<(string? Name, string Excerpt, DateTime CreatedAt, DateTime UpdatedAt)> tabPreviews, int totalNoteCount, bool isLast)
    {
        var container = new StackPanel
        {
            Margin = new Thickness(0, 10, 0, 10)
        };

        // Registry fallback for orphaned desktop names not stored in DB
        if (string.IsNullOrEmpty(desktopName) && Guid.TryParse(guid, out var desktopGuid))
        {
            var regName = Interop.VirtualDesktopInterop.GetDesktopNameFromRegistry(desktopGuid);
            if (!string.IsNullOrEmpty(regName))
                desktopName = regName;
        }

        var finalName = string.IsNullOrEmpty(desktopName) ? "Unknown desktop" : desktopName;

        // Desktop name (bold, primary color)
        var nameBlock = new TextBlock
        {
            Text = finalName,
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        nameBlock.SetResourceReference(TextBlock.ForegroundProperty, "c-text-primary");
        container.Children.Add(nameBlock);

        // Metadata row (tab count + date, muted)
        var metaBlock = new TextBlock
        {
            Text = $"{tabCount} tab{(tabCount == 1 ? "" : "s")} \u00B7 {lastUpdated:MMM d, yyyy}",
            FontSize = 11,
            Margin = new Thickness(0, 2, 0, 6)
        };
        metaBlock.SetResourceReference(TextBlock.ForegroundProperty, "c-text-muted");
        container.Children.Add(metaBlock);

        // Tab preview lines (matching cleanup panel style)
        foreach (var (name, excerpt, createdAt, updatedAt) in tabPreviews)
        {
            var tabContainer = new StackPanel { Margin = new Thickness(0, 3, 0, 3) };

            string displayExcerpt = excerpt.Length > 50 ? excerpt[..50] + "..." : excerpt;
            displayExcerpt = displayExcerpt.Replace('\n', ' ').Replace('\r', ' ');

            // Title line with optional excerpt
            var lineBlock = new TextBlock
            {
                FontSize = 11,
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            if (name is not null)
            {
                lineBlock.Inlines.Add(new System.Windows.Documents.Run(name)
                {
                    FontWeight = FontWeights.Normal
                });
                if (!string.IsNullOrEmpty(displayExcerpt))
                {
                    var dashRun = new System.Windows.Documents.Run($" \u2014 {displayExcerpt}")
                    {
                        FontStyle = FontStyles.Italic
                    };
                    dashRun.SetResourceReference(System.Windows.Documents.Run.ForegroundProperty, "c-text-muted");
                    lineBlock.Inlines.Add(dashRun);
                }
            }
            else if (!string.IsNullOrEmpty(displayExcerpt))
            {
                lineBlock.Text = displayExcerpt;
            }
            else
            {
                lineBlock.Text = "Empty note";
                lineBlock.FontStyle = FontStyles.Italic;
            }

            lineBlock.SetResourceReference(TextBlock.ForegroundProperty, "c-text-primary");
            tabContainer.Children.Add(lineBlock);

            // Date row: created (left) + updated (right)
            var dateRow = new Grid { Margin = new Thickness(0, 1, 0, 0) };
            var createdBlock = new TextBlock
            {
                Text = NoteTab.FormatCreatedDisplay(createdAt),
                FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Left,
                ToolTip = NoteTab.CreatedTooltip(createdAt)
            };
            createdBlock.SetResourceReference(TextBlock.ForegroundProperty, "c-text-muted");
            dateRow.Children.Add(createdBlock);
            var updatedBlock = new TextBlock
            {
                Text = NoteTab.FormatUpdatedDisplay(updatedAt),
                FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Right,
                ToolTip = NoteTab.UpdatedTooltip(updatedAt)
            };
            updatedBlock.SetResourceReference(TextBlock.ForegroundProperty, "c-text-muted");
            dateRow.Children.Add(updatedBlock);
            tabContainer.Children.Add(dateRow);

            container.Children.Add(tabContainer);
        }

        // "+N more" line (if totalNoteCount > tabPreviews.Count)
        int remaining = totalNoteCount - tabPreviews.Count;
        if (remaining > 0)
        {
            var moreBlock = new TextBlock
            {
                Text = $"+{remaining} more",
                FontSize = 11,
                FontStyle = FontStyles.Italic,
                Margin = new Thickness(0, 1, 0, 1)
            };
            moreBlock.SetResourceReference(TextBlock.ForegroundProperty, "c-text-muted");
            container.Children.Add(moreBlock);
        }

        // Button row (Adopt left, Delete right)
        var buttonPanel = new DockPanel
        {
            Margin = new Thickness(0, 8, 0, 0),
            LastChildFill = false
        };

        Button CreateRowButton(string text, bool isDestructive = false)
        {
            var btn = new Button
            {
                Content = text,
                FontSize = 11,
                MinWidth = 45,
                Height = 24,
                Cursor = Cursors.Hand,
                Padding = new Thickness(8, 2, 8, 2),
                BorderThickness = new Thickness(1)
            };
            btn.SetResourceReference(Button.BorderBrushProperty, "c-border");
            if (isDestructive)
            {
                btn.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xe7, 0x4c, 0x3c));
                btn.Foreground = System.Windows.Media.Brushes.White;
                btn.BorderThickness = new Thickness(0);
            }
            else
            {
                btn.Background = System.Windows.Media.Brushes.Transparent;
                btn.SetResourceReference(Button.ForegroundProperty, "c-text-primary");
            }
            return btn;
        }

        // Adopt — merge tabs into current desktop
        var adoptBtn = CreateRowButton("Adopt");
        DockPanel.SetDock(adoptBtn, Dock.Left);
        adoptBtn.Click += async (s, e) =>
        {
            await NoteStore.MigrateTabsAsync(guid, _desktopGuid);
            await SessionStore.DeleteSessionAndNotesAsync(guid);
            RemoveOrphanGuid(guid);
            await RefreshAfterOrphanAction();
        };
        buttonPanel.Children.Add(adoptBtn);

        // Delete — permanently delete session and all its notes
        var deleteBtn = CreateRowButton("Delete", isDestructive: true);
        DockPanel.SetDock(deleteBtn, Dock.Right);
        deleteBtn.Click += async (s, e) =>
        {
            await SessionStore.DeleteSessionAndNotesAsync(guid);
            RemoveOrphanGuid(guid);
            await RefreshAfterOrphanAction();
        };
        buttonPanel.Children.Add(deleteBtn);

        container.Children.Add(buttonPanel);

        // Wrap row + optional divider in outer container
        if (!isLast)
        {
            var wrapper = new StackPanel();
            wrapper.Children.Add(container);
            var divider = new Border
            {
                Height = 1,
                Margin = new Thickness(0, 0, 0, 0)
            };
            divider.SetResourceReference(Border.BackgroundProperty, "c-border");
            wrapper.Children.Add(divider);
            return wrapper;
        }

        return container;
    }

    /// <summary>
    /// Removes a GUID from the orphaned session list after a recovery action.
    /// </summary>
    private void RemoveOrphanGuid(string guid)
    {
        var list = VirtualDesktopService.OrphanedSessionGuids.ToList();
        list.Remove(guid);
        VirtualDesktopService.SetOrphanedSessionGuids(list);
    }

    /// <summary>
    /// Refreshes recovery panel and badge after an orphan action.
    /// Closes the panel if no orphans remain. Always reloads tabs.
    /// </summary>
    private async Task RefreshAfterOrphanAction()
    {
        UpdateOrphanBadge();

        if (VirtualDesktopService.OrphanedSessionGuids.Count == 0)
        {
            HideRecoveryPanel();
            await LoadTabsAsync();
            return;
        }

        // Refresh rows for remaining orphans
        var orphanInfos = await SessionStore.GetOrphanedSessionInfoAsync(
            VirtualDesktopService.OrphanedSessionGuids);
        RecoveryPanel.SessionList_.Children.Clear();
        var orphanList = orphanInfos.ToList();
        for (int i = 0; i < orphanList.Count; i++)
        {
            var (guid, name, tabCount, lastUpdated) = orphanList[i];
            var tabPreviews = await NoteStore.GetNotePreviewsForDesktopAsync(guid, 5);
            var totalCount = await NoteStore.GetNoteCountForDesktopAsync(guid);
            bool isLast = (i == orphanList.Count - 1);
            RecoveryPanel.SessionList_.Children.Add(CreateRecoveryRow(guid, name, tabCount, lastUpdated, tabPreviews, totalCount, isLast));
        }

        // Reload tabs in case Adopt added new tabs to this desktop
        await LoadTabsAsync();
    }

    /// <summary>
    /// Updates the hamburger badge dot and "Recover sessions" color based on orphan count.
    /// Badge dot (7px, accent-colored) appears when orphans exist; disappears when all resolved.
    /// </summary>
    public void UpdateOrphanBadge()
    {
        bool hasOrphans = VirtualDesktopService.OrphanedSessionGuids.Count > 0;
        OrphanBadge.Visibility = hasOrphans ? Visibility.Visible : Visibility.Collapsed;
        MenuRecover.Visibility = hasOrphans ? Visibility.Visible : Visibility.Collapsed; // Hide entire menu item
        MenuRecoverText.SetResourceReference(TextBlock.ForegroundProperty,
            hasOrphans ? "c-accent" : "c-text-primary");
    }
}
