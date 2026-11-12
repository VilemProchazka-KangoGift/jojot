using System.Globalization;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using JoJot.Models;
using JoJot.Resources;
using JoJot.Services;

namespace JoJot.AvaloniaExperiment;

public sealed partial class MainWindow
{
    internal static Task<int> RunStartupAutoDeleteAsync() => RunStartupAutoDeleteAsync(DateTime.Now);

    internal static async Task<int> RunStartupAutoDeleteAsync(DateTime now)
    {
        var days = await PreferenceStore.GetPreferenceAsync("auto_delete_days");
        var cutoff = GetTabCleanupCutoff(days, 0, now);
        return cutoff is { } date ? await NoteStore.DeleteOldNotesAsync(date) : 0;
    }

    internal static DateTime? GetTabCleanupCutoff(string? ageText, int unitIndex, DateTime now)
    {
        if (!int.TryParse(ageText, out var age) || age < 1)
            return null;

        var days = unitIndex switch { 1 => age / 24d, 2 => age * 7d, 3 => age * 30d, _ => age };
        var elapsedDays = (now - DateTime.MinValue).TotalDays;
        return days >= elapsedDays ? DateTime.MinValue : now.AddDays(-days);
    }

    internal static NoteTab[] GetTabCleanupCandidates(IEnumerable<NoteTab> tabs, DateTime? cutoff, bool includePinned) =>
        cutoff is { } date ? tabs.Where(tab => tab.UpdatedAt < date && (includePinned || !tab.Pinned)).ToArray() : [];

    private void ShowCleanup() => _ = RunPanelActionAsync(ShowCleanupAsync);

    private async Task ShowCleanupAsync()
    {
        var body = TogglePanel("cleanup", Strings.Cleanup_Header);
        if (body is null)
            return;

        var savedAutoDelete = await PreferenceStore.GetPreferenceAsync("auto_delete_days");
        if (!ReferenceEquals(body, _panelBody))
            return;

        var autoDeleteDays = new TextBox { Text = savedAutoDelete, Width = 64, MaxLength = 10 };
        AutomationProperties.SetName(autoDeleteDays, Strings.Pref_AutoDelete);
        autoDeleteDays.AddHandler(InputElement.TextInputEvent, (_, e) =>
        {
            if (e.Text is { } text && text.Any(character => !char.IsDigit(character)))
                e.Handled = true;
        }, RoutingStrategies.Tunnel);
        autoDeleteDays.TextChanged += async (_, _) => await RunPanelActionAsync(async () =>
        {
            var value = autoDeleteDays.Text;
            if (string.IsNullOrWhiteSpace(value))
                await PreferenceStore.SetPreferenceAsync("auto_delete_days", "");
            else if (int.TryParse(value, out var days) && days > 0)
                await PreferenceStore.SetPreferenceAsync("auto_delete_days", days.ToString(CultureInfo.InvariantCulture));
        });
        body.Children.Add(PanelText(Strings.Pref_AutoDelete));
        body.Children.Add(PanelRow(autoDeleteDays, PanelText(Strings.Pref_Days)));
        body.Children.Add(PanelText(Strings.Pref_AutoDeleteHint));
        body.Children.Add(new Separator());

        var age = new TextBox { Text = "7", Width = 64, MaxLength = 10 };
        AutomationProperties.SetName(age, Strings.Cleanup_OlderThan);
        var units = new ComboBox
        {
            ItemsSource = new[] { Strings.Cleanup_UnitDays, Strings.Cleanup_UnitHours, Strings.Cleanup_UnitWeeks, Strings.Cleanup_UnitMonths },
            SelectedIndex = 0,
        };
        AutomationProperties.SetName(units, Strings.Cleanup_OlderThan);
        var includePinned = new CheckBox { Content = Strings.Cleanup_IncludePinned };
        var previews = new StackPanel { Spacing = 6 };
        NoteTab[] candidates = [];
        var delete = new Button { Content = Strings.Cleanup_Delete0 };
        delete.Click += async (_, _) => await RunPanelActionAsync(async () =>
        {
            await DeleteCleanupCandidatesAsync(candidates);
            RefreshPreview();
        });

        void RefreshPreview()
        {
            CaptureEditorText();
            candidates = GetTabCleanupCandidates(_notes, GetTabCleanupCutoff(age.Text, units.SelectedIndex, DateTime.Now),
                includePinned.IsChecked == true);
            var count = candidates.Length;
            var template = PanelLanguage.Plural(Strings.Cleanup_DeleteN_One, Strings.Cleanup_DeleteN_Few, Strings.Cleanup_DeleteN, count);
            delete.Content = count == 0 ? Strings.Cleanup_Delete0 : string.Format(template, count);
            delete.IsEnabled = count != 0;
            previews.Children.Clear();
            if (count == 0)
                previews.Children.Add(PanelText(Strings.Cleanup_NoMatch));
            foreach (var note in candidates)
            {
                var title = note.Pinned ? $"\u25CF {note.DisplayLabel}" : note.DisplayLabel;
                previews.Children.Add(NotePreview(title, note.Name is null ? "" : Excerpt(note.Content), note.CreatedAt, note.UpdatedAt));
            }
        }

        age.TextChanged += (_, _) => RefreshPreview();
        units.SelectionChanged += (_, _) => RefreshPreview();
        includePinned.IsCheckedChanged += (_, _) => RefreshPreview();
        body.Children.Add(PanelText(Strings.Cleanup_OlderThan));
        body.Children.Add(PanelRow(age, units));
        body.Children.Add(includePinned);
        body.Children.Add(delete);
        body.Children.Add(previews);
        RefreshPreview();
    }

