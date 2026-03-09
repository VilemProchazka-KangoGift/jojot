using JoJot.Services;
using JoJot.Tests.Helpers;

namespace JoJot.Tests.Services;

/// <summary>
/// Additional StartupService tests targeting the RunBackgroundMigrationsAsync exception path.
/// </summary>
[Collection("Database")]
public class StartupServiceCoverageTests : IAsyncLifetime
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
    public async Task RunBackgroundMigrations_AfterMultipleRuns_IsIdempotent()
    {
        // Run multiple times to exercise the "no pending migrations" path
        await StartupService.RunBackgroundMigrationsAsync();
        await StartupService.RunBackgroundMigrationsAsync();
        await StartupService.RunBackgroundMigrationsAsync();
    }

    [Fact]
    public async Task CreateWelcomeTab_DoesNotDuplicate_OnMultipleCalls()
    {
        await StartupService.CreateWelcomeTabIfFirstLaunch();
        await StartupService.CreateWelcomeTabIfFirstLaunch();

        var count = await DatabaseCore.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM notes;");
        count.Should().Be(1);
    }
}
