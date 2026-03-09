using JoJot.Services;
using JoJot.Tests.Helpers;

namespace JoJot.Tests.Services;

/// <summary>
/// Additional store tests targeting error/edge case paths in NoteStore, SessionStore,
/// PreferenceStore, and PendingMoveStore.
/// </summary>
[Collection("Database")]
public class StoreCoverageTests : IAsyncLifetime
{
    private TestDatabase _db = null!;

    public async Task InitializeAsync()
    {
        _db = await TestDatabase.CreateAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
    }

    // ─── NoteStore ──────────────────────────────────────────────────

    [Fact]
    public async Task GetNotesForDesktop_EmptyDesktop_ReturnsEmpty()
    {
        var notes = await NoteStore.GetNotesForDesktopAsync("nonexistent-desktop");
        notes.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteNote_NonexistentId_NoOp()
    {
        await NoteStore.DeleteNoteAsync(99999);
        // Should not throw
    }

    [Fact]
    public async Task UpdateNoteName_Changes_PersistsAndReloads()
    {
        var id = await NoteStore.InsertNoteAsync("desk-1", "Original", "content", false, 0);
        await NoteStore.UpdateNoteNameAsync(id, "Updated");

        var notes = await NoteStore.GetNotesForDesktopAsync("desk-1");
        notes[0].Name.Should().Be("Updated");
    }

    [Fact]
    public async Task UpdateNotePinned_Toggles()
    {
        var id = await NoteStore.InsertNoteAsync("desk-1", "Note", "content", false, 0);
        await NoteStore.UpdateNotePinnedAsync(id, true);

        var notes = await NoteStore.GetNotesForDesktopAsync("desk-1");
        notes[0].Pinned.Should().BeTrue();

        await NoteStore.UpdateNotePinnedAsync(id, false);
        notes = await NoteStore.GetNotesForDesktopAsync("desk-1");
        notes[0].Pinned.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateNoteSortOrders_PersistsMultiple()
    {
        var id1 = await NoteStore.InsertNoteAsync("desk-1", "A", "", false, 0);
        var id2 = await NoteStore.InsertNoteAsync("desk-1", "B", "", false, 1);

        await NoteStore.UpdateNoteSortOrdersAsync(new List<(long, int)> { (id1, 10), (id2, 20) });

        var notes = await NoteStore.GetNotesForDesktopAsync("desk-1");
        notes.Should().Contain(n => n.Id == id1 && n.SortOrder == 10);
        notes.Should().Contain(n => n.Id == id2 && n.SortOrder == 20);
    }

    [Fact]
    public async Task GetNoteCount_ReturnsCorrectCount()
    {
        await NoteStore.InsertNoteAsync("desk-count", "A", "", false, 0);
        await NoteStore.InsertNoteAsync("desk-count", "B", "", false, 1);

        var count = await NoteStore.GetNoteCountForDesktopAsync("desk-count");
        count.Should().Be(2);
    }

    [Fact]
    public async Task GetNoteNames_ReturnsNames()
    {
        await NoteStore.InsertNoteAsync("desk-names", "Alpha", "", false, 0);
        await NoteStore.InsertNoteAsync("desk-names", "Beta", "", false, 1);

        var names = await NoteStore.GetNoteNamesForDesktopAsync("desk-names", 10);
        names.Should().Contain("Alpha");
        names.Should().Contain("Beta");
    }

    [Fact]
    public async Task GetNotePreviews_ReturnsPreviewData()
    {
        await NoteStore.InsertNoteAsync("desk-prev", "Note1", "Content here", false, 0);

        var previews = await NoteStore.GetNotePreviewsForDesktopAsync("desk-prev");
        previews.Should().NotBeEmpty();
    }

    [Fact]
    public async Task MigrateTabsAsync_MovesSpecificTabs()
    {
        var id1 = await NoteStore.InsertNoteAsync("desk-src", "A", "content", false, 0);
        var id2 = await NoteStore.InsertNoteAsync("desk-src", "B", "content", true, 1);
        await NoteStore.InsertNoteAsync("desk-dst", "C", "existing", false, 0);

        await NoteStore.MigrateTabsAsync("desk-src", "desk-dst");

        var srcNotes = await NoteStore.GetNotesForDesktopAsync("desk-src");
        srcNotes.Should().BeEmpty();

        var dstNotes = await NoteStore.GetNotesForDesktopAsync("desk-dst");
        dstNotes.Should().HaveCount(3);
    }

    [Fact]
    public async Task MigrateTabsPreservePinsAsync_KeepsPinState()
    {
        await NoteStore.InsertNoteAsync("desk-pin", "Pinned", "content", true, 0);

        await NoteStore.MigrateTabsPreservePinsAsync("desk-pin", "desk-pin-dst");

        var notes = await NoteStore.GetNotesForDesktopAsync("desk-pin-dst");
        notes.Should().ContainSingle().Which.Pinned.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteEmptyNotes_RemovesEmptyOnly()
    {
        await NoteStore.InsertNoteAsync("desk-del", null, "", false, 0);
        await NoteStore.InsertNoteAsync("desk-del", "Named", "Has content", false, 1);

        var deleted = await NoteStore.DeleteEmptyNotesAsync("desk-del");
        deleted.Should().Be(1);

        var remaining = await NoteStore.GetNotesForDesktopAsync("desk-del");
        remaining.Should().ContainSingle().Which.Name.Should().Be("Named");
    }

    // ─── SessionStore ───────────────────────────────────────────────

    [Fact]
    public async Task UpdateSession_ChangesGuid_MigratesNotes()
    {
        await SessionStore.CreateSessionAsync("old-guid", "Desktop", 0);
        await NoteStore.InsertNoteAsync("old-guid", "Note", "content", false, 0);

        await SessionStore.UpdateSessionAsync("old-guid", "new-guid", "Desktop", 0);

        var notes = await NoteStore.GetNotesForDesktopAsync("new-guid");
        notes.Should().ContainSingle();
    }

    [Fact]
    public async Task GetDesktopName_Null_WhenNotFound()
    {
        var name = await SessionStore.GetDesktopNameAsync("nonexistent-guid");
        name.Should().BeNull();
    }

    [Fact]
    public async Task GetWindowGeometry_Null_WhenNoGeometry()
    {
        await SessionStore.CreateSessionAsync("geo-test", null, null);
        var geo = await SessionStore.GetWindowGeometryAsync("geo-test");
        geo.Should().BeNull();
    }

    [Fact]
    public async Task SaveAndGetWindowGeometry_RoundTrips()
    {
        await SessionStore.CreateSessionAsync("geo-round", null, null);
        await SessionStore.SaveWindowGeometryAsync("geo-round", new JoJot.Models.WindowGeometry(10, 20, 800, 600, false));

        var geo = await SessionStore.GetWindowGeometryAsync("geo-round");
        geo.Should().NotBeNull();
        geo!.Left.Should().Be(10);
        geo.Width.Should().Be(800);
    }

    [Fact]
    public async Task GetOrphanedSessionInfo_ReturnsEmpty_ForEmptyList()
    {
        var info = await SessionStore.GetOrphanedSessionInfoAsync([]);
        info.Should().BeEmpty();
    }

    // ─── PreferenceStore ────────────────────────────────────────────

    [Fact]
    public async Task GetPreference_Null_WhenNotSet()
    {
        var value = await PreferenceStore.GetPreferenceAsync("nonexistent_key_xyz");
        value.Should().BeNull();
    }

    [Fact]
    public async Task SetPreference_Upserts()
    {
        await PreferenceStore.SetPreferenceAsync("upsert_key", "value1");
        await PreferenceStore.SetPreferenceAsync("upsert_key", "value2");

        var result = await PreferenceStore.GetPreferenceAsync("upsert_key");
        result.Should().Be("value2");
    }

    [Fact]
    public async Task SetPreference_EmptyValue_Allowed()
    {
        await PreferenceStore.SetPreferenceAsync("empty_val", "");
        var result = await PreferenceStore.GetPreferenceAsync("empty_val");
        result.Should().Be("");
    }

    // ─── PendingMoveStore ───────────────────────────────────────────

    [Fact]
    public async Task DeleteAllPendingMoves_ClearsAll()
    {
        await PendingMoveStore.InsertPendingMoveAsync("w1", "from1", "to1");
        await PendingMoveStore.InsertPendingMoveAsync("w2", "from2", null);

        await PendingMoveStore.DeleteAllPendingMovesAsync();

        var moves = await PendingMoveStore.GetPendingMovesAsync();
        moves.Should().BeEmpty();
    }

    [Fact]
    public async Task GetPendingMoves_ReturnsAllFields()
    {
        await PendingMoveStore.DeleteAllPendingMovesAsync();
        await PendingMoveStore.InsertPendingMoveAsync("win-test", "desk-from", "desk-to");

        var moves = await PendingMoveStore.GetPendingMovesAsync();
        moves.Should().ContainSingle();
        moves[0].WindowId.Should().Be("win-test");
        moves[0].FromDesktop.Should().Be("desk-from");
        moves[0].ToDesktop.Should().Be("desk-to");
        moves[0].DetectedAt.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task DeletePendingMove_NonexistentWindow_NoOp()
    {
        await PendingMoveStore.DeletePendingMoveAsync("nonexistent-window-id");
    }
}