    private async Task DeleteCleanupCandidatesAsync(NoteTab[] candidates)
    {
        var count = candidates.Length;
        if (count == 0)
            return;
        var pinnedCount = candidates.Count(note => note.Pinned);
        var pinned = pinnedCount > 0 ? string.Format(Strings.Cleanup_ConfirmPinnedNote, pinnedCount) : "";
        var template = PanelLanguage.Plural(Strings.Cleanup_ConfirmMessage_One, Strings.Cleanup_ConfirmMessage_Few,
            Strings.Cleanup_ConfirmMessage, count);
        if (!await ConfirmAsync(Strings.Cleanup_ConfirmTitle, PanelText(string.Format(template, count, pinned)), Strings.Common_Delete))
            return;

        int deleted = 0;
        await RunActionAsync(async () =>
        {
            await FlushEditsAsync();
            await CommitPendingDeletionAsync();
            var selectedIndex = _active is not null && candidates.Contains(_active) ? _notes.IndexOf(_active) : -1;
            deleted = await NoteStore.DeleteNotesAsync(candidates.Select(note => note.Id).ToArray());
            foreach (var note in candidates)
                UndoManager.Instance.RemoveStack(note.Id);
            await ReloadNotesCoreAsync();
            if (selectedIndex >= 0 && _viewModel.GetFocusCascadeTarget(selectedIndex) is { } next)
                ShowNote(next);
        });
        if (deleted > 0)
        {
            var format = PanelLanguage.Plural(Strings.Toast_CleanedUp_One, Strings.Toast_CleanedUp_Few, Strings.Toast_CleanedUp, deleted);
            ShowInfoToast(string.Format(format, deleted));
        }
    }

    private static string Excerpt(string content)
    {
        var trimmed = content.TrimStart();
        var summary = trimmed.Length > 50 ? trimmed[..50] + "..." : trimmed;
        return summary.Replace('\r', ' ').Replace('\n', ' ');
    }

    private static Control NotePreview(string title, string excerpt, DateTime created, DateTime updated)
    {
        var line = new TextBlock
        {
            Text = string.IsNullOrEmpty(excerpt) ? title : $"{title} \u2014 {excerpt}",
            TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 12,
        };
        var createdText = new TextBlock { Text = NoteTab.FormatCreatedDisplay(created), FontSize = 10 };
        var updatedText = new TextBlock { Text = NoteTab.FormatUpdatedDisplay(updated), FontSize = 10 };
        ToolTip.SetTip(createdText, NoteTab.CreatedTooltip(created));
        ToolTip.SetTip(updatedText, NoteTab.UpdatedTooltip(updated));
        return new StackPanel { Spacing = 3, Children = { line, PanelRow(createdText, updatedText) } };
    }

    private static readonly SemaphoreSlim OrphanRecoveryGate = new(1, 1);

    internal void UpdateOrphanBadge()
    {
        var hasRecovery = OrphanSessionGuids?.Invoke().Count > 0 || ReviewPendingMoves?.Invoke().Count > 0;
        RecoveryMenuItem.IsVisible = hasRecovery;
        HamburgerButton.Content = hasRecovery ? "\u2630 \u2022" : "\u2630";
        ToolTip.SetTip(HamburgerButton, hasRecovery ? Strings.Menu_Recover : Strings.Tooltip_Menu);
    }

