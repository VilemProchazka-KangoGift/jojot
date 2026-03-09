using JoJot.Services;
using JoJot.Tests.Helpers;

namespace JoJot.Tests.Services;

/// <summary>
/// Tests for DatabaseCore integrity checking and corruption handling.
/// </summary>
[Collection("Database")]
public class DatabaseCoreIntegrityTests : IAsyncLifetime
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
    public async Task VerifyIntegrity_AllTablesPresent_ReturnsTrue()
    {
        var result = await DatabaseCore.VerifyIntegrityAsync();
        result.Should().BeTrue();
    }

    [Fact]
    public async Task VerifyIntegrity_WithCancellationToken_Works()
    {
        using var cts = new CancellationTokenSource();
        var result = await DatabaseCore.VerifyIntegrityAsync(cts.Token);
        result.Should().BeTrue();
    }

    [Fact]
    public async Task VerifyIntegrity_AfterMigration_StillHealthy()
    {
        await DatabaseCore.RunPendingMigrationsAsync();
        var result = await DatabaseCore.VerifyIntegrityAsync();
        result.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteNonQuery_InvalidSql_ThrowsAndLogsError()
    {
        var act = () => DatabaseCore.ExecuteNonQueryAsync("DROP TABLE nonexistent_xyz_123");
        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task ExecuteScalar_ReturnsLong()
    {
        var result = await DatabaseCore.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM notes");
        result.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task ExecuteScalar_ReturnsString()
    {
        var result = await DatabaseCore.ExecuteScalarAsync<string>("SELECT 'test_value'");
        result.Should().Be("test_value");
    }

    [Fact]
    public async Task WriteLock_CancelledToken_ThrowsOperationCancelled()
    {
        // Acquire the lock first
        await DatabaseCore.AcquireWriteLockAsync();
        try
        {
            // Try to acquire again with an already-cancelled token
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var act = () => DatabaseCore.AcquireWriteLockAsync(cts.Token);
            await act.Should().ThrowAsync<OperationCanceledException>();
        }
        finally
        {
            DatabaseCore.ReleaseWriteLock();
        }
    }

    [Fact]
    public async Task EnsureSchema_IsIdempotent()
    {
        await DatabaseCore.EnsureSchemaAsync();
        await DatabaseCore.EnsureSchemaAsync();

        var result = await DatabaseCore.VerifyIntegrityAsync();
        result.Should().BeTrue();
    }
}
