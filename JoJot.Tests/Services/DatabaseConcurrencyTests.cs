using JoJot.Services;
using JoJot.Tests.Helpers;

namespace JoJot.Tests.Services;

/// <summary>
/// Verifies that read paths do not queue behind the database write lock (WAL readers
/// are concurrent with a writer, so reads must not take the write semaphore).
/// </summary>
[Collection("Database")]
public class DatabaseConcurrencyTests : IAsyncLifetime
{
    private TestDatabase _db = null!;

    public async Task InitializeAsync() => _db = await TestDatabase.CreateAsync();
    public async Task DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task GetPreference_CompletesWhileWriteLockIsHeld()
    {
        await PreferenceStore.SetPreferenceAsync("held_key", "held_value");

        await DatabaseCore.AcquireWriteLockAsync();
        try
        {
            var read = PreferenceStore.GetPreferenceAsync("held_key");
            var winner = await Task.WhenAny(read, Task.Delay(2000));

            winner.Should().Be(read, "reads must not wait for the write lock");
            (await read).Should().Be("held_value");
        }
        finally
        {
            DatabaseCore.ReleaseWriteLock();
        }
    }

    [Fact]
    public async Task GetNotesForDesktop_CompletesWhileWriteLockIsHeld()
    {
        await NoteStore.InsertNoteAsync("desktop-a", null, "hello", false, 0);

        await DatabaseCore.AcquireWriteLockAsync();
        try
        {
            var read = NoteStore.GetNotesForDesktopAsync("desktop-a");
            var winner = await Task.WhenAny(read, Task.Delay(2000));

            winner.Should().Be(read, "reads must not wait for the write lock");
            (await read).Should().HaveCount(1);
        }
        finally
        {
            DatabaseCore.ReleaseWriteLock();
        }
    }
}
