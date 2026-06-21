using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Database.Entities;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Database.Tests;

[TestFixture]
public class SqliteSchedulingCapacityConfigProviderTest
{
    private string _dbPath = null!;
    private Func<AppDbContext> _contextFactory = null!;

    [SetUp]
    public async Task SetUp()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "Spark3Dent", "SqliteSchedulingCapacityConfigProviderTest", $"{Guid.NewGuid():N}.db");
        Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;
        await using (var ctx = new AppDbContext(options))
            await ctx.Database.MigrateAsync();
        _contextFactory = () => new AppDbContext(options);
    }

    [TearDown]
    public void TearDown()
    {
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
    }

    [Test]
    public async Task Migration_CreatesAndSeedsSchedulingCapacityConfigs()
    {
        await using var ctx = _contextFactory();

        var rows = await ctx.SchedulingCapacityConfigs.AsNoTracking().OrderBy(c => c.ActiveFromDate).ToListAsync();

        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(rows[0].ActiveFromDate, Is.EqualTo(new DateOnly(2026, 1, 1)));
            Assert.That(rows[0].DailyCapacityUnits, Is.EqualTo(100.0m));
            Assert.That(rows[0].WeeklyCapacityUnits, Is.EqualTo(500.0m));
        });
    }

    [Test]
    public async Task GetForDateAsync_UsesLatestActiveFromDateOnOrBeforeCandidateDate()
    {
        await using (var ctx = _contextFactory())
        {
            ctx.SchedulingCapacityConfigs.Add(new SchedulingCapacityConfigEntity
            {
                ActiveFromDate = new DateOnly(2026, 6, 10),
                DailyCapacityUnits = 2.5m,
                WeeklyCapacityUnits = 12.5m,
                CreatedAt = DateTimeOffset.Parse("2026-06-21T00:00:00Z"),
                UpdatedAt = DateTimeOffset.Parse("2026-06-21T00:00:00Z")
            });
            await ctx.SaveChangesAsync();
        }
        var provider = new SqliteSchedulingCapacityConfigProvider(_contextFactory);

        var before = await provider.GetForDateAsync(new DateOnly(2026, 6, 9));
        var after = await provider.GetForDateAsync(new DateOnly(2026, 6, 10));

        Assert.That(before.DailyCapacityUnits, Is.EqualTo(100.0m));
        Assert.That(after.DailyCapacityUnits, Is.EqualTo(2.5m));
        Assert.That(after.WeeklyCapacityUnits, Is.EqualTo(12.5m));
    }

    [Test]
    public void GetForDateAsync_GivenNoApplicableRow_FailsClearly()
    {
        var provider = new SqliteSchedulingCapacityConfigProvider(_contextFactory);

        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await provider.GetForDateAsync(new DateOnly(2025, 12, 31)));

        Assert.That(ex!.Message, Does.Contain("missing").IgnoreCase);
        Assert.That(ex.Message, Does.Contain("2025-12-31"));
    }
}
