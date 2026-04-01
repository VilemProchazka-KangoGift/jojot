using JoJot.Services;
using JoJot.Tests.Helpers;

namespace JoJot.Tests.Services;

/// <summary>
/// Integration tests for SessionStore operations beyond basic CRUD
/// (which is already covered in DatabaseServiceTests).
/// </summary>
[Collection("Database")]
public class SessionStoreTests : IAsyncLifetime
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

    // ─── CreateSession ────────────────────────────────────────────────

    [Fact]
    public async Task CreateSession_DoesNotDuplicate_WhenCalledTwice()
    {
        await SessionStore.CreateSessionAsync("dup-1", "Desktop", 0);
        await SessionStore.CreateSessionAsync("dup-1", "Desktop Again", 1);

        var sessions = await SessionStore.GetAllSessionsAsync();
        sessions.Count(s => s.DesktopGuid == "dup-1").Should().Be(1);
    }

    [Fact]
    public async Task CreateSession_PreservesNameAndIndex()
    {
        await SessionStore.CreateSessionAsync("named-1", "My Desktop", 3);

        var sessions = await SessionStore.GetAllSessionsAsync();
        var session = sessions.Single(s => s.DesktopGuid == "named-1");
        session.DesktopName.Should().Be("My Desktop");
        session.DesktopIndex.Should().Be(3);
    }

    [Fact]
    public async Task CreateSession_AllowsNullNameAndIndex()
    {
        await SessionStore.CreateSessionAsync("null-1", null, null);

        var sessions = await SessionStore.GetAllSessionsAsync();
        var session = sessions.Single(s => s.DesktopGuid == "null-1");
        session.DesktopName.Should().BeNull();
        session.DesktopIndex.Should().BeNull();
    }

    // ─── UpdateSession ────────────────────────────────────────────────

    [Fact]
    public async Task UpdateSession_UpdatesGuidAndProperties()
    {
        await SessionStore.CreateSessionAsync("old-guid", "Old", 0);

        await SessionStore.UpdateSessionAsync("old-guid", "new-guid", "New Name", 2);

        var sessions = await SessionStore.GetAllSessionsAsync();
        sessions.Should().NotContain(s => s.DesktopGuid == "old-guid");
        var updated = sessions.Single(s => s.DesktopGuid == "new-guid");
        updated.DesktopName.Should().Be("New Name");
        updated.DesktopIndex.Should().Be(2);
    }

    [Fact]
    public async Task UpdateSession_MigratesNotesToNewGuid()
    {
        await SessionStore.CreateSessionAsync("src-guid", "Source", 0);
        await NoteStore.InsertNoteAsync("src-guid", "Note 1", "Content", false, 0);

        await SessionStore.UpdateSessionAsync("src-guid", "dst-guid", "Dest", 1);

        var srcNotes = await NoteStore.GetNotesForDesktopAsync("src-guid");
        var dstNotes = await NoteStore.GetNotesForDesktopAsync("dst-guid");
        srcNotes.Should().BeEmpty();
        dstNotes.Should().ContainSingle().Which.Name.Should().Be("Note 1");
    }

    [Fact]
    public async Task UpdateSession_SameGuid_DoesNotMigrateNotes()
    {
        await SessionStore.CreateSessionAsync("same-guid", "Desktop", 0);
        await NoteStore.InsertNoteAsync("same-guid", "Note", "Content", false, 0);

        await SessionStore.UpdateSessionAsync("same-guid", "same-guid", "Updated Name", 1);

        var notes = await NoteStore.GetNotesForDesktopAsync("same-guid");
        notes.Should().ContainSingle();
    }

    // ─── UpdateSessionDesktop ─────────────────────────────────────────

    [Fact]
    public async Task UpdateSessionDesktop_DeletesExistingTarget()
    {
        await SessionStore.CreateSessionAsync("old-desk", "Old", 0);
        await SessionStore.CreateSessionAsync("new-desk", "Existing", 1);

        await SessionStore.UpdateSessionDesktopAsync("old-desk", "new-desk", "Merged", 1);

        var sessions = await SessionStore.GetAllSessionsAsync();
        sessions.Should().NotContain(s => s.DesktopGuid == "old-desk");
        sessions.Count(s => s.DesktopGuid == "new-desk").Should().Be(1);
        sessions.Single(s => s.DesktopGuid == "new-desk").DesktopName.Should().Be("Merged");
    }

    // ─── GetDesktopName ───────────────────────────────────────────────

    [Fact]
    public async Task GetDesktopName_ReturnsNull_WhenNotFound()
    {
        var name = await SessionStore.GetDesktopNameAsync("nonexistent");
        name.Should().BeNull();
    }

    [Fact]
    public async Task GetDesktopName_ReturnsName()
    {
        await SessionStore.CreateSessionAsync("name-test", "Work Desktop", 0);

        var name = await SessionStore.GetDesktopNameAsync("name-test");
        name.Should().Be("Work Desktop");
    }

    // ─── Window Geometry ──────────────────────────────────────────────

    [Fact]
    public async Task SaveAndGetGeometry_MaximizedState_RoundTrips()
    {
        await SessionStore.CreateSessionAsync("geo-max", "Geo", 0);
        var geo = new JoJot.Models.WindowGeometry(50, 75, 1920, 1080, true);
        await SessionStore.SaveWindowGeometryAsync("geo-max", geo);

        var loaded = await SessionStore.GetWindowGeometryAsync("geo-max");
        loaded.Should().NotBeNull();
        loaded!.IsMaximized.Should().BeTrue();
        loaded.Width.Should().Be(1920);
        loaded.Height.Should().Be(1080);
    }

    [Fact]
    public async Task GetWindowGeometry_ReturnsNull_WhenNoGeometrySaved()
    {
        await SessionStore.CreateSessionAsync("geo-empty", "No Geo", 0);

        var geo = await SessionStore.GetWindowGeometryAsync("geo-empty");
        geo.Should().BeNull();
    }

    [Fact]
    public async Task SaveGeometry_Overwrites_PreviousGeometry()
    {
        await SessionStore.CreateSessionAsync("geo-update", "Geo Update", 0);

        var geo1 = new JoJot.Models.WindowGeometry(100, 200, 800, 600, false);
        await SessionStore.SaveWindowGeometryAsync("geo-update", geo1);

        var geo2 = new JoJot.Models.WindowGeometry(300, 400, 1024, 768, true);
        await SessionStore.SaveWindowGeometryAsync("geo-update", geo2);

        var loaded = await SessionStore.GetWindowGeometryAsync("geo-update");
        loaded!.Left.Should().Be(300);
        loaded.Top.Should().Be(400);
        loaded.Width.Should().Be(1024);
        loaded.Height.Should().Be(768);
        loaded.IsMaximized.Should().BeTrue();
    }

    // ─── Orphaned Sessions ────────────────────────────────────────────

    [Fact]
    public async Task GetOrphanedSessionInfo_EmptyList_ReturnsEmpty()
    {
        var results = await SessionStore.GetOrphanedSessionInfoAsync([]);
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task GetOrphanedSessionInfo_ReturnsTabCountAndDate()
    {
        await SessionStore.CreateSessionAsync("orphan-1", "Orphan Desktop", 0);
        await NoteStore.InsertNoteAsync("orphan-1", "Tab 1", "content 1", false, 0);
        await NoteStore.InsertNoteAsync("orphan-1", "Tab 2", "content 2", false, 1);

        var results = await SessionStore.GetOrphanedSessionInfoAsync(["orphan-1"]);

        results.Should().ContainSingle();
        results[0].DesktopGuid.Should().Be("orphan-1");
        results[0].DesktopName.Should().Be("Orphan Desktop");
        results[0].TabCount.Should().Be(2);
    }

    [Fact]
    public async Task GetOrphanedSessionInfo_NoNotes_ReturnsZeroCount()
    {
        await SessionStore.CreateSessionAsync("orphan-empty", "Empty", 0);

        var results = await SessionStore.GetOrphanedSessionInfoAsync(["orphan-empty"]);

        results.Should().ContainSingle();
        results[0].TabCount.Should().Be(0);
    }

    [Fact]
    public async Task GetOrphanedSessionInfo_MultipleOrphans_BatchedInTwoQueries()
    {
        // Setup 3 orphan sessions with varying note counts
        await SessionStore.CreateSessionAsync("batch-1", "Desktop A", 0);
        await NoteStore.InsertNoteAsync("batch-1", "Tab A1", "content", false, 0);
        await NoteStore.InsertNoteAsync("batch-1", "Tab A2", "content", false, 1);
        await NoteStore.InsertNoteAsync("batch-1", "Tab A3", "content", false, 2);

        await SessionStore.CreateSessionAsync("batch-2", "Desktop B", 1);
        await NoteStore.InsertNoteAsync("batch-2", "Tab B1", "content", false, 0);

        await SessionStore.CreateSessionAsync("batch-3", null, 2); // No name

        var results = await SessionStore.GetOrphanedSessionInfoAsync(["batch-1", "batch-2", "batch-3"]);

        results.Should().HaveCount(3);

        var r1 = results.Single(r => r.DesktopGuid == "batch-1");
        r1.DesktopName.Should().Be("Desktop A");
        r1.TabCount.Should().Be(3);

        var r2 = results.Single(r => r.DesktopGuid == "batch-2");
        r2.DesktopName.Should().Be("Desktop B");
        r2.TabCount.Should().Be(1);

        var r3 = results.Single(r => r.DesktopGuid == "batch-3");
        r3.DesktopName.Should().BeNull();
        r3.TabCount.Should().Be(0);
    }

    [Fact]
    public async Task GetOrphanedSessionInfo_PreservesInputOrder()
    {
        await SessionStore.CreateSessionAsync("order-z", "Z", 0);
        await SessionStore.CreateSessionAsync("order-a", "A", 1);
        await NoteStore.InsertNoteAsync("order-z", "Note", "c", false, 0);
        await NoteStore.InsertNoteAsync("order-a", "Note", "c", false, 0);

        // Request in z, a order — results should preserve that order
        var results = await SessionStore.GetOrphanedSessionInfoAsync(["order-z", "order-a"]);

        results.Should().HaveCount(2);
        results[0].DesktopGuid.Should().Be("order-z");
        results[1].DesktopGuid.Should().Be("order-a");
    }

    [Fact]
    public async Task GetOrphanedSessionInfo_UnknownGuid_ReturnsNullNameZeroCount()
    {
        // GUID with no session or notes at all
        var results = await SessionStore.GetOrphanedSessionInfoAsync(["nonexistent-guid"]);

        results.Should().ContainSingle();
        results[0].DesktopGuid.Should().Be("nonexistent-guid");
        results[0].DesktopName.Should().BeNull();
        results[0].TabCount.Should().Be(0);
        results[0].LastUpdated.Should().Be(new DateTime(2000, 1, 1));
    }

    [Fact]
    public async Task GetOrphanedSessionInfo_LastUpdated_ReturnsMaxAcrossNotes()
    {
        await SessionStore.CreateSessionAsync("lu-test", "LU", 0);
        // Insert notes — the one with the latest UpdatedAt should be returned
        await NoteStore.InsertNoteAsync("lu-test", "Old", "old content", false, 0);
        await NoteStore.InsertNoteAsync("lu-test", "New", "new content", false, 1);

        var results = await SessionStore.GetOrphanedSessionInfoAsync(["lu-test"]);
        var notes = await NoteStore.GetNotesForDesktopAsync("lu-test");
        var expectedMax = notes.Max(n => n.UpdatedAt);

        results[0].LastUpdated.Should().Be(expectedMax);
    }

    [Fact]
    public async Task DeleteSessionAndNotes_RemovesBoth()
    {
        await SessionStore.CreateSessionAsync("del-sess", "Delete Me", 0);
        await NoteStore.InsertNoteAsync("del-sess", "Note", "Content", false, 0);

        await SessionStore.DeleteSessionAndNotesAsync("del-sess");

        var sessions = await SessionStore.GetAllSessionsAsync();
        sessions.Should().NotContain(s => s.DesktopGuid == "del-sess");

        var notes = await NoteStore.GetNotesForDesktopAsync("del-sess");
        notes.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteSessionAndNotes_NoNotes_StillDeletesSession()
    {
        await SessionStore.CreateSessionAsync("del-no-notes", "Delete", 0);

        await SessionStore.DeleteSessionAndNotesAsync("del-no-notes");

        var sessions = await SessionStore.GetAllSessionsAsync();
        sessions.Should().NotContain(s => s.DesktopGuid == "del-no-notes");
    }
}
