using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using Orders;

namespace Database.Tests;

[TestFixture]
public class SqliteLabOffdayRepositoryTest
{
    private string _dbPath = null!;
    private Func<AppDbContext> _contextFactory = null!;
    private readonly DateTimeOffset _now = DateTimeOffset.Parse("2026-06-25T10:00:00Z");

    [SetUp]
    public async Task SetUp()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "Spark3Dent", "SqliteLabOffdayRepositoryTest", $"{Guid.NewGuid():N}.db");
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
    public async Task Migration_CreatesSchedulingLabOffdaysTable()
    {
        await using var ctx = _contextFactory();

        ctx.SchedulingLabOffdays.Add(new Database.Entities.SchedulingLabOffdayEntity
        {
            StartDate = new DateOnly(2026, 7, 1),
            EndDate = new DateOnly(2026, 7, 2),
            CreatedAt = _now,
            UpdatedAt = _now
        });
        await ctx.SaveChangesAsync();

        Assert.That(await ctx.SchedulingLabOffdays.CountAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task CreateAndListIntersectingAsync_ReturnsInclusiveRanges()
    {
        var repo = new SqliteLabOffdayRepository(_contextFactory);
        var created = await repo.CreateAsync(new LabOffdayCreate(new DateOnly(2026, 7, 10), new DateOnly(2026, 7, 12)), _now);

        var before = await repo.ListIntersectingAsync(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 9));
        var touchingStart = await repo.ListIntersectingAsync(new DateOnly(2026, 7, 12), new DateOnly(2026, 7, 20));

        Assert.Multiple(() =>
        {
            Assert.That(created.Id, Is.GreaterThan(0));
            Assert.That(before, Is.Empty);
            Assert.That(touchingStart.Select(x => x.Id), Is.EqualTo(new[] { created.Id }));
        });
    }

    [Test]
    public async Task UpdateAsync_ChangesRangeAndUpdatedAt()
    {
        var repo = new SqliteLabOffdayRepository(_contextFactory);
        var created = await repo.CreateAsync(new LabOffdayCreate(new DateOnly(2026, 7, 10), new DateOnly(2026, 7, 10)), _now);
        var later = _now.AddHours(1);

        var updated = await repo.UpdateAsync(created.Id, new LabOffdayUpdate(new DateOnly(2026, 7, 11), new DateOnly(2026, 7, 13)), later);

        Assert.Multiple(() =>
        {
            Assert.That(updated.StartDate, Is.EqualTo(new DateOnly(2026, 7, 11)));
            Assert.That(updated.EndDate, Is.EqualTo(new DateOnly(2026, 7, 13)));
            Assert.That(updated.CreatedAt, Is.EqualTo(_now));
            Assert.That(updated.UpdatedAt, Is.EqualTo(later));
        });
    }

    [Test]
    public async Task DeleteAsync_RemovesRow()
    {
        var repo = new SqliteLabOffdayRepository(_contextFactory);
        var created = await repo.CreateAsync(new LabOffdayCreate(new DateOnly(2026, 7, 10), new DateOnly(2026, 7, 10)), _now);

        await repo.DeleteAsync(created.Id);

        Assert.That(await repo.ListAllAsync(), Is.Empty);
    }

    [Test]
    public void CreateAsync_RejectsInvalidRange()
    {
        var repo = new SqliteLabOffdayRepository(_contextFactory);

        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await repo.CreateAsync(new LabOffdayCreate(new DateOnly(2026, 7, 12), new DateOnly(2026, 7, 10)), _now));

        Assert.That(ex!.Message, Does.Contain("end date"));
    }

    [Test]
    public async Task CreateAndUpdateAsync_RejectOverlappingRanges()
    {
        var repo = new SqliteLabOffdayRepository(_contextFactory);
        var first = await repo.CreateAsync(new LabOffdayCreate(new DateOnly(2026, 7, 10), new DateOnly(2026, 7, 12)), _now);
        var second = await repo.CreateAsync(new LabOffdayCreate(new DateOnly(2026, 7, 20), new DateOnly(2026, 7, 21)), _now);

        Assert.ThrowsAsync<LabOffdayOverlapException>(async () =>
            await repo.CreateAsync(new LabOffdayCreate(new DateOnly(2026, 7, 12), new DateOnly(2026, 7, 15)), _now));
        Assert.ThrowsAsync<LabOffdayOverlapException>(async () =>
            await repo.UpdateAsync(second.Id, new LabOffdayUpdate(new DateOnly(2026, 7, 9), new DateOnly(2026, 7, 10)), _now));

        var selfUpdate = await repo.UpdateAsync(first.Id, new LabOffdayUpdate(new DateOnly(2026, 7, 10), new DateOnly(2026, 7, 12)), _now);
        Assert.That(selfUpdate.Id, Is.EqualTo(first.Id));
    }
}
