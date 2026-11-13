using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.VisualTree;
using JoJot.Models;
using JoJot.Resources;
using JoJot.Services;
using Xunit;

namespace JoJot.AvaloniaExperiment.Tests;

public sealed class PanelsTests
{
    [AvaloniaFact]
    public async Task PreferencesAndHelpOpenFromMenuAndStoreThemeAndFont()
    {
        await using var fixture = new MainWindowTests.WindowFixture();
        var window = fixture.Open();
        await window.Ready;
        var panel = Assert.IsType<Border>(window.FindControl<Border>("PanelHost"));
        ClickMenu(window, "PreferencesMenuItem");
        await MainWindowTests.WaitForAsync(() => panel.GetVisualDescendants().OfType<Button>().Any(button => Equals(button.Content, Strings.Pref_ThemeDark)), "preferences content");
        Assert.True(panel.IsVisible);
        var theme = FindButton(panel, Strings.Pref_ThemeDark);
        Click(theme);
        await MainWindowTests.WaitForAsync(() => window.RequestedThemeVariant == ThemeVariant.Dark, "apply dark theme");

        Click(FindButton(panel, "+"));
        var editor = Assert.IsType<TextBox>(window.FindControl<TextBox>("Editor"));
        await MainWindowTests.WaitForAsync(() => editor.FontSize == 14 &&
            window.FindControl<TextBlock>("ToastText")?.Text == "108%", "increase and persist editor font");
        Assert.Equal("dark", await PreferenceStore.GetPreferenceAsync("theme"));
        Assert.Equal("14", await PreferenceStore.GetPreferenceAsync("font_size"));

        Click(FindButton(panel, "New note"));
        await MainWindowTests.WaitForAsync(() => FindButton(panel, "New note").Classes.Contains("active"), "startup choice selected");
        Assert.Equal("new", await PreferenceStore.GetPreferenceAsync("startup_note"));

        Click(FindButton(panel, Strings.Help_Title));
        await MainWindowTests.WaitForAsync(() => panel.GetVisualDescendants().OfType<TextBlock>()
            .Any(text => text.Text == Strings.Help_Find), "shortcut reference");
        Assert.DoesNotContain(panel.GetVisualDescendants().OfType<Button>(), button => button == theme);
        window.KeyPress(Key.F, RawInputModifiers.Control, PhysicalKey.F, null);
        window.KeyRelease(Key.F, RawInputModifiers.Control, PhysicalKey.F, null);
        Assert.False(panel.IsVisible);
        var find = Assert.IsType<Border>(window.FindControl<Border>("FindHost"));
        Assert.True(find.IsVisible);
        var query = find.GetVisualDescendants().OfType<TextBox>().Single(box => box.Name == "FindQuery");
        await MainWindowTests.WaitForAsync(() => query.IsFocused, "find query focus after switching panels");
    }

    [AvaloniaFact]
    public async Task LanguageSelectionPersistsOnlyAfterRestartConfirmation()
    {
        var previousRestart = MainWindow.RestartForLanguageAsync;
        var restartRequested = false;
        MainWindow.RestartForLanguageAsync = () =>
        {
            restartRequested = true;
            return Task.CompletedTask;
        };
        try
        {
            await using var fixture = new MainWindowTests.WindowFixture();
            var window = fixture.Open();
            await window.Ready;
            var panel = Assert.IsType<Border>(window.FindControl<Border>("PanelHost"));
            ClickMenu(window, "PreferencesMenuItem");
            const string czechLabel = "Česky";
            await MainWindowTests.WaitForAsync(() => panel.GetVisualDescendants().OfType<Button>()
                .Any(button => Equals(button.Content, czechLabel)), "Czech language option");
            var languageButton = FindButton(panel, czechLabel);

            Click(languageButton);
            await MainWindowTests.WaitForAsync(() => window.OwnedWindows.Count == 1, "restart confirmation");
            Click(FindButton(Assert.Single(window.OwnedWindows), Strings.Common_Cancel));
            await MainWindowTests.WaitForAsync(() => window.OwnedWindows.Count == 0, "cancelled restart");
            Assert.False(restartRequested);
            Assert.Null(await PreferenceStore.GetPreferenceAsync("language"));

            Click(languageButton);
            await MainWindowTests.WaitForAsync(() => window.OwnedWindows.Count == 1, "restart confirmation again");
            Click(FindButton(Assert.Single(window.OwnedWindows), Strings.Language_RestartButton));
            await MainWindowTests.WaitForAsync(() => restartRequested, "language restart requested");
            Assert.Equal("cs", await PreferenceStore.GetPreferenceAsync("language"));
        }
        finally { MainWindow.RestartForLanguageAsync = previousRestart; }
    }

