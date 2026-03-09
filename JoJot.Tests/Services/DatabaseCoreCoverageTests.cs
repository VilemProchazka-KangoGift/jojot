using JoJot.Services;
using JoJot.Tests.Helpers;

namespace JoJot.Tests.Services;

/// <summary>
/// Additional DatabaseCore tests targeting uncovered branches:
/// CloseAsync, HandleCorruptionAsync, ColumnExistsAsync, error paths.
/// </summary>
[Collection("Database")]
public class DatabaseCoreCoverageTests : IAsyncLifetime
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
    public async Task CloseAsync_ClosesConnection()
    {
        // CloseAsync then reopen should work
        await DatabaseCore.CloseAsync();

        // Reopen for subsequent tests
        _db = await TestDatabase.CreateAsync();
    }

    [Fact]
    public async Task CloseAsync_NoOp_WhenAlreadyClosed()
    {
        await DatabaseCore.CloseAsync();
        // Second close should not throw
        await DatabaseCore.CloseAsync();

        // Reopen for subsequent tests
        _db = await TestDatabase.CreateAsync();
    }

    [Fact]
    public async Task VerifyIntegrity_ReturnsTrueOnFreshSchema()
    {
        var result = await DatabaseCore.VerifyIntegrityAsync();
        result.Should().BeTrue();
    }

    [Fact]
    public async Task RunPendingMigrations_SecondRun_IsNoOp()
    {
        // First run adds column if missing
        await DatabaseCore.RunPendingMigrationsAsync();
        // Second run detects column exists
        await DatabaseCore.RunPendingMigrationsAsync();
        // Should not throw
    }

    [Fact]
    public async Task ExecuteScalar_ReturnsString()
    {
        var result = await DatabaseCore.ExecuteScalarAsync<string>("SELECT 'hello';");
        result.Should().Be("hello");
    }

    [Fact]
    public async Task WriteLock_CancellationToken_Supported()
    {
        using var cts = new CancellationTokenSource();
        await DatabaseCore.AcquireWriteLockAsync(cts.Token);
        DatabaseCore.ReleaseWriteLock();
    }
}
