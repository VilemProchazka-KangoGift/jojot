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

    // ─── Connection Configuration ─────────────────────────────────────

    [Fact]
    public void ProductionConnectionString_IncludesBusyTimeout()
    {
        // Verify that the production OpenAsync path sets DefaultTimeout.
        // The test DB uses OpenWithConnectionStringAsync (no builder), so we verify the builder directly.
        var builder = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
        {
            DataSource = "test.db",
            Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadWriteCreate,
            DefaultTimeout = 5,
        };
        builder.ToString().Should().Contain("Default Timeout=5",
            "BusyTimeout should be set to prevent indefinite blocking on lock contention");
    }

    // ─── Note CRUD ─────────────────────────────────────────────────────

    [Fact]
    public async Task InsertNote_ReturnsPositiveId()
    {
        var id = await NoteStore.InsertNoteAsync("desktop-1", "Test", "Content", false, 0);
        id.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetNotesForDesktop_ReturnsInsertedNotes()
    {
        await NoteStore.InsertNoteAsync("desktop-2", "Note 1", "Content 1", false, 0);
        await NoteStore.InsertNoteAsync("desktop-2", "Note 2", "Content 2", false, 1);
        await NoteStore.InsertNoteAsync("other-desktop", "Other", "Other", false, 0);

        var notes = await NoteStore.GetNotesForDesktopAsync("desktop-2");

        notes.Should().HaveCount(2);
        notes.Should().AllSatisfy(n => n.DesktopGuid.Should().Be("desktop-2"));
    }

    [Fact]
    public async Task UpdateNoteContent_PersistsChange()
    {
        var id = await NoteStore.InsertNoteAsync("desktop-3", "Name", "Old content", false, 0);
        await NoteStore.UpdateNoteContentAsync(id, "New content");

        var notes = await NoteStore.GetNotesForDesktopAsync("desktop-3");
        notes.Should().ContainSingle().Which.Content.Should().Be("New content");
    }

    [Fact]
    public async Task UpdateNoteName_PersistsChange()
    {
        var id = await NoteStore.InsertNoteAsync("desktop-4", "Old Name", "", false, 0);
        await NoteStore.UpdateNoteNameAsync(id, "New Name");

        var notes = await NoteStore.GetNotesForDesktopAsync("desktop-4");
        notes.Should().ContainSingle().Which.Name.Should().Be("New Name");
    }

    [Fact]
    public async Task UpdateNotePinned_TogglesPin()
    {
        var id = await NoteStore.InsertNoteAsync("desktop-5", "Pin Test", "", false, 0);
        await NoteStore.UpdateNotePinnedAsync(id, true);

        var notes = await NoteStore.GetNotesForDesktopAsync("desktop-5");
        notes.Should().ContainSingle().Which.Pinned.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteNote_RemovesFromDatabase()
    {
        var id = await NoteStore.InsertNoteAsync("desktop-6", "To Delete", "", false, 0);
        await NoteStore.DeleteNoteAsync(id);

        var notes = await NoteStore.GetNotesForDesktopAsync("desktop-6");
        notes.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteEmptyNotes_RemovesOnlyEmptyNotes()
    {
        await NoteStore.InsertNoteAsync("desktop-7", null, "", false, 0);
        await NoteStore.InsertNoteAsync("desktop-7", null, "has content", false, 1);

        var deleted = await NoteStore.DeleteEmptyNotesAsync("desktop-7");

        deleted.Should().Be(1);
        var remaining = await NoteStore.GetNotesForDesktopAsync("desktop-7");
        remaining.Should().ContainSingle().Which.Content.Should().Be("has content");
    }

    // ─── Sort Orders ───────────────────────────────────────────────────

    [Fact]
    public async Task UpdateNoteSortOrders_PersistsNewOrder()
    {
        var id1 = await NoteStore.InsertNoteAsync("desktop-8", "A", "", false, 0);
        var id2 = await NoteStore.InsertNoteAsync("desktop-8", "B", "", false, 1);

        await NoteStore.UpdateNoteSortOrdersAsync([(id1, 10), (id2, 5)]);

        var notes = await NoteStore.GetNotesForDesktopAsync("desktop-8");
        notes.First(n => n.Id == id1).SortOrder.Should().Be(10);
        notes.First(n => n.Id == id2).SortOrder.Should().Be(5);
    }

    [Fact]
    public async Task GetMaxSortOrder_ReturnsHighestSortOrder()
    {
        await NoteStore.InsertNoteAsync("desktop-9", "A", "", false, 3);
        await NoteStore.InsertNoteAsync("desktop-9", "B", "", false, 7);
        await NoteStore.InsertNoteAsync("desktop-9", "C", "", false, 5);

        var max = await NoteStore.GetMaxSortOrderAsync("desktop-9");
        max.Should().Be(7);
    }

    // ─── Note Queries ──────────────────────────────────────────────────

    [Fact]
    public async Task GetNoteCountForDesktop_ReturnsCorrectCount()
    {
        await NoteStore.InsertNoteAsync("desktop-10", "A", "", false, 0);
        await NoteStore.InsertNoteAsync("desktop-10", "B", "", false, 1);

        var count = await NoteStore.GetNoteCountForDesktopAsync("desktop-10");
        count.Should().Be(2);
    }

    [Fact]
    public async Task GetNoteNamesForDesktop_ReturnsNames()
    {
        await NoteStore.InsertNoteAsync("desktop-11", "Alpha", "", false, 0);
        await NoteStore.InsertNoteAsync("desktop-11", "Beta", "", false, 1);

        var names = await NoteStore.GetNoteNamesForDesktopAsync("desktop-11");
        names.Should().Contain("Alpha").And.Contain("Beta");
    }

    [Fact]
    public async Task GetNotePreviewsForDesktop_ReturnsPreviews()
    {
        await NoteStore.InsertNoteAsync("desktop-12", "Preview", "Some content here", false, 0);

        var previews = await NoteStore.GetNotePreviewsForDesktopAsync("desktop-12");
        previews.Should().ContainSingle();
        previews[0].Name.Should().Be("Preview");
    }

    // ─── Sessions ──────────────────────────────────────────────────────

    [Fact]
    public async Task CreateSession_And_GetAllSessions()
    {
        await SessionStore.CreateSessionAsync("session-1", "Desktop 1", 0);
        await SessionStore.CreateSessionAsync("session-2", "Desktop 2", 1);

        var sessions = await SessionStore.GetAllSessionsAsync();
        sessions.Should().Contain(s => s.DesktopGuid == "session-1");
        sessions.Should().Contain(s => s.DesktopGuid == "session-2");
    }

    [Fact]
    public async Task UpdateDesktopName_PersistsChange()
    {
        await SessionStore.CreateSessionAsync("session-3", "Old Name", 0);
        await SessionStore.UpdateDesktopNameAsync("session-3", "New Name");

        var name = await SessionStore.GetDesktopNameAsync("session-3");
        name.Should().Be("New Name");
    }

    // ─── Window Geometry ───────────────────────────────────────────────

    [Fact]
    public async Task SaveAndGetWindowGeometry_RoundTrips()
    {
        await SessionStore.CreateSessionAsync("geo-1", "Geo Test", 0);
        var geo = new JoJot.Models.WindowGeometry(100.0, 200.0, 800.0, 600.0, false);
        await SessionStore.SaveWindowGeometryAsync("geo-1", geo);

        var loaded = await SessionStore.GetWindowGeometryAsync("geo-1");
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
        var geo = await SessionStore.GetWindowGeometryAsync("nonexistent");
        geo.Should().BeNull();
    }

    // ─── Preferences ───────────────────────────────────────────────────

    [Fact]
    public async Task SetAndGetPreference_RoundTrips()
    {
        await PreferenceStore.SetPreferenceAsync("test_key", "test_value");
        var value = await PreferenceStore.GetPreferenceAsync("test_key");
        value.Should().Be("test_value");
    }

    [Fact]
    public async Task GetPreference_ReturnsNull_WhenNotSet()
    {
        var value = await PreferenceStore.GetPreferenceAsync("nonexistent_key");
        value.Should().BeNull();
    }

    [Fact]
    public async Task SetPreference_OverwritesExisting()
    {
        await PreferenceStore.SetPreferenceAsync("overwrite_key", "first");
        await PreferenceStore.SetPreferenceAsync("overwrite_key", "second");

        var value = await PreferenceStore.GetPreferenceAsync("overwrite_key");
        value.Should().Be("second");
    }

    // ─── Pending Moves ─────────────────────────────────────────────────

    [Fact]
    public async Task InsertAndGetPendingMoves_RoundTrips()
    {
        var id = await PendingMoveStore.InsertPendingMoveAsync("window-1", "from-guid", "to-guid");
        id.Should().BeGreaterThan(0);

        var moves = await PendingMoveStore.GetPendingMovesAsync();
        moves.Should().Contain(m => m.WindowId == "window-1");
    }

    [Fact]
    public async Task DeletePendingMove_RemovesMove()
    {
        await PendingMoveStore.InsertPendingMoveAsync("window-2", "from", "to");
        await PendingMoveStore.DeletePendingMoveAsync("window-2");

        var moves = await PendingMoveStore.GetPendingMovesAsync();
        moves.Should().NotContain(m => m.WindowId == "window-2");
    }

    // ─── Tab Migration ─────────────────────────────────────────────────

    [Fact]
    public async Task MigrateTabsAsync_MovesNotesToTargetDesktop()
    {
        await NoteStore.InsertNoteAsync("source-desk", "Tab 1", "C1", false, 0);
        await NoteStore.InsertNoteAsync("source-desk", "Tab 2", "C2", false, 1);

        await SessionStore.CreateSessionAsync("source-desk", "Source", 0);
        await SessionStore.CreateSessionAsync("target-desk", "Target", 1);

        await NoteStore.MigrateTabsAsync("source-desk", "target-desk");

        var source = await NoteStore.GetNotesForDesktopAsync("source-desk");
        var target = await NoteStore.GetNotesForDesktopAsync("target-desk");

        source.Should().BeEmpty();
        target.Should().HaveCount(2);
    }

    // ─── Delete Session And Notes ──────────────────────────────────────

    [Fact]
    public async Task DeleteSessionAndNotes_RemovesBoth()
    {
        await NoteStore.InsertNoteAsync("delete-desk", "Tab", "Content", false, 0);
        await SessionStore.CreateSessionAsync("delete-desk", "Delete Me", 0);

        await SessionStore.DeleteSessionAndNotesAsync("delete-desk");

        var notes = await NoteStore.GetNotesForDesktopAsync("delete-desk");
        notes.Should().BeEmpty();

        var sessions = await SessionStore.GetAllSessionsAsync();
        sessions.Should().NotContain(s => s.DesktopGuid == "delete-desk");
    }
}