    [AvaloniaFact]
    public async Task ShortcutRecordingIgnoresUnmodifiedKeysAndCancelsWithEscape()
    {
        var previousPause = MainWindow.PauseGlobalShortcut;
        var previousRecord = MainWindow.RecordGlobalShortcutAsync;
        var previousDisplay = MainWindow.CurrentGlobalShortcut;
        var pauses = new List<bool>();
        var keys = new List<(Key, KeyModifiers)>();
        MainWindow.PauseGlobalShortcut = pauses.Add;
        MainWindow.RecordGlobalShortcutAsync = (key, modifiers) =>
        {
            keys.Add((key, modifiers));
            return Task.FromResult(true);
        };
        MainWindow.CurrentGlobalShortcut = () => keys.Count == 0 ? "Win + Shift + N" : "Ctrl + F9";
        try
        {
            await using var fixture = new MainWindowTests.WindowFixture();
            var window = fixture.Open();
            await window.Ready;
            var panel = Assert.IsType<Border>(window.FindControl<Border>("PanelHost"));
            ClickMenu(window, "PreferencesMenuItem");
            await MainWindowTests.WaitForAsync(() => panel.GetVisualDescendants().OfType<Button>()
                .Any(button => Equals(button.Content, Strings.Pref_HotkeyRecord)), "record button");
            var record = FindButton(panel, Strings.Pref_HotkeyRecord);
            Click(record);
            Assert.Equal(Strings.Pref_HotkeyPressKeys, record.Content);
            PressKey(window, Key.F9, KeyModifiers.None);
            Assert.Empty(keys);
            PressKey(window, Key.Escape, KeyModifiers.None);
            Assert.Equal(Strings.Pref_HotkeyRecord, record.Content);
            Assert.True(panel.IsVisible);

            Click(record);
            PressKey(window, Key.F9, KeyModifiers.Control);
            Assert.Equal((Key.F9, KeyModifiers.Control), Assert.Single(keys));
            Assert.Equal(new[] { true, false, true, false }, pauses);
            Assert.Contains(panel.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Ctrl + F9");
        }
        finally
        {
            MainWindow.PauseGlobalShortcut = previousPause;
            MainWindow.RecordGlobalShortcutAsync = previousRecord;
            MainWindow.CurrentGlobalShortcut = previousDisplay;
        }
    }

    [AvaloniaFact]
    public async Task CleanupConfirmationDeletesOnlyPreviewedNotes()
    {
        await using var fixture = new MainWindowTests.WindowFixture();
        var window = fixture.Open();
        await window.Ready;
        var desktop = window.DesktopGuid;
        var old = await NoteStore.InsertNoteAsync(desktop, "old", "old content", false, 0);
        var pinned = await NoteStore.InsertNoteAsync(desktop, "pinned", "pinned content", true, 1);
        var recent = await NoteStore.InsertNoteAsync(desktop, "recent", "recent content", false, 2);
        await DatabaseCore.ExecuteNonQueryAsync("UPDATE notes SET updated_at = @old WHERE id IN (@one, @two)",
            ("@old", DateTime.Now.AddDays(-30)), ("@one", old), ("@two", pinned));
        await window.ReloadNotesAsync();

        var panel = Assert.IsType<Border>(window.FindControl<Border>("PanelHost"));
        ClickMenu(window, "CleanupMenuItem");
        var one = string.Format(Strings.Cleanup_DeleteN_One, 1);
        await MainWindowTests.WaitForAsync(() => panel.GetVisualDescendants().OfType<Button>().Any(button => Equals(button.Content, one)),
            "single cleanup candidate");
        Click(FindButton(panel, one));
        await MainWindowTests.WaitForAsync(() => window.OwnedWindows.Count == 1, "cleanup confirmation");
        var confirmation = Assert.Single(window.OwnedWindows);
        Click(FindButton(confirmation, Strings.Common_Delete));

        var list = Assert.IsType<ListBox>(window.FindControl<ListBox>("NoteList"));
        await MainWindowTests.WaitForAsync(() => list.Items.OfType<NoteTab>().All(note => note.Id != old), "cleanup list refreshed");
        var remaining = (await NoteStore.GetNotesForDesktopAsync(desktop)).Select(note => note.Id).ToHashSet();
        Assert.DoesNotContain(old, remaining);
        Assert.Contains(pinned, remaining);
        Assert.Contains(recent, remaining);
    }

    [AvaloniaFact]
    public async Task RecoveryAdoptsOrphanNotesAndUpdatesBadge()
    {
        var sessions = new HashSet<string>();
        var previousSessions = MainWindow.OrphanSessionGuids;
        var previousResolved = MainWindow.OrphanSessionResolved;
        MainWindow.OrphanSessionGuids = () => sessions;
        MainWindow.OrphanSessionResolved = guid => sessions.Remove(guid);
        try
        {
            await using var fixture = new MainWindowTests.WindowFixture();
            var window = fixture.Open();
            await window.Ready;
            const string orphan = "old-desktop";
            await SessionStore.CreateSessionAsync(orphan, "Previous desktop", 1);
            var note = await NoteStore.InsertNoteAsync(orphan, "Recovered tab", "Recovered content", true, 0);
            sessions.Add(orphan);
            window.UpdateOrphanBadge();

            var panel = Assert.IsType<Border>(window.FindControl<Border>("PanelHost"));
            var menu = Assert.IsType<MenuItem>(window.FindControl<MenuItem>("RecoveryMenuItem"));
            Assert.True(menu.IsVisible);
            ClickMenu(window, "RecoveryMenuItem");
            await MainWindowTests.WaitForAsync(() => panel.GetVisualDescendants().OfType<Button>()
                .Any(button => Equals(button.Content, Strings.Recovery_Adopt)), "orphan preview");
            Assert.Contains(panel.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Previous desktop");
            Click(FindButton(panel, Strings.Recovery_Adopt));

            await MainWindowTests.WaitForAsync(() => !panel.IsVisible && !menu.IsVisible, "orphan resolved");
            Assert.False(await SessionStore.SessionExistsAsync(orphan));
            var adopted = Assert.Single(await NoteStore.GetNotesForDesktopAsync(window.DesktopGuid), item => item.Id == note);
            Assert.Equal("Recovered content", adopted.Content);
            Assert.False(adopted.Pinned);

            const string toDelete = "another-old-desktop";
            await SessionStore.CreateSessionAsync(toDelete, "Another previous desktop", 2);
            await NoteStore.InsertNoteAsync(toDelete, "To delete", "Unneeded content", false, 0);
            sessions.Add(toDelete);
            window.UpdateOrphanBadge();
            ClickMenu(window, "RecoveryMenuItem");
            await MainWindowTests.WaitForAsync(() => panel.GetVisualDescendants().OfType<Button>()
                .Any(button => Equals(button.Content, Strings.Common_Delete)), "second orphan preview");
            Click(FindButton(panel, Strings.Common_Delete));
            await MainWindowTests.WaitForAsync(() => !panel.IsVisible && !menu.IsVisible, "orphan deleted");
            Assert.Empty(window.OwnedWindows);
            Assert.False(await SessionStore.SessionExistsAsync(toDelete));
            Assert.Empty(await NoteStore.GetNotesForDesktopAsync(toDelete));
        }
        finally
        {
            MainWindow.OrphanSessionGuids = previousSessions;
            MainWindow.OrphanSessionResolved = previousResolved;
        }
    }

    private static Button FindButton(Visual root, string label) =>
        Assert.Single(root.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, label));

    [AvaloniaTheory]
    [InlineData("target")]
    [InlineData(null)]
    public async Task InterruptedMoveReviewClearsOnlyConfirmedMarkerAndPreservesNotes(string? target)
    {
        var oldReview = MainWindow.ReviewPendingMoves;
        var oldResolved = MainWindow.PendingMoveResolved;
        var oldCanOpen = MainWindow.CanOpenRecoveryDesktop;
        var oldOpen = MainWindow.OpenRecoveryDesktopAsync;
        var moves = new List<PendingMove>();
        var opened = new List<string>();
        MainWindow.ReviewPendingMoves = () => moves;
        MainWindow.PendingMoveResolved = id => moves.RemoveAll(move => move.Id == id);
        MainWindow.CanOpenRecoveryDesktop = guid => guid == "source";
        MainWindow.OpenRecoveryDesktopAsync = guid =>
        {
            opened.Add(guid);
            return Task.CompletedTask;
        };
        try
        {
            await using var fixture = new MainWindowTests.WindowFixture();
            var window = fixture.Open();
            await window.Ready;
            const string source = "source";
            const string targetDesktop = "target";
            await SessionStore.CreateSessionAsync(source, "Source notes", 1);
            await SessionStore.CreateSessionAsync(targetDesktop, "Destination notes", 2);
            var sourceId = await NoteStore.InsertNoteAsync(source, "Source tab", "Source content", true, 0);
            var targetId = await NoteStore.InsertNoteAsync(targetDesktop, "Destination tab", "Destination content", false, 0);
            var markerId = await PendingMoveStore.InsertPendingMoveAsync("legacy-window", source, target);
            var otherId = await PendingMoveStore.InsertPendingMoveAsync("legacy-window", "unrelated", null);
            moves.AddRange((await PendingMoveStore.GetPendingMovesAsync()).OrderBy(move => move.Id));
            window.UpdateOrphanBadge();

            var panel = Assert.IsType<Border>(window.FindControl<Border>("PanelHost"));
            var menu = Assert.IsType<MenuItem>(window.FindControl<MenuItem>("RecoveryMenuItem"));
            Assert.True(menu.IsVisible);
            ClickMenu(window, "RecoveryMenuItem");
            const string clear = "Clear pending move";
            Button[] ClearButtons() => panel.GetVisualDescendants().OfType<Button>()
                .Where(button => Equals(button.Content, clear)).ToArray();
            await MainWindowTests.WaitForAsync(() => ClearButtons().Length == 2, "pending move previews");
            var labels = panel.GetVisualDescendants().OfType<TextBlock>()
                .Select(text => text.Text ?? text.Inlines?.Text).ToArray();
            Assert.Contains("Source tab \u2014 Source content", labels);
            Assert.Contains(target is null ? "Destination was not recorded." : "Destination tab \u2014 Destination content", labels);
            Click(FindButton(panel, "Open desktop"));
            Assert.Equal(source, Assert.Single(opened));

            Click(ClearButtons()[0]);
            await MainWindowTests.WaitForAsync(() => window.OwnedWindows.Count == 1, "clear pending confirmation");
            Click(FindButton(Assert.Single(window.OwnedWindows), Strings.Common_Cancel));
            await MainWindowTests.WaitForAsync(() => window.OwnedWindows.Count == 0 &&
                ClearButtons()[0].IsEffectivelyEnabled, "cancelled pending review");
            Assert.Equal(2, (await PendingMoveStore.GetPendingMovesAsync()).Count);
            Assert.True(menu.IsVisible);

            Click(ClearButtons()[0]);
            await MainWindowTests.WaitForAsync(() => window.OwnedWindows.Count == 1, "clear pending confirmation again");
            Click(FindButton(Assert.Single(window.OwnedWindows), clear));
            await MainWindowTests.WaitForAsync(() => ClearButtons() is [{ IsEffectivelyEnabled: true }],
                "remaining pending move preview");
            Assert.True(menu.IsVisible);
            Assert.Equal(otherId, Assert.Single(await PendingMoveStore.GetPendingMovesAsync()).Id);
            Assert.NotEqual(markerId, otherId);
            Assert.True(await SessionStore.SessionExistsAsync(source));
            Assert.True(await SessionStore.SessionExistsAsync(targetDesktop));
            var sourceNote = Assert.Single(await NoteStore.GetNotesForDesktopAsync(source));
            var targetNote = Assert.Single(await NoteStore.GetNotesForDesktopAsync(targetDesktop));
            Assert.Equal((sourceId, "Source content", true), (sourceNote.Id, sourceNote.Content, sourceNote.Pinned));
            Assert.Equal((targetId, "Destination content", false), (targetNote.Id, targetNote.Content, targetNote.Pinned));

            Click(FindButton(panel, clear));
            await MainWindowTests.WaitForAsync(() => window.OwnedWindows.Count == 1, "clear remaining marker");
            Click(FindButton(Assert.Single(window.OwnedWindows), clear));
            await MainWindowTests.WaitForAsync(() => !panel.IsVisible && !menu.IsVisible, "pending move badge cleared");
            Assert.Empty(await PendingMoveStore.GetPendingMovesAsync());
        }
        finally
        {
            MainWindow.ReviewPendingMoves = oldReview;
            MainWindow.PendingMoveResolved = oldResolved;
            MainWindow.CanOpenRecoveryDesktop = oldCanOpen;
            MainWindow.OpenRecoveryDesktopAsync = oldOpen;
        }
    }

    private static void Click(Button button) =>
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));

    private static void PressKey(MainWindow window, Key key, KeyModifiers modifiers) =>
        window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers });

    private static void ClickMenu(MainWindow window, string name)
    {
        var menu = Assert.IsType<MenuItem>(window.FindControl<MenuItem>(name));
        menu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, menu));
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(1, 2d / 24)]
    [InlineData(2, 14)]
    [InlineData(3, 60)]
    public void CleanupUsesStrictAgeAndExplicitPinnedInclusion(int unitIndex, double elapsedDays)
    {
        var now = new DateTime(2026, 6, 15, 12, 0, 0);
        var expected = now.AddDays(-elapsedDays);
        var cutoff = MainWindow.GetTabCleanupCutoff("2", unitIndex, now);
        Assert.Equal(expected, cutoff);
        var older = new NoteTab { UpdatedAt = expected.AddSeconds(-1) };
        var pinned = new NoteTab { UpdatedAt = expected.AddSeconds(-1), Pinned = true };
        var exact = new NoteTab { UpdatedAt = expected };

        Assert.Equal(new[] { older }, MainWindow.GetTabCleanupCandidates([older, pinned, exact], cutoff, false));
        Assert.Equal(new[] { older, pinned }, MainWindow.GetTabCleanupCandidates([older, pinned, exact], cutoff, true));
        Assert.Null(MainWindow.GetTabCleanupCutoff("0", unitIndex, now));
        Assert.Equal(DateTime.MinValue, MainWindow.GetTabCleanupCutoff(int.MaxValue.ToString(), unitIndex, now));
    }

    [Theory]
    [InlineData(null, 13)]
    [InlineData("oops", 13)]
    [InlineData("3", 8)]
    [InlineData("24", 24)]
    [InlineData("900", 32)]
    public void ReadsCompatibleFontPreferences(string? stored, int expected)
    {
        Assert.Equal(expected, MainWindow.ParsePanelFontSize(stored));
    }

    [Fact]
    public void CzechPanelsUseSharedResourcesAndPluralForms()
    {
        var original = PanelLanguage.Current;
        try
        {
            PanelLanguage.Apply("en");
            var english = Strings.Recovery_Header;
            PanelLanguage.Apply("cs");
            Assert.Equal("cs", PanelLanguage.Current);
            Assert.NotEqual(english, Strings.Recovery_Header);
            Assert.Equal(Strings.Cleanup_DeleteN_Few, PanelLanguage.Plural(Strings.Cleanup_DeleteN_One,
                Strings.Cleanup_DeleteN_Few, Strings.Cleanup_DeleteN, 3));
        }
        finally { PanelLanguage.Apply(original); }
    }

    [AvaloniaFact]
    public async Task StartupAutoDeleteUsesStoredDaysAcrossDesktopsAndPreservesPinned()
    {
        await using var fixture = new MainWindowTests.WindowFixture();
        var window = fixture.Open();
        await window.Ready;
        var old = await NoteStore.InsertNoteAsync("one", "old", "old", false, 0);
        var pinned = await NoteStore.InsertNoteAsync("two", "pinned", "pinned", true, 0);
        var recent = await NoteStore.InsertNoteAsync("two", "recent", "recent", false, 1);
        var now = new DateTime(2026, 6, 15, 12, 0, 0);
        await DatabaseCore.ExecuteNonQueryAsync("UPDATE notes SET updated_at = @old WHERE id IN (@oldId, @pinnedId)",
            ("@old", now.AddDays(-30)), ("@oldId", old), ("@pinnedId", pinned));
        await DatabaseCore.ExecuteNonQueryAsync("UPDATE notes SET updated_at = @now WHERE id = @recent",
            ("@now", now), ("@recent", recent));

        Assert.Equal(0, await MainWindow.RunStartupAutoDeleteAsync(now));
        await PreferenceStore.SetPreferenceAsync("auto_delete_days", "7");
        Assert.Equal(1, await MainWindow.RunStartupAutoDeleteAsync(now));
        Assert.DoesNotContain(await NoteStore.GetNotesForDesktopAsync("one"), note => note.Id == old);
        var preserved = await NoteStore.GetNotesForDesktopAsync("two");
        Assert.Equal(new[] { pinned, recent }, preserved.Select(note => note.Id));
    }
}
