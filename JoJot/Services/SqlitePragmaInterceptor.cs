using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace JoJot.Services
{
    /// <summary>
    /// Ensures PRAGMA foreign_keys=ON is set on every new SQLite connection opened by EF Core.
    /// </summary>
    public class SqlitePragmaInterceptor : DbConnectionInterceptor
    {
        public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "PRAGMA foreign_keys=ON;";
            cmd.ExecuteNonQuery();
        }

        public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "PRAGMA foreign_keys=ON;";
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
