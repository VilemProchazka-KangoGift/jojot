using JoJot.Services;
using JoJot.Tests.Helpers;

namespace JoJot.Tests.Services;

/// <summary>
/// Integration tests for DatabaseCore infrastructure operations.
/// </summary>
[Collection("Database")]
public class DatabaseCoreTests : IAsyncLifetime
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

    // ─── Schema ───────────────────────────────────────────────────────

    [Fact]
    public async Task EnsureSchema_CreatesAllTables()
    {
        // EnsureSchemaAsync is already called by TestDatabase.CreateAsync.
        // Verify all four tables exist.
        var notesCount = await DatabaseCore.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='notes';");
        notesCount.Should().Be(1);

        var appStateCount = await DatabaseCore.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='app_state';");
        appStateCount.Should().Be(1);

        var prefsCount = await DatabaseCore.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='preferences';");
        prefsCount.Should().Be(1);

        var movesCount = await DatabaseCore.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='pending_moves';");
        movesCount.Should().Be(1);
    }

    // ─── VerifyIntegrity ──────────────────────────────────────────────

    [Fact]
    public async Task VerifyIntegrity_ReturnsTrue_WhenHealthy()
    {
        var result = await DatabaseCore.VerifyIntegrityAsync();
        result.Should().BeTrue();
    }

    // ─── RunPendingMigrations ─────────────────────────────────────────

    [Fact]
    public async Task RunPendingMigrations_IsIdempotent()
    {
        // Should not throw even if called multiple times
        await DatabaseCore.RunPendingMigrationsAsync();
        await DatabaseCore.RunPendingMigrationsAsync();
    }

    // ─── ExecuteNonQuery / ExecuteScalar ──────────────────────────────

    [Fact]
    public async Task ExecuteScalar_ReturnsCorrectValue()
    {
        var result = await DatabaseCore.ExecuteScalarAsync<long>("SELECT 42;");
        result.Should().Be(42);
    }

    [Fact]
    public async Task ExecuteNonQuery_InsertsRow()
    {
        await DatabaseCore.ExecuteNonQueryAsync(
            "INSERT INTO preferences (key, value) VALUES ('exec_test', 'exec_value');");

        var value = await PreferenceStore.GetPreferenceAsync("exec_test");
        value.Should().Be("exec_value");
    }

    // ─── Write Lock ───────────────────────────────────────────────────

    [Fact]
    public async Task WriteLock_AcquireAndRelease_DoesNotThrow()
    {
        await DatabaseCore.AcquireWriteLockAsync();
        DatabaseCore.ReleaseWriteLock();
    }

    [Fact]
    public async Task WriteLock_SecondAcquire_AfterRelease_Succeeds()
    {
        await DatabaseCore.AcquireWriteLockAsync();
        DatabaseCore.ReleaseWriteLock();

        await DatabaseCore.AcquireWriteLockAsync();
        DatabaseCore.ReleaseWriteLock();
    }

    // ─── Clock ────────────────────────────────────────────────────────

    [Fact]
    public void SetClock_ChangesClock()
    {
        var testClock = new TestClock();
        DatabaseCore.SetClock(testClock);

        DatabaseCore.Clock.Should().BeSameAs(testClock);

        // Restore
        DatabaseCore.SetClock(SystemClock.Instance);
    }

    [Fact]
    public void Clock_DefaultIsSystemClock()
    {
        DatabaseCore.SetClock(SystemClock.Instance);
        DatabaseCore.Clock.Should().BeOfType<SystemClock>();
    }

    // ─── ConnectionString ─────────────────────────────────────────────

    [Fact]
    public void ConnectionString_IsNotEmpty()
    {
        DatabaseCore.ConnectionString.Should().NotBeNullOrWhiteSpace();
    }

    // ─── CreateTestContext ────────────────────────────────────────────

    [Fact]
    public async Task CreateTestContext_ReturnsWorkingContext()
    {
        await using var context = DatabaseCore.CreateTestContext();
        context.Should().NotBeNull();

        // Should be able to query without error
        var count = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
            .CountAsync(context.Notes);
        count.Should().BeGreaterThanOrEqualTo(0);
    }
}
