using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace JoJot.Services;

/// <summary>
/// EF Core connection interceptor that ensures <c>PRAGMA foreign_keys=ON</c> is set
/// on every new SQLite connection.
/// </summary>
public sealed class SqlitePragmaInterceptor : DbConnectionInterceptor
{
    /// <summary>
    /// Synchronously sets <c>PRAGMA foreign_keys=ON</c> after a connection is opened.
    /// </summary>
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys=ON;";
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Asynchronously sets <c>PRAGMA foreign_keys=ON</c> after a connection is opened.
    /// </summary>
    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys=ON;";
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
