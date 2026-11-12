using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using JoJot.Resources;
using JoJot.AvaloniaExperiment.Platform;
using JoJot.Services;
using Xunit;

namespace JoJot.AvaloniaExperiment.Tests;

public sealed class DesktopMovementTests
{
    [AvaloniaFact]
    public async Task KeepHerePreservesExistingTargetNotesAndClearsPendingMove()
    {
        var oldHasWindow = MainWindow.DesktopHasWindow;
        var oldReparent = MainWindow.DesktopReparent;
        try
        {
            await using var fixture = new MainWindowTests.WindowFixture();
            var window = fixture.Open();
            await window.Ready;
            var source = window.DesktopGuid;
            var target = Guid.NewGuid().ToString();
            await SessionStore.CreateSessionAsync(target, "target", 1);
            var existing = await NoteStore.InsertNoteAsync(target, "existing", "destination", true, 0);
            var moved = await NoteStore.InsertNoteAsync(source, "moved", "source", true, 0);
            await window.ReloadNotesAsync();
            MainWindow.DesktopHasWindow = _ => false;
            MainWindow.DesktopReparent = (_, _) => { };
            var resolution = window.ResolveDesktopMoveAsync(0, target);
            await MainWindowTests.WaitForAsync(() => window.OwnedWindows.Count == 1, "desktop move dialog");
            Assert.Contains(await PendingMoveStore.GetPendingMovesAsync(), m => m.WindowId == source && m.ToDesktop is null);
            var dialog = Assert.Single(window.OwnedWindows);
            var button = Assert.Single(dialog.GetVisualDescendants().OfType<Button>(), b => Equals(b.Content, Strings.Drag_KeepHere));
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await resolution;
            Assert.Equal(target, window.DesktopGuid);
            var notes = await NoteStore.GetNotesForDesktopAsync(target);
            Assert.Contains(notes, n => n.Id == existing && n.Pinned && n.Content == "destination");
            Assert.Contains(notes, n => n.Id == moved && n.Pinned && n.Content == "source");
            Assert.True(notes.Single(n => n.Id == moved).SortOrder > notes.Single(n => n.Id == existing).SortOrder);
            Assert.DoesNotContain(await PendingMoveStore.GetPendingMovesAsync(), m => m.WindowId == source);
        }
        finally
        {
            MainWindow.DesktopHasWindow = oldHasWindow;
            MainWindow.DesktopReparent = oldReparent;
        }
    }
    [AvaloniaFact]
    public async Task RetargetAndReturnDismissesPendingMove()
    {
        await using var fixture = new MainWindowTests.WindowFixture();
        var window = fixture.Open();
        await window.Ready;
        var source = window.DesktopGuid;
        var first = Guid.NewGuid().ToString();
        var second = Guid.NewGuid().ToString();
        var existing = await NoteStore.InsertNoteAsync(second, "destination", "keep me", true, 0);
        var resolution = window.ResolveDesktopMoveAsync(0, first);
        await MainWindowTests.WaitForAsync(() => window.OwnedWindows.Count == 1, "desktop move dialog");
        await window.NotifyDesktopMoveTargetAsync(second);
        var pending = Assert.Single(await PendingMoveStore.GetPendingMovesAsync());
        Assert.Null(pending.ToDesktop);
        // Emulate WPF App.ShouldRecoverMove and its recovery branch against this marker.
        if (pending.ToDesktop is not null && !pending.FromDesktop.Equals(pending.ToDesktop, StringComparison.OrdinalIgnoreCase))
            await NoteStore.MigrateTabsPreservePinsAsync(pending.ToDesktop, pending.FromDesktop);
        Assert.Contains(await NoteStore.GetNotesForDesktopAsync(second), n => n.Id == existing && n.Pinned && n.Content == "keep me");
        await window.NotifyDesktopMoveTargetAsync(source);
        await resolution;
        Assert.Empty(await PendingMoveStore.GetPendingMovesAsync());
    }

