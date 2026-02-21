using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NUnit.Framework;

namespace Database.Tests;

/// <summary>
/// Fails when the model has changed but no migration has been added.
/// Run: dotnet ef migrations add &lt;MigrationName&gt; --project Database --startup-project Cli
/// </summary>
[TestFixture]
public class PendingModelChangesTest
{
    [Test]
    public void NoPendingModelChanges()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        using var ctx = new AppDbContext(options);
        var migrator = ctx.GetService<IMigrator>();
        var hasPending = migrator.HasPendingModelChanges();

        Assert.That(hasPending, Is.False,
            "Model has changed since last migration. Run: dotnet ef migrations add <MigrationName> --project Database --startup-project Cli");
    }
}
