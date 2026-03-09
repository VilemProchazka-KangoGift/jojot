using JoJot.Services;
using JoJot.Tests.Helpers;

namespace JoJot.Tests.Services;

/// <summary>
/// DatabaseCore tests for error paths and edge cases using in-memory DB.
/// </summary>
[Collection("Database")]
public class DatabaseCoreCoverage2Tests : IAsyncLifetime
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
    public async Task ExecuteNonQuery_InvalidSql_Throws()
    {
        var act = () => DatabaseCore.ExecuteNonQueryAsync("INVALID SQL STATEMENT XYZ");
        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task ExecuteScalar_InvalidSql_Throws()
    {
        var act = () => DatabaseCore.ExecuteScalarAsync<long>("SELECT * FROM nonexistent_table_xyz");
        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task ColumnExistsAsync_MigrationIdempotent()
    {
        await DatabaseCore.RunPendingMigrationsAsync();
        await DatabaseCore.RunPendingMigrationsAsync();

        await DatabaseCore.ExecuteNonQueryAsync(
            "INSERT INTO app_state (desktop_guid, active_tab_id, window_state) VALUES ('test', 1, 'normal');");

        var ws = await DatabaseCore.ExecuteScalarAsync<string>(
            "SELECT window_state FROM app_state WHERE desktop_guid='test';");
        ws.Should().Be("normal");
    }

    [Fact]
    public async Task VerifyIntegrity_WithAllTables_ReturnsTrue()
    {
        var result = await DatabaseCore.VerifyIntegrityAsync(CancellationToken.None);
        result.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteScalar_ReturnsDouble()
    {
        var result = await DatabaseCore.ExecuteScalarAsync<double>("SELECT 3.14;");
        result.Should().BeApproximately(3.14, 0.001);
    }

    [Fact]
    public async Task CloseAsync_ThenReopen_Works()
    {
        await DatabaseCore.CloseAsync();
        _db = await TestDatabase.CreateAsync();

        var count = await DatabaseCore.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM notes;");
        count.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task CloseAsync_CalledTwice_DoesNotThrow()
    {
        await DatabaseCore.CloseAsync();
        await DatabaseCore.CloseAsync();
        _db = await TestDatabase.CreateAsync();
    }
}
