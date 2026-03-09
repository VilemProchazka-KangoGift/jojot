using JoJot.Services;
using JoJot.Tests.Helpers;

namespace JoJot.Tests.Services;

/// <summary>
/// Tests for StartupService using in-memory SQLite.
/// Must run in the Database collection since it shares DatabaseService static state.
/// </summary>
[Collection("Database")]
public class StartupServiceTests : IAsyncLifetime
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
    public async Task CreateWelcomeTab_InsertsWhenDatabaseEmpty()
    {
        // VirtualDesktopService.CurrentDesktopGuid is "default" in test
        // because COM is not initialized
        await StartupService.CreateWelcomeTabIfFirstLaunch();

        var count = await DatabaseService.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM notes;");
        count.Should().Be(1);

        var notes = await DatabaseService.GetNotesForDesktopAsync(
            VirtualDesktopService.CurrentDesktopGuid);
        notes.Should().ContainSingle();
        notes[0].Name.Should().Be("Welcome to JoJot");
        notes[0].Content.Should().Contain("Welcome to JoJot!");
    }

    [Fact]
    public async Task CreateWelcomeTab_SkipsWhenNotesExist()
    {
        await DatabaseService.InsertNoteAsync("some-desktop", "Existing", "Content", false, 0);

        await StartupService.CreateWelcomeTabIfFirstLaunch();

        var count = await DatabaseService.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM notes;");
        count.Should().Be(1); // Only the pre-existing note, no welcome tab
    }

    [Fact]
    public async Task RunBackgroundMigrations_DoesNotThrow()
    {
        // Should complete without error on a fresh schema
        await StartupService.RunBackgroundMigrationsAsync();
    }
}