    [AvaloniaFact]
    public async Task GoBackShowsRetryAfterFailureAndClearsPendingAfterSuccess()
    {
        var previousMove = MainWindow.DesktopMoveWindow;
        try
        {
            await using var fixture = new MainWindowTests.WindowFixture();
            var window = fixture.Open();
            await window.Ready;
            var attempts = 0;
            MainWindow.DesktopMoveWindow = (_, _) => ++attempts > 1;
            var resolution = window.ResolveDesktopMoveAsync(0, Guid.NewGuid().ToString());
            await MainWindowTests.WaitForAsync(() => window.OwnedWindows.Count == 1, "desktop move dialog");
            var dialog = Assert.Single(window.OwnedWindows);
            var button = Assert.Single(dialog.GetVisualDescendants().OfType<Button>(), b => Equals(b.Content, Strings.Drag_GoBack));
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(Strings.Drag_Retry, button.Content);
            Assert.Single(await PendingMoveStore.GetPendingMovesAsync());
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await resolution;
            Assert.Equal(2, attempts);
            Assert.Empty(await PendingMoveStore.GetPendingMovesAsync());
        }
        finally { MainWindow.DesktopMoveWindow = previousMove; }
    }
    [AvaloniaFact]
    public async Task TargetWindowAppearingChangesChoiceAndMergesPinnedNotes()
    {
        var oldHas = MainWindow.DesktopHasWindow;
        var oldReload = MainWindow.DesktopReloadTarget;
        var oldClose = MainWindow.DesktopCloseSource;
        try
        {
            await using var fixture = new MainWindowTests.WindowFixture();
            var window = fixture.Open();
            await window.Ready;
            var source = window.DesktopGuid;
            var target = Guid.NewGuid().ToString();
            var pinned = await NoteStore.InsertNoteAsync(source, "moved", "content", true, 0);
            await window.ReloadNotesAsync();
            var targetOpen = false;
            var reloaded = false;
            var closed = false;
            MainWindow.DesktopHasWindow = _ => targetOpen;
            MainWindow.DesktopReloadTarget = _ => { reloaded = true; return Task.CompletedTask; };
            MainWindow.DesktopCloseSource = _ => closed = true;
            var resolution = window.ResolveDesktopMoveAsync(0, target);
            await MainWindowTests.WaitForAsync(() => window.OwnedWindows.Count == 1, "desktop move dialog");
            var dialog = Assert.Single(window.OwnedWindows);
            targetOpen = true;
            await window.NotifyDesktopMoveTargetAsync(target);
            var button = Assert.Single(dialog.GetVisualDescendants().OfType<Button>(), b => Equals(b.Content, Strings.Drag_MergeNotes));
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await resolution;
            Assert.True(reloaded);
            Assert.True(closed);
            Assert.Contains(await NoteStore.GetNotesForDesktopAsync(target), n => n.Id == pinned && n.Pinned);
            Assert.Empty(await PendingMoveStore.GetPendingMovesAsync());
        }
        finally
        {
            MainWindow.DesktopHasWindow = oldHas;
            MainWindow.DesktopReloadTarget = oldReload;
            MainWindow.DesktopCloseSource = oldClose;
        }
    }
    [AvaloniaFact]
    public async Task TransactionFailureRollsBackNotesSessionAndPendingMarker()
    {
        await using var fixture = new MainWindowTests.WindowFixture();
        var window = fixture.Open();
        await window.Ready;
        var source = window.DesktopGuid;
        var target = Guid.NewGuid().ToString();
        await SessionStore.CreateSessionAsync(target, "target", 1);
        var moved = await NoteStore.InsertNoteAsync(source, "source", "source text", true, 0);
        var existing = await NoteStore.InsertNoteAsync(target, "target", "target text", false, 0);
        await PendingMoveStore.InsertPendingMoveAsync(source, source, null);
        foreach (var statement in new[]
        {
            "CREATE TRIGGER fail_move BEFORE UPDATE ON app_state BEGIN SELECT RAISE(ABORT, 'injected'); END",
            "CREATE TRIGGER fail_move BEFORE DELETE ON pending_moves BEGIN SELECT RAISE(ABORT, 'injected'); END",
        })
        {
            await DatabaseCore.ExecuteNonQueryAsync(statement);
            try
            {
                await Assert.ThrowsAnyAsync<Exception>(() => DesktopMoveStore.ResolveAsync(source, target, false, "target", 1));
                Assert.Contains(await NoteStore.GetNotesForDesktopAsync(source), n => n.Id == moved && n.Pinned);
                Assert.Contains(await NoteStore.GetNotesForDesktopAsync(target), n => n.Id == existing);
                Assert.True(await SessionStore.SessionExistsAsync(source));
                Assert.True(await SessionStore.SessionExistsAsync(target));
                Assert.Contains(await PendingMoveStore.GetPendingMovesAsync(), m => m.WindowId == source && m.ToDesktop is null);
            }
            finally { await DatabaseCore.ExecuteNonQueryAsync("DROP TRIGGER fail_move"); }
        }
    }
    [AvaloniaFact]
    public async Task ShutdownClosesUndecidedDialogAndRetainsSafePendingMarker()
    {
        var oldShutdown = MainWindow.DesktopShutdownPending;
        var oldRefresh = MainWindow.DesktopRefreshPending;
        try
        {
            await using var fixture = new MainWindowTests.WindowFixture();
            var window = fixture.Open();
            await window.Ready;
            var target = Guid.NewGuid().ToString();
            var existing = await NoteStore.InsertNoteAsync(target, "destination", "retained", true, 0);
            var refreshed = 0;
            MainWindow.DesktopShutdownPending = () => true;
            MainWindow.DesktopRefreshPending = () => { refreshed++; return Task.CompletedTask; };
            var resolution = window.ResolveDesktopMoveAsync(0, target);
            await MainWindowTests.WaitForAsync(() => window.OwnedWindows.Count == 1, "desktop move dialog");
            Assert.Equal(1, refreshed);
            Assert.Single(window.OwnedWindows).Close();
            await resolution;
            Assert.Equal(2, refreshed);
            Assert.Contains(await PendingMoveStore.GetPendingMovesAsync(), move =>
                move.WindowId == window.DesktopGuid && move.ToDesktop is null);
            Assert.Contains(await NoteStore.GetNotesForDesktopAsync(target), note =>
                note.Id == existing && note.Pinned && note.Content == "retained");
        }
        finally
        {
            MainWindow.DesktopShutdownPending = oldShutdown;
            MainWindow.DesktopRefreshPending = oldRefresh;
        }
    }
}
