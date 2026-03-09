using JoJot.Services;
using JoJot.Tests.Helpers;

namespace JoJot.Tests.Services;

/// <summary>
/// Integration tests for DatabaseService using in-memory SQLite.
/// Tests run sequentially because DatabaseService uses static state.
/// </summary>
[Collection("Database")]
public class DatabaseServiceTests : IAsyncLifetime
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

    // ─── Note CRUD ─────────────────────────────────────────────────────

    [Fact]
    public async Task InsertNote_ReturnsPositiveId()
    {
        var id = await DatabaseService.InsertNoteAsync("desktop-1", "Test", "Content", false, 0);
        id.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetNotesForDesktop_ReturnsInsertedNotes()
    {
        await DatabaseService.InsertNoteAsync("desktop-2", "Note 1", "Content 1", false, 0);
        await DatabaseService.InsertNoteAsync("desktop-2", "Note 2", "Content 2", false, 1);
        await DatabaseService.InsertNoteAsync("other-desktop", "Other", "Other", false, 0);

        var notes = await DatabaseService.GetNotesForDesktopAsync("desktop-2");

        notes.Should().HaveCount(2);
        notes.Should().AllSatisfy(n => n.DesktopGuid.Should().Be("desktop-2"));
    }

    [Fact]
    public async Task UpdateNoteContent_PersistsChange()
    {
        var id = await DatabaseService.InsertNoteAsync("desktop-3", "Name", "Old content", false, 0);
        await DatabaseService.UpdateNoteContentAsync(id, "New content");

        var notes = await DatabaseService.GetNotesForDesktopAsync("desktop-3");
        notes.Should().ContainSingle().Which.Content.Should().Be("New content");
    }

    [Fact]
    public async Task UpdateNoteName_PersistsChange()
    {
        var id = await DatabaseService.InsertNoteAsync("desktop-4", "Old Name", "", false, 0);
        await DatabaseService.UpdateNoteNameAsync(id, "New Name");

        var notes = await DatabaseService.GetNotesForDesktopAsync("desktop-4");
        notes.Should().ContainSingle().Which.Name.Should().Be("New Name");
    }

    [Fact]
    public async Task UpdateNotePinned_TogglesPin()
    {
        var id = await DatabaseService.InsertNoteAsync("desktop-5", "Pin Test", "", false, 0);
        await DatabaseService.UpdateNotePinnedAsync(id, true);

        var notes = await DatabaseService.GetNotesForDesktopAsync("desktop-5");
        notes.Should().ContainSingle().Which.Pinned.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteNote_RemovesFromDatabase()
    {
        var id = await DatabaseService.InsertNoteAsync("desktop-6", "To Delete", "", false, 0);
        await DatabaseService.DeleteNoteAsync(id);

        var notes = await DatabaseService.GetNotesForDesktopAsync("desktop-6");
        notes.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteEmptyNotes_RemovesOnlyEmptyNotes()
    {
        await DatabaseService.InsertNoteAsync("desktop-7", null, "", false, 0);
        await DatabaseService.InsertNoteAsync("desktop-7", null, "has content", false, 1);

        var deleted = await DatabaseService.DeleteEmptyNotesAsync("desktop-7");

        deleted.Should().Be(1);
        var remaining = await DatabaseService.GetNotesForDesktopAsync("desktop-7");
        remaining.Should().ContainSingle().Which.Content.Should().Be("has content");
    }

    // ─── Sort Orders ───────────────────────────────────────────────────

    [Fact]
    public async Task UpdateNoteSortOrders_PersistsNewOrder()
    {
        var id1 = await DatabaseService.InsertNoteAsync("desktop-8", "A", "", false, 0);
        var id2 = await DatabaseService.InsertNoteAsync("desktop-8", "B", "", false, 1);

        await DatabaseService.UpdateNoteSortOrdersAsync([(id1, 10), (id2, 5)]);

        var notes = await DatabaseService.GetNotesForDesktopAsync("desktop-8");
        notes.First(n => n.Id == id1).SortOrder.Should().Be(10);
        notes.First(n => n.Id == id2).SortOrder.Should().Be(5);
    }

    [Fact]
    public async Task GetMaxSortOrder_ReturnsHighestSortOrder()
    {
        await DatabaseService.InsertNoteAsync("desktop-9", "A", "", false, 3);
        await DatabaseService.InsertNoteAsync("desktop-9", "B", "", false, 7);
        await DatabaseService.InsertNoteAsync("desktop-9", "C", "", false, 5);

        var max = await DatabaseService.GetMaxSortOrderAsync("desktop-9");
        max.Should().Be(7);
    }

    // ─── Note Queries ──────────────────────────────────────────────────

    [Fact]
    public async Task GetNoteCountForDesktop_ReturnsCorrectCount()
    {
        await DatabaseService.InsertNoteAsync("desktop-10", "A", "", false, 0);
        await DatabaseService.InsertNoteAsync("desktop-10", "B", "", false, 1);

        var count = await DatabaseService.GetNoteCountForDesktopAsync("desktop-10");
        count.Should().Be(2);
    }

    [Fact]
    public async Task GetNoteNamesForDesktop_ReturnsNames()
    {
        await DatabaseService.InsertNoteAsync("desktop-11", "Alpha", "", false, 0);
        await DatabaseService.InsertNoteAsync("desktop-11", "Beta", "", false, 1);

        var names = await DatabaseService.GetNoteNamesForDesktopAsync("desktop-11");
        names.Should().Contain("Alpha").And.Contain("Beta");
    }

    [Fact]
    public async Task GetNotePreviewsForDesktop_ReturnsPreviews()
    {
        await DatabaseService.InsertNoteAsync("desktop-12", "Preview", "Some content here", false, 0);

        var previews = await DatabaseService.GetNotePreviewsForDesktopAsync("desktop-12");
        previews.Should().ContainSingle();
        previews[0].Name.Should().Be("Preview");
    }

    // ─── Sessions ──────────────────────────────────────────────────────

    [Fact]
    public async Task CreateSession_And_GetAllSessions()
    {
        await DatabaseService.CreateSessionAsync("session-1", "Desktop 1", 0);
        await DatabaseService.CreateSessionAsync("session-2", "Desktop 2", 1);

        var sessions = await DatabaseService.GetAllSessionsAsync();
        sessions.Should().Contain(s => s.DesktopGuid == "session-1");
        sessions.Should().Contain(s => s.DesktopGuid == "session-2");
    }

    [Fact]
    public async Task UpdateDesktopName_PersistsChange()
    {
        await DatabaseService.CreateSessionAsync("session-3", "Old Name", 0);
        await DatabaseService.UpdateDesktopNameAsync("session-3", "New Name");

        var name = await DatabaseService.GetDesktopNameAsync("session-3");
        name.Should().Be("New Name");
    }

    // ─── Window Geometry ───────────────────────────────────────────────

    [Fact]
    public async Task SaveAndGetWindowGeometry_RoundTrips()
    {
        await DatabaseService.CreateSessionAsync("geo-1", "Geo Test", 0);
        var geo = new JoJot.Models.WindowGeometry(100.0, 200.0, 800.0, 600.0, false);
        await DatabaseService.SaveWindowGeometryAsync("geo-1", geo);

        var loaded = await DatabaseService.GetWindowGeometryAsync("geo-1");
        loaded.Should().NotBeNull();
        loaded!.Left.Should().Be(100.0);
        loaded.Top.Should().Be(200.0);
        loaded.Width.Should().Be(800.0);
        loaded.Height.Should().Be(600.0);
        loaded.IsMaximized.Should().BeFalse();
    }

    [Fact]
    public async Task GetWindowGeometry_ReturnsNull_WhenNoSession()
    {
        var geo = await DatabaseService.GetWindowGeometryAsync("nonexistent");
        geo.Should().BeNull();
    }

    // ─── Preferences ───────────────────────────────────────────────────

    [Fact]
    public async Task SetAndGetPreference_RoundTrips()
    {
        await DatabaseService.SetPreferenceAsync("test_key", "test_value");
        var value = await DatabaseService.GetPreferenceAsync("test_key");
        value.Should().Be("test_value");
    }

    [Fact]
    public async Task GetPreference_ReturnsNull_WhenNotSet()
    {
        var value = await DatabaseService.GetPreferenceAsync("nonexistent_key");
        value.Should().BeNull();
    }

    [Fact]
    public async Task SetPreference_OverwritesExisting()
    {
        await DatabaseService.SetPreferenceAsync("overwrite_key", "first");
        await DatabaseService.SetPreferenceAsync("overwrite_key", "second");

        var value = await DatabaseService.GetPreferenceAsync("overwrite_key");
        value.Should().Be("second");
    }

    // ─── Pending Moves ─────────────────────────────────────────────────

    [Fact]
    public async Task InsertAndGetPendingMoves_RoundTrips()
    {
        var id = await DatabaseService.InsertPendingMoveAsync("window-1", "from-guid", "to-guid");
        id.Should().BeGreaterThan(0);

        var moves = await DatabaseService.GetPendingMovesAsync();
        moves.Should().Contain(m => m.WindowId == "window-1");
    }

    [Fact]
    public async Task DeletePendingMove_RemovesMove()
    {
        await DatabaseService.InsertPendingMoveAsync("window-2", "from", "to");
        await DatabaseService.DeletePendingMoveAsync("window-2");

        var moves = await DatabaseService.GetPendingMovesAsync();
        moves.Should().NotContain(m => m.WindowId == "window-2");
    }

    // ─── Tab Migration ─────────────────────────────────────────────────

    [Fact]
    public async Task MigrateTabsAsync_MovesNotesToTargetDesktop()
    {
        await DatabaseService.InsertNoteAsync("source-desk", "Tab 1", "C1", false, 0);
        await DatabaseService.InsertNoteAsync("source-desk", "Tab 2", "C2", false, 1);

        await DatabaseService.CreateSessionAsync("source-desk", "Source", 0);
        await DatabaseService.CreateSessionAsync("target-desk", "Target", 1);

        await DatabaseService.MigrateTabsAsync("source-desk", "target-desk");

        var source = await DatabaseService.GetNotesForDesktopAsync("source-desk");
        var target = await DatabaseService.GetNotesForDesktopAsync("target-desk");

        source.Should().BeEmpty();
        target.Should().HaveCount(2);
    }

    // ─── Delete Session And Notes ──────────────────────────────────────

    [Fact]
    public async Task DeleteSessionAndNotes_RemovesBoth()
    {
        await DatabaseService.InsertNoteAsync("delete-desk", "Tab", "Content", false, 0);
        await DatabaseService.CreateSessionAsync("delete-desk", "Delete Me", 0);

        await DatabaseService.DeleteSessionAndNotesAsync("delete-desk");

        var notes = await DatabaseService.GetNotesForDesktopAsync("delete-desk");
        notes.Should().BeEmpty();

        var sessions = await DatabaseService.GetAllSessionsAsync();
        sessions.Should().NotContain(s => s.DesktopGuid == "delete-desk");
    }
}
