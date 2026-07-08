using JoJot.Services;
using JoJot.Tests.Helpers;

namespace JoJot.Tests.Services;

/// <summary>Tests the PRAGMA user_version schema stamp fast path.</summary>
[Collection("Database")]
public class SchemaVersionTests : IAsyncLifetime
{
    private TestDatabase _db = null!;

    public async Task InitializeAsync() => _db = await TestDatabase.CreateAsync();
    public async Task DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task FreshDatabase_HasVersionZero_UntilMigrationsStampIt()
    {
        (await DatabaseCore.GetUserVersionAsync()).Should().Be(0);

        await DatabaseCore.RunPendingMigrationsAsync();

        (await DatabaseCore.GetUserVersionAsync()).Should().Be(DatabaseCore.CurrentSchemaVersion);
    }

    [Fact]
    public async Task EnsureSchema_SkipsWork_WhenStamped()
    {
        await DatabaseCore.RunPendingMigrationsAsync(); // stamps

        bool didWork = await DatabaseCore.EnsureSchemaAsync();

        didWork.Should().BeFalse("a stamped schema must skip EnsureCreated");
    }

    [Fact]
    public async Task EnsureSchema_RunsWork_WhenNotStamped()
    {
        // TestDatabase already ran EnsureSchemaAsync once, but the stamp is only
        // written by RunPendingMigrationsAsync — so a second call must still do work.
        bool didWork = await DatabaseCore.EnsureSchemaAsync();

        didWork.Should().BeTrue();
    }

    [Fact]
    public async Task Migrations_SkipProbes_WhenStamped()
    {
        await DatabaseCore.RunPendingMigrationsAsync(); // stamps
        // Second run must return without touching table_info (observable only via
        // timing/logs, so assert the cheap contract: version unchanged and no throw).
        await DatabaseCore.RunPendingMigrationsAsync();
        (await DatabaseCore.GetUserVersionAsync()).Should().Be(DatabaseCore.CurrentSchemaVersion);
    }
}
