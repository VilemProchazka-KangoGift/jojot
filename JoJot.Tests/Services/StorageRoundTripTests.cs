using JoJot.Services;
using JoJot.Tests.Helpers;

namespace JoJot.Tests.Services;

/// <summary>
/// Exercises every table through the stores against the production-created schema.
/// </summary>
[Collection("Database")]
public class StorageRoundTripTests : IAsyncLifetime
{
    private TestDatabase _db = null!;

    public async Task InitializeAsync() => _db = await TestDatabase.CreateAsync();
    public async Task DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task AllTables_RoundTripThroughStores()
    {
        long id = await NoteStore.InsertNoteAsync("desk-1", "name", "content", true, 3);
        (await NoteStore.GetNotesForDesktopAsync("desk-1")).Should().ContainSingle(n => n.Id == id);

        await SessionStore.CreateSessionAsync("desk-1", "Desktop", 2);
        (await SessionStore.GetAllSessionsAsync()).Should().ContainSingle(s => s.DesktopGuid == "desk-1" && s.DesktopIndex == 2);

        await PreferenceStore.SetPreferenceAsync("cm_key", "cm_value");
        (await PreferenceStore.GetPreferenceAsync("cm_key")).Should().Be("cm_value");

        await PendingMoveStore.InsertPendingMoveAsync("win-1", "from-d", "to-d");
        (await PendingMoveStore.GetPendingMovesAsync()).Should().NotBeEmpty();
    }
}
