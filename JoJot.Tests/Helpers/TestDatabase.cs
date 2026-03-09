using JoJot.Services;
using Microsoft.Data.Sqlite;

namespace JoJot.Tests.Helpers;

/// <summary>
/// Sets up an in-memory SQLite database via <see cref="DatabaseService"/> for integration tests.
/// The connection stays open for the lifetime of this object; disposing closes it.
/// </summary>
public sealed class TestDatabase : IAsyncDisposable
{
    private TestDatabase() { }

    /// <summary>
    /// Opens DatabaseService against a shared in-memory SQLite database and ensures schema.
    /// </summary>
    public static async Task<TestDatabase> CreateAsync()
    {
        LogService.InitializeNoop();

        // Use a unique shared in-memory database per test to avoid cross-test pollution.
        // "cache=shared" keeps the DB alive as long as at least one connection is open.
        var dbName = $"testdb_{Guid.NewGuid():N}";
        var connStr = $"Data Source={dbName};Mode=Memory;Cache=Shared";

        // Open a keepalive connection so the in-memory DB doesn't vanish between operations
        var keepalive = new SqliteConnection(connStr);
        await keepalive.OpenAsync();

        await DatabaseCore.OpenWithConnectionStringAsync(connStr);
        await DatabaseCore.EnsureSchemaAsync();

        return new TestDatabase { _keepalive = keepalive };
    }

    private SqliteConnection? _keepalive;

    public async ValueTask DisposeAsync()
    {
        await DatabaseCore.CloseAsync();
        if (_keepalive is not null)
        {
            await _keepalive.CloseAsync();
            _keepalive.Dispose();
            _keepalive = null;
        }
    }
}
