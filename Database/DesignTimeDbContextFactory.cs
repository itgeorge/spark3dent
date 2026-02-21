using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Database;

/// <summary>
/// Used by EF Core design-time tools (e.g. dotnet ef migrations add).
/// The connection string is only used to discover the provider; migrations add does not connect.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=spark3dent.db")
            .Options;

        return new AppDbContext(options);
    }
}
