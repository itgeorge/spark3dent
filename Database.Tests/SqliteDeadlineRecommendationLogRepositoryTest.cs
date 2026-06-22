using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using Orders;

namespace Database.Tests;

[TestFixture]
public class SqliteDeadlineRecommendationLogRepositoryTest
{
    private string _dbPath = null!;
    private Func<AppDbContext> _contextFactory = null!;

    [SetUp]
    public async Task SetUp()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "Spark3Dent", "SqliteDeadlineRecommendationLogRepositoryTest", $"{Guid.NewGuid():N}.db");
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
    public async Task AddAndListForOrderAsync_RoundTripsJsonAndOrdersNewestFirst()
    {
        var repo = new SqliteDeadlineRecommendationLogRepository(_contextFactory);
        var older = BuildLog(42, "ORD-1", DateTimeOffset.Parse("2026-06-22T10:00:00Z"), "[{\"candidateDate\":\"2026-06-05\"}]", "{\"cutoffTimeUsed\":\"11:00\"}");
        var newer = BuildLog(42, "ORD-1", DateTimeOffset.Parse("2026-06-22T11:00:00Z"), "[{\"accepted\":true}]", "{\"materialConfig\":{}}");
        _ = await repo.AddAsync(older);
        _ = await repo.AddAsync(BuildLog(99, "OTHER", DateTimeOffset.Parse("2026-06-22T12:00:00Z"), "[]", "{}"));
        _ = await repo.AddAsync(newer);

        var rows = await repo.ListForOrderAsync(42);

        Assert.That(rows, Has.Count.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(rows[0].CreatedAtUtc, Is.EqualTo(newer.CreatedAtUtc));
            Assert.That(rows[0].CandidateChecksJson, Is.EqualTo(newer.CandidateChecksJson));
            Assert.That(rows[0].ConfigSnapshotJson, Is.EqualTo(newer.ConfigSnapshotJson));
            Assert.That(rows[1].CandidateChecksJson, Is.EqualTo(older.CandidateChecksJson));
            Assert.That(rows[0].Material, Is.EqualTo(Material.Pmma));
            Assert.That(rows[0].CalculatedOrderCapacityUnits, Is.EqualTo(1.5m));
        });
    }

    private static DeadlineRecommendationLog BuildLog(long orderId, string orderCode, DateTimeOffset createdAt, string candidateJson, string configJson) => new(
        0,
        orderId,
        orderCode,
        createdAt,
        "Lab",
        "LAB",
        "lab-1",
        "Lab Member 1",
        DateTimeOffset.Parse("2026-06-22T09:00:00Z"),
        new DateOnly(2026, 6, 22),
        new TimeOnly(11, 0),
        Material.Pmma,
        1,
        2,
        2,
        0,
        null,
        1.5m,
        1.5m,
        new DateOnly(2026, 6, 24),
        new DateOnly(2026, 6, 24),
        new DateOnly(2026, 6, 24),
        new DateOnly(2026, 6, 24),
        new DateOnly(2026, 6, 24),
        new DateOnly(2026, 8, 23),
        "Accepted",
        null,
        candidateJson,
        configJson);
}
