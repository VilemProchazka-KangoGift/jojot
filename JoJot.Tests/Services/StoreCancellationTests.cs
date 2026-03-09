using JoJot.Services;
using JoJot.Tests.Helpers;

namespace JoJot.Tests.Services;

/// <summary>
/// CancellationToken support tests for SessionStore, PreferenceStore, and PendingMoveStore.
/// Verifies that CT-accepting methods work with both default and explicit tokens.
/// </summary>
[Collection("Database")]
public class StoreCancellationTests : IAsyncLifetime
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

    // ─── SessionStore ────────────────────────────────────────────

    [Fact]
    public async Task SessionStore_GetAllSessions_WithCancellationToken()
    {
        await SessionStore.CreateSessionAsync("ct-sess-1", "Desktop 1", 0);
        using var cts = new CancellationTokenSource();
        var sessions = await SessionStore.GetAllSessionsAsync(cts.Token);
        sessions.Should().Contain(s => s.DesktopGuid == "ct-sess-1");
    }

    [Fact]
    public async Task SessionStore_GetDesktopName_WithCancellationToken()
    {
        await SessionStore.CreateSessionAsync("ct-name-1", "MyDesktop", 0);
        using var cts = new CancellationTokenSource();
        var name = await SessionStore.GetDesktopNameAsync("ct-name-1", cts.Token);
        name.Should().Be("MyDesktop");
    }

    [Fact]
    public async Task SessionStore_GetWindowGeometry_WithCancellationToken()
    {
        await SessionStore.CreateSessionAsync("ct-geo-1", "D", 0);
        var geo = new JoJot.Models.WindowGeometry(10, 20, 800, 600, false);
        await SessionStore.SaveWindowGeometryAsync("ct-geo-1", geo);
        using var cts = new CancellationTokenSource();
        var result = await SessionStore.GetWindowGeometryAsync("ct-geo-1", cts.Token);
        result.Should().NotBeNull();
        result!.Width.Should().Be(800);
    }

    [Fact]
    public async Task SessionStore_GetOrphanedSessionInfo_WithCancellationToken()
    {
        await SessionStore.CreateSessionAsync("ct-orphan-1", "Orphan", 0);
        await NoteStore.InsertNoteAsync("ct-orphan-1", "Note", "content", false, 0);
        using var cts = new CancellationTokenSource();
        var info = await SessionStore.GetOrphanedSessionInfoAsync(["ct-orphan-1"], cts.Token);
        info.Should().HaveCount(1);
    }

    // ─── PreferenceStore ─────────────────────────────────────────

    [Fact]
    public async Task PreferenceStore_GetPreference_WithCancellationToken()
    {
        await PreferenceStore.SetPreferenceAsync("ct-key", "ct-value");
        using var cts = new CancellationTokenSource();
        var value = await PreferenceStore.GetPreferenceAsync("ct-key", cts.Token);
        value.Should().Be("ct-value");
    }

    // ─── PendingMoveStore ────────────────────────────────────────

    [Fact]
    public async Task PendingMoveStore_GetPendingMoves_WithCancellationToken()
    {
        await PendingMoveStore.InsertPendingMoveAsync("ct-win-1", "from-guid", "to-guid");
        using var cts = new CancellationTokenSource();
        var moves = await PendingMoveStore.GetPendingMovesAsync(cts.Token);
        moves.Should().Contain(m => m.WindowId == "ct-win-1");
    }
}