    internal void NotifyOrphansChanged()
    {
        UpdateOrphanBadge();
        if (_currentPanel == "recovery")
            DismissSidePanel();
    }

    private void ShowRecovery() => _ = RunPanelActionAsync(async () =>
    {
        var body = TogglePanel("recovery", Strings.Recovery_Header);
        if (body is not null)
            await LoadRecoveryPanelAsync(body);
    });

    private async Task LoadRecoveryPanelAsync(StackPanel body)
    {
        var guids = OrphanSessionGuids?.Invoke().Where(guid => !string.Equals(guid, DesktopGuid, StringComparison.OrdinalIgnoreCase)).ToList() ?? [];
        var moves = ReviewPendingMoves?.Invoke() ?? [];
        var sessions = await SessionStore.GetOrphanedSessionInfoAsync(guids);
        if (!ReferenceEquals(body, _panelBody))
            return;
        if (sessions.Count == 0 && moves.Count == 0)
        {
            DismissSidePanel();
            return;
        }

        body.Children.Clear();
        if (moves.Count > 0)
        {
            await AddPendingMoveReviewsAsync(body, moves);
            if (!ReferenceEquals(body, _panelBody))
                return;
        }
        foreach (var (guid, name, count, lastUpdated) in sessions)
        {
            var notes = await NoteStore.GetNotePreviewsForDesktopAsync(guid, 5);
            if (!ReferenceEquals(body, _panelBody))
                return;

            if (body.Children.Count > 0)
                body.Children.Add(new Separator());
            body.Children.Add(PanelSection(string.IsNullOrWhiteSpace(name) ? Strings.Recovery_UnknownDesktop : name));
            var countText = string.Format(PanelLanguage.Plural(Strings.Recovery_TabCount_One, Strings.Recovery_TabCount_Few,
                Strings.Recovery_TabCount, count), count);
            var date = lastUpdated.ToString("MMM d, yyyy", Strings.Culture);
            body.Children.Add(PanelText($"{countText} \u00B7 {date}"));
            foreach (var (title, excerpt, created, updated) in notes)
            {
                var preview = Excerpt(excerpt);
                var label = title ?? (string.IsNullOrWhiteSpace(preview) ? Strings.Recovery_EmptyNote : preview);
                body.Children.Add(NotePreview(label, title is null ? "" : preview, created, updated));
            }
            if (count > notes.Count)
                body.Children.Add(PanelText(string.Format(Strings.Recovery_More, count - notes.Count)));
            if (moves.Any(move => ReferencesDesktop(move, guid)))
                body.Children.Add(PanelText(CoreLabel("Review the interrupted move before recovering this session.",
                    "Před obnovením této relace zkontrolujte přerušený přesun.")));
            else
                body.Children.Add(PanelRow(
                    PanelAsyncButton(Strings.Recovery_Adopt, () => ResolveOrphanAsync(body, guid, true)),
                    PanelAsyncButton(Strings.Common_Delete, () => ResolveOrphanAsync(body, guid, false))));
        }
    }

    private async Task ResolveOrphanAsync(StackPanel body, string guid, bool adopt)
    {
        if (_recoveryBusy)
            return;
        _recoveryBusy = true;
        body.IsEnabled = false;
        await OrphanRecoveryGate.WaitAsync();
        try
        {
            if (OrphanSessionGuids?.Invoke().Contains(guid) != true ||
                ReviewPendingMoves?.Invoke().Any(move => ReferencesDesktop(move, guid)) == true)
                return;
            await RunActionAsync(async () =>
            {
                await FlushEditsAsync();
                if (adopt)
                    await NoteStore.MigrateTabsAsync(guid, DesktopGuid);
                await SessionStore.DeleteSessionAndNotesAsync(guid);
                OrphanSessionResolved?.Invoke(guid);
                UpdateOrphanBadge();
                foreach (var window in PanelWindows().Where(window => window != this))
                    window.NotifyOrphansChanged();
                await ReloadNotesCoreAsync();
            });
            if (ReferenceEquals(body, _panelBody))
                await LoadRecoveryPanelAsync(body);
        }
        finally
        {
            OrphanRecoveryGate.Release();
            body.IsEnabled = true;
            _recoveryBusy = false;
        }
    }
}
