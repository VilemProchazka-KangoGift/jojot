using JoJot.Services;
using JoJot.Tests.Helpers;

namespace JoJot.Tests.Services;

/// <summary>
/// Integration tests for PendingMoveStore operations beyond basic CRUD
/// (which is already covered in DatabaseServiceTests).
/// </summary>
[Collection("Database")]
public class PendingMoveStoreTests : IAsyncLifetime
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

    [Fact]
    public async Task InsertPendingMove_ReturnsPositiveId()
    {
        var id = await PendingMoveStore.InsertPendingMoveAsync("win-1", "from-desk", "to-desk");
        id.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task InsertPendingMove_AllowsNullToDesktop()
    {
        var id = await PendingMoveStore.InsertPendingMoveAsync("win-2", "from-desk", null);
        id.Should().BeGreaterThan(0);

        var moves = await PendingMoveStore.GetPendingMovesAsync();
        var move = moves.Single(m => m.WindowId == "win-2");
        move.ToDesktop.Should().BeNull();
    }

    [Fact]
    public async Task InsertPendingMove_StoresDetectedAtTimestamp()
    {
        var clock = new TestClock();
        DatabaseCore.SetClock(clock);
        try
        {
            await PendingMoveStore.InsertPendingMoveAsync("win-ts", "from", "to");

            var moves = await PendingMoveStore.GetPendingMovesAsync();
            var move = moves.Single(m => m.WindowId == "win-ts");
            move.DetectedAt.Should().Contain("2025-06-15");
        }
        finally
        {
            DatabaseCore.SetClock(new JoJot.Services.SystemClock());
        }
    }

    [Fact]
    public async Task DeletePendingMove_RemovesOnlyTargetWindow()
    {
        await PendingMoveStore.InsertPendingMoveAsync("win-a", "from", "to");
        await PendingMoveStore.InsertPendingMoveAsync("win-b", "from", "to");

        await PendingMoveStore.DeletePendingMoveAsync("win-a");

        var moves = await PendingMoveStore.GetPendingMovesAsync();
        moves.Should().NotContain(m => m.WindowId == "win-a");
        moves.Should().Contain(m => m.WindowId == "win-b");
    }

    [Fact]
    public async Task DeletePendingMove_NoOp_WhenWindowNotFound()
    {
        // Should not throw
        await PendingMoveStore.DeletePendingMoveAsync("nonexistent-window");
    }

    [Fact]
    public async Task DeleteAllPendingMoves_ClearsAllRows()
    {
        await PendingMoveStore.InsertPendingMoveAsync("win-x", "from-1", "to-1");
        await PendingMoveStore.InsertPendingMoveAsync("win-y", "from-2", "to-2");
        await PendingMoveStore.InsertPendingMoveAsync("win-z", "from-3", "to-3");

        await PendingMoveStore.DeleteAllPendingMovesAsync();

        var moves = await PendingMoveStore.GetPendingMovesAsync();
        moves.Should().BeEmpty();
    }

    [Fact]
    public async Task GetPendingMoves_ReturnsAllFields()
    {
        await PendingMoveStore.InsertPendingMoveAsync("win-fields", "desk-from", "desk-to");

        var moves = await PendingMoveStore.GetPendingMovesAsync();
        var move = moves.Single(m => m.WindowId == "win-fields");

        move.Id.Should().BeGreaterThan(0);
        move.WindowId.Should().Be("win-fields");
        move.FromDesktop.Should().Be("desk-from");
        move.ToDesktop.Should().Be("desk-to");
        move.DetectedAt.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GetPendingMoves_ReturnsEmpty_WhenNoneExist()
    {
        var moves = await PendingMoveStore.GetPendingMovesAsync();
        moves.Should().BeEmpty();
    }

    [Fact]
    public async Task MultipleMovesForSameWindow_AllPersisted()
    {
        await PendingMoveStore.InsertPendingMoveAsync("win-multi", "desk-a", "desk-b");
        await PendingMoveStore.InsertPendingMoveAsync("win-multi", "desk-b", "desk-c");

        var moves = await PendingMoveStore.GetPendingMovesAsync();
        moves.Count(m => m.WindowId == "win-multi").Should().Be(2);
    }
}
