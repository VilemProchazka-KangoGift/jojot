using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace JoJot.Data;

/// <summary>
/// Design-time factory for the dotnet-ef CLI (`dotnet ef dbcontext optimize`).
/// Never used at runtime — runtime contexts come from DatabaseCore's pooled factory.
/// </summary>
public sealed class DesignTimeContextFactory : IDesignTimeDbContextFactory<JoJotDbContext>
{
    public JoJotDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<JoJotDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        return new JoJotDbContext(options);
    }
}
