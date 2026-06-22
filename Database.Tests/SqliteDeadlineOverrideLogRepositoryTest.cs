using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using Orders;

namespace Database.Tests;

[TestFixture]
public class SqliteDeadlineOverrideLogRepositoryTest
{
    private string _dbPath = null!;
    private Func<AppDbContext> _contextFactory = null!;

    [SetUp]
    public async Task SetUp()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "Spark3Dent", "SqliteDeadlineOverrideLogRepositoryTest", $"{Guid.NewGuid():N}.db");
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
    public async Task AddAndListForOrderAsync_RoundTripsFieldsAndOrdersNewestFirst()
    {
        var repo = new SqliteDeadlineOverrideLogRepository(_contextFactory);
        var older = BuildLog(42, "ORD-1", DateTimeOffset.Parse("2026-06-22T10:00:00Z"), "[\"MinimumLeadTime\"]");
        var newer = BuildLog(42, "ORD-1", DateTimeOffset.Parse("2026-06-22T11:00:00Z"), "[\"DailyCapacityExceeded\"]") with
        {
            RecommendationLogId = 123,
            ExistingDailyCapacityUsed = 1m,
            DailyCapacityLimitUsed = 1m,
            DailyCapacityAfterOverride = 2m
        };
        _ = await repo.AddAsync(older);
        _ = await repo.AddAsync(BuildLog(99, "OTHER", DateTimeOffset.Parse("2026-06-22T12:00:00Z"), "[]"));
        _ = await repo.AddAsync(newer);

        var rows = await repo.ListForOrderAsync(42);

        Assert.That(rows, Has.Count.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(rows[0].CreatedAtUtc, Is.EqualTo(newer.CreatedAtUtc));
            Assert.That(rows[0].RulesBypassedJson, Is.EqualTo(newer.RulesBypassedJson));
            Assert.That(rows[0].OverrideReason, Is.EqualTo("Approved rush"));
            Assert.That(rows[0].RecommendationLogId, Is.EqualTo(123));
            Assert.That(rows[0].DailyCapacityAfterOverride, Is.EqualTo(2m));
            Assert.That(rows[1].RulesBypassedJson, Is.EqualTo(older.RulesBypassedJson));
        });
    }

    private static DeadlineOverrideLog BuildLog(long orderId, string orderCode, DateTimeOffset createdAt, string rulesJson) => new(
        0,
        orderId,
        orderCode,
        createdAt,
        "Lab",
        "LAB",
        "lab-1",
        "Lab Member 1",
        new DateOnly(2026, 6, 24),
        new DateOnly(2026, 6, 25),
        new DateOnly(2026, 6, 25),
        1.0m,
        rulesJson,
        "Approved rush",
        null,
        0m,
        0m,
        1m,
        10m,
        1m,
        1m,
        "Before minimum lead time");
}
