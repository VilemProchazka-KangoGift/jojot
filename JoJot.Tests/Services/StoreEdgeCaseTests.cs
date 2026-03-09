using JoJot.Services;
using JoJot.Tests.Helpers;

namespace JoJot.Tests.Services;

/// <summary>
/// Edge case and boundary tests for NoteStore, SessionStore, PreferenceStore, PendingMoveStore.
/// </summary>
[Collection("Database")]
public class StoreEdgeCaseTests : IAsyncLifetime
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

    // ─── NoteStore: Preview truncation boundary ────────────────────

    [Fact]
    public async Task GetNotePreviews_Content59Chars_NoTruncation()
    {
        var content = new string('a', 59);
        await NoteStore.InsertNoteAsync("trunc-59", "Note", content, false, 0);

        var previews = await NoteStore.GetNotePreviewsForDesktopAsync("trunc-59");
        previews[0].Excerpt.Should().Be(content);
        previews[0].Excerpt.Length.Should().Be(59);
    }

    [Fact]
    public async Task GetNotePreviews_Content60Chars_ExactBoundary()
    {
        var content = new string('b', 60);
        await NoteStore.InsertNoteAsync("trunc-60", "Note", content, false, 0);

        var previews = await NoteStore.GetNotePreviewsForDesktopAsync("trunc-60");
        previews[0].Excerpt.Should().Be(content);
        previews[0].Excerpt.Length.Should().Be(60);
    }

    [Fact]
    public async Task GetNotePreviews_Content61Chars_Truncated()
    {
        var content = new string('c', 61);
        await NoteStore.InsertNoteAsync("trunc-61", "Note", content, false, 0);

        var previews = await NoteStore.GetNotePreviewsForDesktopAsync("trunc-61");
        previews[0].Excerpt.Length.Should().Be(60);
    }

    // ─── NoteStore: Migration with interleaved pins ────────────────

    [Fact]
    public async Task MigrateTabsPreservePins_FiveTabs_InterleavedPins()
    {
        var src = "pin-interleave-src";
        var tgt = "pin-interleave-tgt";
        await SessionStore.CreateSessionAsync(src, "Source", 0);
        await SessionStore.CreateSessionAsync(tgt, "Target", 1);

        await NoteStore.InsertNoteAsync(src, "P1", "c", true, 0);
        await NoteStore.InsertNoteAsync(src, "U1", "c", false, 1);
        await NoteStore.InsertNoteAsync(src, "P2", "c", true, 2);
        await NoteStore.InsertNoteAsync(src, "U2", "c", false, 3);
        await NoteStore.InsertNoteAsync(src, "P3", "c", true, 4);

        await NoteStore.MigrateTabsPreservePinsAsync(src, tgt);

        var notes = await NoteStore.GetNotesForDesktopAsync(tgt);
        notes.Should().HaveCount(5);
        notes.Count(n => n.Pinned).Should().Be(3);
        notes.Count(n => !n.Pinned).Should().Be(2);
    }

    // ─── NoteStore: MaxSortOrder with gaps ─────────────────────────

    [Fact]
    public async Task GetMaxSortOrder_WithGaps_ReturnsMax()
    {
        var desk = "max-gaps";
        await NoteStore.InsertNoteAsync(desk, "A", "", false, 0);
        await NoteStore.InsertNoteAsync(desk, "B", "", false, 5);
        await NoteStore.InsertNoteAsync(desk, "C", "", false, 10);

        var max = await NoteStore.GetMaxSortOrderAsync(desk);
        max.Should().Be(10);
    }

    // ─── NoteStore: Negative sort order values ─────────────────────

    [Fact]
    public async Task UpdateNoteSortOrders_NegativeValues_Persists()
    {
        var id = await NoteStore.InsertNoteAsync("neg-sort", "A", "", false, 0);
        await NoteStore.UpdateNoteSortOrdersAsync([(id, -5)]);

        var notes = await NoteStore.GetNotesForDesktopAsync("neg-sort");
        notes[0].SortOrder.Should().Be(-5);
    }

    // ─── NoteStore: UpdateNoteName to null ─────────────────────────

    [Fact]
    public async Task UpdateNoteName_ToNull_ClearsName()
    {
        var id = await NoteStore.InsertNoteAsync("name-null", "HasName", "content", false, 0);
        await NoteStore.UpdateNoteNameAsync(id, null);

        var notes = await NoteStore.GetNotesForDesktopAsync("name-null");
        notes[0].Name.Should().BeNull();
    }

    // ─── NoteStore: Empty content ──────────────────────────────────

    [Fact]
    public async Task InsertNote_EmptyContent_Allowed()
    {
        var id = await NoteStore.InsertNoteAsync("empty-content", "Note", "", false, 0);
        id.Should().BeGreaterThan(0);

        var notes = await NoteStore.GetNotesForDesktopAsync("empty-content");
        notes[0].Content.Should().Be("");
    }

    // ─── NoteStore: GetNoteNames with no name, no content ──────────

    [Fact]
    public async Task GetNoteNames_NullNameAndEmptyContent_ReturnsEmptyString()
    {
        await NoteStore.InsertNoteAsync("empty-names", null, "", false, 0);

        var names = await NoteStore.GetNoteNamesForDesktopAsync("empty-names");
        // COALESCE(NULLIF(name, ''), SUBSTR(content, 1, 30)) → SUBSTR("", 1, 30) = ""
        // The .Select(n => n ?? "Empty note") only catches null, not ""
        names.Should().ContainSingle().Which.Should().Be("");
    }

    // ─── SessionStore: GUID case sensitivity ───────────────────────

    [Fact]
    public async Task SessionStore_GuidLookup_IsCaseSensitive()
    {
        await SessionStore.CreateSessionAsync("ABC-123", "Upper", 0);

        var name = await SessionStore.GetDesktopNameAsync("abc-123");
        // SQLite default collation is case-sensitive for ASCII
        name.Should().BeNull();
    }

    // ─── SessionStore: Delete non-existent session ─────────────────

    [Fact]
    public async Task DeleteSessionAndNotes_NonexistentGuid_NoOp()
    {
        // Should not throw
        await SessionStore.DeleteSessionAndNotesAsync("nonexistent-session-guid");
    }

    // ─── SessionStore: UpdateSession to same GUID ──────────────────

    [Fact]
    public async Task UpdateSession_SameGuid_UpdatesNameAndIndex()
    {
        await SessionStore.CreateSessionAsync("same-update", "Original", 0);
        await SessionStore.UpdateSessionAsync("same-update", "same-update", "Updated", 5);

        var sessions = await SessionStore.GetAllSessionsAsync();
        var session = sessions.Single(s => s.DesktopGuid == "same-update");
        session.DesktopName.Should().Be("Updated");
        session.DesktopIndex.Should().Be(5);
    }

    // ─── PreferenceStore: null vs empty ────────────────────────────

    [Fact]
    public async Task PreferenceStore_EmptyString_DistinguishedFromNull()
    {
        await PreferenceStore.SetPreferenceAsync("empty-pref", "");

        var result = await PreferenceStore.GetPreferenceAsync("empty-pref");
        result.Should().NotBeNull();
        result.Should().Be("");
    }

    [Fact]
    public async Task PreferenceStore_NonexistentKey_ReturnsNull()
    {
        var result = await PreferenceStore.GetPreferenceAsync("truly-nonexistent-key-xyz");
        result.Should().BeNull();
    }

    // ─── PendingMoveStore: Duplicate pairs ─────────────────────────

    [Fact]
    public async Task PendingMoveStore_DuplicatePairs_BothPersist()
    {
        await PendingMoveStore.DeleteAllPendingMovesAsync();

        await PendingMoveStore.InsertPendingMoveAsync("dup-win", "desk-A", "desk-B");
        await PendingMoveStore.InsertPendingMoveAsync("dup-win", "desk-A", "desk-B");

        var moves = await PendingMoveStore.GetPendingMovesAsync();
        moves.Count(m => m.WindowId == "dup-win").Should().Be(2);
    }

    // ─── PendingMoveStore: Ordering ────────────────────────────────

    [Fact]
    public async Task PendingMoveStore_InsertionOrder_Preserved()
    {
        await PendingMoveStore.DeleteAllPendingMovesAsync();

        await PendingMoveStore.InsertPendingMoveAsync("first", "f1", "t1");
        await PendingMoveStore.InsertPendingMoveAsync("second", "f2", "t2");
        await PendingMoveStore.InsertPendingMoveAsync("third", "f3", "t3");

        var moves = await PendingMoveStore.GetPendingMovesAsync();
        // IDs should be in insertion order
        moves[0].Id.Should().BeLessThan(moves[1].Id);
        moves[1].Id.Should().BeLessThan(moves[2].Id);
    }

    // ─── SessionStore: GetOrphanedSessionInfo ordering ─────────────

    [Fact]
    public async Task GetOrphanedSessionInfo_MultipleOrphans_ReturnsAll()
    {
        await SessionStore.CreateSessionAsync("orphan-a", "Desktop A", 0);
        await SessionStore.CreateSessionAsync("orphan-b", "Desktop B", 1);
        await NoteStore.InsertNoteAsync("orphan-a", "Tab1", "content", false, 0);
        await NoteStore.InsertNoteAsync("orphan-b", "Tab2", "content", false, 0);
        await NoteStore.InsertNoteAsync("orphan-b", "Tab3", "content", false, 1);

        var results = await SessionStore.GetOrphanedSessionInfoAsync(["orphan-a", "orphan-b"]);
        results.Should().HaveCount(2);

        var a = results.Single(r => r.DesktopGuid == "orphan-a");
        a.TabCount.Should().Be(1);

        var b = results.Single(r => r.DesktopGuid == "orphan-b");
        b.TabCount.Should().Be(2);
    }

    // ─── NoteStore: CancellationToken support ──────────────────────

    [Fact]
    public async Task GetNotesForDesktop_CancellationToken_Works()
    {
        await NoteStore.InsertNoteAsync("ct-desk", "Note", "c", false, 0);
        using var cts = new CancellationTokenSource();
        var notes = await NoteStore.GetNotesForDesktopAsync("ct-desk", cts.Token);
        notes.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetMaxSortOrder_CancellationToken_Works()
    {
        await NoteStore.InsertNoteAsync("ct-max", "Note", "c", false, 3);
        using var cts = new CancellationTokenSource();
        var max = await NoteStore.GetMaxSortOrderAsync("ct-max", cts.Token);
        max.Should().Be(3);
    }

    [Fact]
    public async Task GetNoteCount_CancellationToken_Works()
    {
        await NoteStore.InsertNoteAsync("ct-count", "Note", "c", false, 0);
        using var cts = new CancellationTokenSource();
        var count = await NoteStore.GetNoteCountForDesktopAsync("ct-count", cts.Token);
        count.Should().Be(1);
    }

    [Fact]
    public async Task GetNoteNames_CancellationToken_Works()
    {
        await NoteStore.InsertNoteAsync("ct-names", "Named", "c", false, 0);
        using var cts = new CancellationTokenSource();
        var names = await NoteStore.GetNoteNamesForDesktopAsync("ct-names", ct: cts.Token);
        names.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetNotePreviews_CancellationToken_Works()
    {
        await NoteStore.InsertNoteAsync("ct-prev", "Note", "content", false, 0);
        using var cts = new CancellationTokenSource();
        var previews = await NoteStore.GetNotePreviewsForDesktopAsync("ct-prev", ct: cts.Token);
        previews.Should().HaveCount(1);
    }
}
