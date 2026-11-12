using Avalonia.Headless.XUnit;
using JoJot.AvaloniaExperiment.Platform;
using JoJot.Services;
using Xunit;

namespace JoJot.AvaloniaExperiment.Tests;

public sealed class DesktopSessionMatcherTests
{
    [AvaloniaFact]
    public async Task MatchesGuidNameAndIndexAndPreservesNonemptyOrphans()
    {
        await using var fixture = new MainWindowTests.WindowFixture();
        var window = fixture.Open();
        await window.Ready;
        var exact = Guid.NewGuid().ToString();
        var byName = Guid.NewGuid().ToString();
        var byIndex = Guid.NewGuid().ToString();
        await SessionStore.CreateSessionAsync(exact, "old", 9);
        await SessionStore.CreateSessionAsync("old-name", "Named", 8);
        await SessionStore.CreateSessionAsync("old-index", null, 2);
        await SessionStore.CreateSessionAsync("orphan", "lost", 40);
        var nameNote = await NoteStore.InsertNoteAsync("old-name", "name", "content", true, 0);
        var indexNote = await NoteStore.InsertNoteAsync("old-index", "index", "content", false, 0);
        await NoteStore.InsertNoteAsync("orphan", "lost", "content", false, 0);
        var orphans = await DesktopSessionMatcher.MatchAsync([
            new(exact, "updated", 0), new(byName, "Named", 1), new(byIndex, "", 2)]);
        Assert.Contains("orphan", orphans);
        Assert.Contains(await NoteStore.GetNotesForDesktopAsync(byName), n => n.Id == nameNote && n.Pinned);
        Assert.Contains(await NoteStore.GetNotesForDesktopAsync(byIndex), n => n.Id == indexNote);
        var sessions = await SessionStore.GetAllSessionsAsync();
        Assert.Contains(sessions, s => s.DesktopGuid == exact && s.DesktopName == "updated" && s.DesktopIndex == 0);
    }

    [AvaloniaFact]
    public async Task AmbiguousIndexDoesNotReassignSessions()
    {
        await using var fixture = new MainWindowTests.WindowFixture();
        var window = fixture.Open();
        await window.Ready;
        await SessionStore.CreateSessionAsync("one", null, 7);
        await SessionStore.CreateSessionAsync("two", null, 7);
        await NoteStore.InsertNoteAsync("one", "one", "", false, 0);
        await NoteStore.InsertNoteAsync("two", "two", "", false, 0);
        var target = Guid.NewGuid().ToString();
        var orphans = await DesktopSessionMatcher.MatchAsync([new(target, "", 7)]);
        Assert.Contains("one", orphans);
        Assert.Contains("two", orphans);
        Assert.Empty(await NoteStore.GetNotesForDesktopAsync(target));
    }
}
