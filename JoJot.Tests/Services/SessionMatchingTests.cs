using JoJot.Models;
using JoJot.Services;
using JoJot.Tests.Helpers;

namespace JoJot.Tests.Services;

/// <summary>
/// Session matching against a real (in-memory) database. The tiers run through
/// <c>MatchSessionsCoreAsync</c> with an explicit live-desktop list, so no COM is needed; the
/// public <c>MatchSessionsAsync</c> takes the fallback branch in tests (COM unavailable).
/// </summary>
[Collection("Database")]
public class SessionMatchingTests : IAsyncLifetime
{
    private TestDatabase _db = null!;

    private static readonly Guid DeskA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid DeskB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    public async Task InitializeAsync()
    {
        _db = await TestDatabase.CreateAsync();
    }

    public async Task DisposeAsync()
    {
        VirtualDesktopService.SetOrphanedSessionGuids([]);
        await _db.DisposeAsync();
    }

    // ─── Tier 1 ───────────────────────────────────────────────────────

    [Fact]
    public async Task Tier1_UnchangedNameAndIndex_IssuesNoUpdate()
    {
        await SessionStore.CreateSessionAsync(DeskA.ToString(), "Work", 0);

        var result = await VirtualDesktopService.MatchSessionsCoreAsync([new DesktopInfo(DeskA, "Work", 0)]);

        result.Tier1Matched.Should().Be(1);
        result.Tier1Updated.Should().Be(0);
        var session = (await SessionStore.GetAllSessionsAsync()).Single();
        session.DesktopName.Should().Be("Work");
        session.DesktopIndex.Should().Be(0);
    }

    [Fact]
    public async Task Tier1_StoredNullName_LiveEmptyName_IssuesNoUpdate()
    {
        await SessionStore.CreateSessionAsync(DeskA.ToString(), null, 0);

        var result = await VirtualDesktopService.MatchSessionsCoreAsync([new DesktopInfo(DeskA, "", 0)]);

        result.Tier1Updated.Should().Be(0);
    }

    [Fact]
    public async Task Tier1_RenamedOrReorderedDesktop_RefreshesRow()
    {
        await SessionStore.CreateSessionAsync(DeskA.ToString(), "Old", 0);
        await SessionStore.CreateSessionAsync(DeskB.ToString(), "Same", 1);

        var result = await VirtualDesktopService.MatchSessionsCoreAsync(
            [new DesktopInfo(DeskA, "New", 0), new DesktopInfo(DeskB, "Same", 3)]);

        result.Tier1Matched.Should().Be(2);
        result.Tier1Updated.Should().Be(2);
        var sessions = await SessionStore.GetAllSessionsAsync();
        sessions.Single(s => s.DesktopGuid == DeskA.ToString()).DesktopName.Should().Be("New");
        sessions.Single(s => s.DesktopGuid == DeskB.ToString()).DesktopIndex.Should().Be(3);
    }

    [Theory]
    [InlineData("Work", 0, "Work", 0, false)]
    [InlineData(null, 0, "", 0, false)]
    [InlineData("Work", 0, "Play", 0, true)]
    [InlineData("Work", 0, "Work", 1, true)]
    [InlineData("Work", null, "Work", 0, true)]
    public void NeedsSessionRefresh_ComparesNameAndIndex(string? storedName, int? storedIndex, string liveName, int liveIndex, bool expected)
    {
        VirtualDesktopService.NeedsSessionRefresh(storedName, storedIndex, new DesktopInfo(DeskA, liveName, liveIndex))
            .Should().Be(expected);
    }

    // ─── Tier 2 still re-homes a session onto a desktop with a new GUID ──

    [Fact]
    public async Task Tier2_NameMatch_MovesSessionAndNotesToNewGuid()
    {
        await SessionStore.CreateSessionAsync("old-guid", "Work", 0);
        await NoteStore.InsertNoteAsync("old-guid", null, "kept", false, 0);

        var result = await VirtualDesktopService.MatchSessionsCoreAsync([new DesktopInfo(DeskA, "Work", 5)]);

        result.Tier2Matched.Should().Be(1);
        (await NoteStore.GetNotesForDesktopAsync(DeskA.ToString())).Should().ContainSingle();
        (await SessionStore.SessionExistsAsync("old-guid")).Should().BeFalse();
    }

    // ─── Orphans (batched note-count query) ───────────────────────────

    [Fact]
    public async Task Orphans_EmptyAreAutoDeleted_NonEmptyAreKept()
    {
        await SessionStore.CreateSessionAsync(DeskA.ToString(), "Live", 0);
        await SessionStore.CreateSessionAsync("orphan-empty", null, null);
        await SessionStore.CreateSessionAsync("orphan-full", null, null);
        await NoteStore.InsertNoteAsync("orphan-full", null, "content", false, 0);

        var result = await VirtualDesktopService.MatchSessionsCoreAsync([new DesktopInfo(DeskA, "Live", 0)]);

        result.Orphans.Should().BeEquivalentTo(["orphan-full"]);
        VirtualDesktopService.OrphanedSessionGuids.Should().BeEquivalentTo(["orphan-full"]);
        (await SessionStore.SessionExistsAsync("orphan-empty")).Should().BeFalse();
        (await SessionStore.SessionExistsAsync("orphan-full")).Should().BeTrue();
    }

    [Fact]
    public async Task Fallback_EmptyOrphansAutoDeleted_NonEmptyKept_DefaultCreated()
    {
        await SessionStore.CreateSessionAsync("fb-empty", null, null);
        await SessionStore.CreateSessionAsync("fb-full", null, null);
        await NoteStore.InsertNoteAsync("fb-full", null, "content", false, 0);

        // COM is unavailable under test, so the public entry point takes the fallback branch.
        await VirtualDesktopService.MatchSessionsAsync();

        VirtualDesktopService.OrphanedSessionGuids.Should().BeEquivalentTo(["fb-full"]);
        (await SessionStore.SessionExistsAsync("fb-empty")).Should().BeFalse();
        (await SessionStore.SessionExistsAsync("default")).Should().BeTrue();
    }

    [Fact]
    public async Task NewDesktop_GetsSessionRow()
    {
        var result = await VirtualDesktopService.MatchSessionsCoreAsync([new DesktopInfo(DeskB, "Fresh", 2)]);

        result.NewSessions.Should().Be(1);
        (await SessionStore.SessionExistsAsync(DeskB.ToString())).Should().BeTrue();
    }
}
