using JoJot.Services;
using JoJot.Tests.Helpers;

namespace JoJot.Tests.Services;

/// <summary>
/// Extended integration tests for NoteStore operations beyond basic CRUD
/// (which is already covered in DatabaseServiceTests).
/// </summary>
[Collection("Database")]
public class NoteStoreTests : IAsyncLifetime
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

    // ─── Ordering ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetNotesForDesktop_OrdersByPinnedThenSortOrder()
    {
        var desk = "order-test";
        await NoteStore.InsertNoteAsync(desk, "Unpinned-1", "", false, 2);
        await NoteStore.InsertNoteAsync(desk, "Pinned-1", "", true, 1);
        await NoteStore.InsertNoteAsync(desk, "Unpinned-0", "", false, 0);
        await NoteStore.InsertNoteAsync(desk, "Pinned-0", "", true, 0);

        var notes = await NoteStore.GetNotesForDesktopAsync(desk);

        // Pinned first (by sort_order), then unpinned (by sort_order)
        notes[0].Name.Should().Be("Pinned-0");
        notes[1].Name.Should().Be("Pinned-1");
        notes[2].Name.Should().Be("Unpinned-0");
        notes[3].Name.Should().Be("Unpinned-1");
    }

    // ─── MaxSortOrder ─────────────────────────────────────────────────

    [Fact]
    public async Task GetMaxSortOrder_ReturnsNegativeOne_WhenEmpty()
    {
        var max = await NoteStore.GetMaxSortOrderAsync("empty-desktop");
        max.Should().Be(-1);
    }

    [Fact]
    public async Task GetMaxSortOrder_ReturnsSingleNoteSortOrder()
    {
        await NoteStore.InsertNoteAsync("single-so", "Note", "", false, 5);

        var max = await NoteStore.GetMaxSortOrderAsync("single-so");
        max.Should().Be(5);
    }

    // ─── DeleteEmptyNotes ─────────────────────────────────────────────

    [Fact]
    public async Task DeleteEmptyNotes_PreservesPinnedEmptyNotes()
    {
        var desk = "del-empty-pin";
        await NoteStore.InsertNoteAsync(desk, null, "", true, 0);  // Pinned empty
        await NoteStore.InsertNoteAsync(desk, null, "", false, 1); // Unpinned empty

        var deleted = await NoteStore.DeleteEmptyNotesAsync(desk);

        deleted.Should().Be(1);
        var remaining = await NoteStore.GetNotesForDesktopAsync(desk);
        remaining.Should().ContainSingle().Which.Pinned.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteEmptyNotes_PreservesWhitespaceContent_False()
    {
        var desk = "del-whitespace";
        await NoteStore.InsertNoteAsync(desk, null, "   ", false, 0);

        var deleted = await NoteStore.DeleteEmptyNotesAsync(desk);
        deleted.Should().Be(1);
    }

    [Fact]
    public async Task DeleteEmptyNotes_ReturnsZero_WhenAllHaveContent()
    {
        var desk = "del-has-content";
        await NoteStore.InsertNoteAsync(desk, "Note", "Some content", false, 0);

        var deleted = await NoteStore.DeleteEmptyNotesAsync(desk);
        deleted.Should().Be(0);
    }

    [Fact]
    public async Task DeleteEmptyNotes_ReturnsZero_WhenNoNotes()
    {
        var deleted = await NoteStore.DeleteEmptyNotesAsync("no-notes-desk");
        deleted.Should().Be(0);
    }

    // ─── MigrateNotesDesktopGuid ──────────────────────────────────────

    [Fact]
    public async Task MigrateNotesDesktopGuid_MovesAllNotes()
    {
        var from = "migrate-from";
        var to = "migrate-to";
        await NoteStore.InsertNoteAsync(from, "A", "content A", false, 0);
        await NoteStore.InsertNoteAsync(from, "B", "content B", true, 1);

        await NoteStore.MigrateNotesDesktopGuidAsync(from, to);

        var srcNotes = await NoteStore.GetNotesForDesktopAsync(from);
        srcNotes.Should().BeEmpty();

        var dstNotes = await NoteStore.GetNotesForDesktopAsync(to);
        dstNotes.Should().HaveCount(2);
    }

    [Fact]
    public async Task MigrateNotesDesktopGuid_NoOp_WhenNoNotes()
    {
        // Should not throw
        await NoteStore.MigrateNotesDesktopGuidAsync("no-src", "no-dst");
    }

    // ─── MigrateTabsPreservePins ──────────────────────────────────────

    [Fact]
    public async Task MigrateTabsPreservePins_KeepsPinState()
    {
        var src = "ppin-src";
        var tgt = "ppin-tgt";
        await SessionStore.CreateSessionAsync(src, "Source", 0);
        await SessionStore.CreateSessionAsync(tgt, "Target", 1);
        await NoteStore.InsertNoteAsync(src, "Pinned", "content", true, 0);
        await NoteStore.InsertNoteAsync(src, "Unpinned", "content", false, 1);

        await NoteStore.MigrateTabsPreservePinsAsync(src, tgt);

        var targetNotes = await NoteStore.GetNotesForDesktopAsync(tgt);
        targetNotes.Should().HaveCount(2);
        targetNotes.Should().Contain(n => n.Pinned && n.Name == "Pinned");
        targetNotes.Should().Contain(n => !n.Pinned && n.Name == "Unpinned");
    }

    [Fact]
    public async Task MigrateTabs_UnpinsMigratedTabs()
    {
        var src = "unpin-src";
        var tgt = "unpin-tgt";
        await SessionStore.CreateSessionAsync(src, "Source", 0);
        await SessionStore.CreateSessionAsync(tgt, "Target", 1);
        await NoteStore.InsertNoteAsync(src, "Was Pinned", "content", true, 0);

        await NoteStore.MigrateTabsAsync(src, tgt);

        var targetNotes = await NoteStore.GetNotesForDesktopAsync(tgt);
        targetNotes.Should().ContainSingle().Which.Pinned.Should().BeFalse();
    }

    [Fact]
    public async Task MigrateTabs_ReassignsSortOrderAfterTarget()
    {
        var src = "so-src";
        var tgt = "so-tgt";
        await SessionStore.CreateSessionAsync(src, "Source", 0);
        await SessionStore.CreateSessionAsync(tgt, "Target", 1);
        await NoteStore.InsertNoteAsync(tgt, "Existing", "c", false, 5);
        await NoteStore.InsertNoteAsync(src, "Migrated-0", "c", false, 0);
        await NoteStore.InsertNoteAsync(src, "Migrated-1", "c", false, 1);

        await NoteStore.MigrateTabsAsync(src, tgt);

        var notes = await NoteStore.GetNotesForDesktopAsync(tgt);
        notes.Should().HaveCount(3);
        // Migrated tabs should have sort_order > 5
        var migrated = notes.Where(n => n.Name!.StartsWith("Migrated")).ToList();
        migrated.Should().AllSatisfy(n => n.SortOrder.Should().BeGreaterThan(5));
    }

    // ─── GetNoteCount ─────────────────────────────────────────────────

    [Fact]
    public async Task GetNoteCount_ReturnsZero_WhenNoNotes()
    {
        var count = await NoteStore.GetNoteCountForDesktopAsync("empty-count");
        count.Should().Be(0);
    }

    // ─── GetNoteNames ─────────────────────────────────────────────────

    [Fact]
    public async Task GetNoteNames_FallsBackToContentExcerpt()
    {
        await NoteStore.InsertNoteAsync("names-test", null, "Hello world content", false, 0);

        var names = await NoteStore.GetNoteNamesForDesktopAsync("names-test");
        names.Should().ContainSingle().Which.Should().Contain("Hello world");
    }

    [Fact]
    public async Task GetNoteNames_RespectsLimit()
    {
        var desk = "names-limit";
        for (int i = 0; i < 10; i++)
            await NoteStore.InsertNoteAsync(desk, $"Note {i}", "", false, i);

        var names = await NoteStore.GetNoteNamesForDesktopAsync(desk, limit: 3);
        names.Should().HaveCount(3);
    }

    // ─── GetNotePreviews ──────────────────────────────────────────────

    [Fact]
    public async Task GetNotePreviews_TruncatesLongContent()
    {
        var longContent = new string('a', 200);
        await NoteStore.InsertNoteAsync("preview-trunc", "Long", longContent, false, 0);

        var previews = await NoteStore.GetNotePreviewsForDesktopAsync("preview-trunc");
        previews.Should().ContainSingle();
        previews[0].Excerpt.Length.Should().BeLessThanOrEqualTo(60);
    }

    [Fact]
    public async Task GetNotePreviews_ReturnsEmpty_WhenNoNotes()
    {
        var previews = await NoteStore.GetNotePreviewsForDesktopAsync("no-previews");
        previews.Should().BeEmpty();
    }

    [Fact]
    public async Task GetNotePreviews_NullName_ReturnsNull()
    {
        await NoteStore.InsertNoteAsync("preview-null", null, "content", false, 0);

        var previews = await NoteStore.GetNotePreviewsForDesktopAsync("preview-null");
        previews[0].Name.Should().BeNull();
    }

    // ─── Timestamps ───────────────────────────────────────────────────

    [Fact]
    public async Task InsertNote_SetsTimestamps()
    {
        var clock = new TestClock();
        DatabaseCore.SetClock(clock);
        try
        {
            await NoteStore.InsertNoteAsync("ts-desk", "Note", "Content", false, 0);

            var notes = await NoteStore.GetNotesForDesktopAsync("ts-desk");
            var note = notes.Single();
            note.CreatedAt.Should().Be(clock.Now);
            note.UpdatedAt.Should().Be(clock.Now);
        }
        finally
        {
            DatabaseCore.SetClock(SystemClock.Instance);
        }
    }

    [Fact]
    public async Task UpdateNoteContent_UpdatesTimestamp()
    {
        var clock = new TestClock();
        DatabaseCore.SetClock(clock);
        try
        {
            var id = await NoteStore.InsertNoteAsync("ts-update", "Note", "Old", false, 0);
            var originalTime = clock.Now;

            clock.Advance(TimeSpan.FromMinutes(5));
            await NoteStore.UpdateNoteContentAsync(id, "New");

            var notes = await NoteStore.GetNotesForDesktopAsync("ts-update");
            var note = notes.Single();
            note.UpdatedAt.Should().BeAfter(originalTime);
        }
        finally
        {
            DatabaseCore.SetClock(SystemClock.Instance);
        }
    }
}
