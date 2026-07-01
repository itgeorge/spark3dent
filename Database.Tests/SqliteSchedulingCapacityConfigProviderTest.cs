using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Database.Entities;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using Orders;

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

    [Test]
    public async Task CreateAsync_GivenDuplicateForLabLocalToday_UpdatesExistingRow()
    {
        var originalCreatedAt = DateTimeOffset.Parse("2026-06-29T10:00:00Z");
        var now = DateTimeOffset.Parse("2026-06-30T21:30:00Z"); // 2026-07-01 in Bulgaria/Sofia.
        await SeedCapacityConfigAsync(new DateOnly(2026, 7, 1), 10m, 50m, originalCreatedAt);
        var provider = new SqliteSchedulingCapacityConfigProvider(_contextFactory);

        var saved = await provider.CreateAsync(new SchedulingCapacityConfigCreate(new DateOnly(2026, 7, 1), 12m, 60m), now);

        await using var ctx = _contextFactory();
        var rows = await ctx.SchedulingCapacityConfigs.AsNoTracking().Where(c => c.ActiveFromDate == new DateOnly(2026, 7, 1)).ToArrayAsync();
        Assert.Multiple(() =>
        {
            Assert.That(rows, Has.Length.EqualTo(1));
            Assert.That(saved.Id, Is.EqualTo(rows[0].Id));
            Assert.That(rows[0].DailyCapacityUnits, Is.EqualTo(12m));
            Assert.That(rows[0].WeeklyCapacityUnits, Is.EqualTo(60m));
            Assert.That(rows[0].CreatedAt, Is.EqualTo(originalCreatedAt));
            Assert.That(rows[0].UpdatedAt, Is.EqualTo(now));
        });
    }

    [Test]
    public async Task CreateAsync_GivenDuplicateForFutureDate_UpdatesExistingRow()
    {
        var originalCreatedAt = DateTimeOffset.Parse("2026-06-29T10:00:00Z");
        var now = DateTimeOffset.Parse("2026-06-30T21:30:00Z");
        await SeedCapacityConfigAsync(new DateOnly(2026, 7, 2), 10m, 50m, originalCreatedAt);
        var provider = new SqliteSchedulingCapacityConfigProvider(_contextFactory);

        var saved = await provider.CreateAsync(new SchedulingCapacityConfigCreate(new DateOnly(2026, 7, 2), 14m, 70m), now);

        await using var ctx = _contextFactory();
        var rows = await ctx.SchedulingCapacityConfigs.AsNoTracking().Where(c => c.ActiveFromDate == new DateOnly(2026, 7, 2)).ToArrayAsync();
        Assert.Multiple(() =>
        {
            Assert.That(rows, Has.Length.EqualTo(1));
            Assert.That(saved.Id, Is.EqualTo(rows[0].Id));
            Assert.That(rows[0].DailyCapacityUnits, Is.EqualTo(14m));
            Assert.That(rows[0].WeeklyCapacityUnits, Is.EqualTo(70m));
            Assert.That(rows[0].CreatedAt, Is.EqualTo(originalCreatedAt));
            Assert.That(rows[0].UpdatedAt, Is.EqualTo(now));
        });
    }

    [Test]
    public async Task CreateAsync_GivenDuplicateForPastDate_RejectsAndLeavesExistingRowUnchanged()
    {
        var originalCreatedAt = DateTimeOffset.Parse("2026-06-29T10:00:00Z");
        var now = DateTimeOffset.Parse("2026-06-30T21:30:00Z");
        await SeedCapacityConfigAsync(new DateOnly(2026, 6, 30), 10m, 50m, originalCreatedAt);
        var provider = new SqliteSchedulingCapacityConfigProvider(_contextFactory);

        Assert.ThrowsAsync<DuplicateSchedulingCapacityConfigDateException>(async () =>
            await provider.CreateAsync(new SchedulingCapacityConfigCreate(new DateOnly(2026, 6, 30), 14m, 70m), now));

        await using var ctx = _contextFactory();
        var row = await ctx.SchedulingCapacityConfigs.AsNoTracking().SingleAsync(c => c.ActiveFromDate == new DateOnly(2026, 6, 30));
        Assert.Multiple(() =>
        {
            Assert.That(row.DailyCapacityUnits, Is.EqualTo(10m));
            Assert.That(row.WeeklyCapacityUnits, Is.EqualTo(50m));
            Assert.That(row.CreatedAt, Is.EqualTo(originalCreatedAt));
            Assert.That(row.UpdatedAt, Is.EqualTo(originalCreatedAt));
        });
    }

    [Test]
    public async Task CreateAsync_GivenPastDateWithoutExistingRow_CreatesRow()
    {
        var now = DateTimeOffset.Parse("2026-06-30T21:30:00Z");
        var provider = new SqliteSchedulingCapacityConfigProvider(_contextFactory);

        var saved = await provider.CreateAsync(new SchedulingCapacityConfigCreate(new DateOnly(2026, 6, 30), 14m, 70m), now);

        await using var ctx = _contextFactory();
        var row = await ctx.SchedulingCapacityConfigs.AsNoTracking().SingleAsync(c => c.ActiveFromDate == new DateOnly(2026, 6, 30));
        Assert.Multiple(() =>
        {
            Assert.That(saved.Id, Is.EqualTo(row.Id));
            Assert.That(row.DailyCapacityUnits, Is.EqualTo(14m));
            Assert.That(row.WeeklyCapacityUnits, Is.EqualTo(70m));
            Assert.That(row.CreatedAt, Is.EqualTo(now));
            Assert.That(row.UpdatedAt, Is.EqualTo(now));
        });
    }

    private async Task SeedCapacityConfigAsync(DateOnly activeFromDate, decimal dailyCapacityUnits, decimal weeklyCapacityUnits, DateTimeOffset timestamp)
    {
        await using var ctx = _contextFactory();
        ctx.SchedulingCapacityConfigs.Add(new SchedulingCapacityConfigEntity
        {
            ActiveFromDate = activeFromDate,
            DailyCapacityUnits = dailyCapacityUnits,
            WeeklyCapacityUnits = weeklyCapacityUnits,
            CreatedAt = timestamp,
            UpdatedAt = timestamp
        });
        await ctx.SaveChangesAsync();
    }

}
