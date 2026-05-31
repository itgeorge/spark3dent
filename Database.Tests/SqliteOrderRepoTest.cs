using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using Orders;

namespace Database.Tests;

[TestFixture]
public class SqliteOrderRepoTest
{
    private string _dbPath = null!;
    private Func<AppDbContext> _contextFactory = null!;

    [SetUp]
    public async Task SetUp()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "Spark3Dent", "SqliteOrderRepoTest", $"{Guid.NewGuid():N}.db");
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
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); }
        catch { /* best effort */ }
    }

    [Test]
    public async Task CreateOrderAsync_GivenDuplicateOrderCode_ThrowsDuplicateOrderCodeException()
    {
        var repo = new SqliteOrderRepo(_contextFactory);
        await repo.CreateOrderAsync(BuildOrder("ABC-234", "first", DateTimeOffset.Parse("2026-05-31T10:00:00Z")));

        var ex = Assert.ThrowsAsync<DuplicateOrderCodeException>(async () =>
            await repo.CreateOrderAsync(BuildOrder("ABC-234", "second", DateTimeOffset.Parse("2026-05-31T10:01:00Z"))));

        Assert.That(ex!.OrderCode, Is.EqualTo("ABC-234"));
    }

    [Test]
    public async Task ListOrdersAsync_OrdersByCreatedAtDescendingInDatabase()
    {
        var repo = new SqliteOrderRepo(_contextFactory);
        await repo.CreateOrderAsync(BuildOrder("AAA-234", "old", DateTimeOffset.Parse("2026-05-31T10:00:00Z")));
        await repo.CreateOrderAsync(BuildOrder("BBB-234", "new", DateTimeOffset.Parse("2026-05-31T11:00:00Z")));

        var orders = await repo.ListOrdersAsync();

        Assert.That(orders.Select(o => o.OrderCode), Is.EqualTo(new[] { "BBB-234", "AAA-234" }));
    }

    private static OrderRecord BuildOrder(string code, string caseName, DateTimeOffset createdAt) => new(
        0,
        code,
        "DEMO",
        "Demo Clinic",
        "cred-1",
        "Credential 1",
        "fingerprint",
        caseName,
        new DateOnly(2026, 5, 31),
        ProductCategory.Permanent,
        WorkType.Crown,
        Material.FullContourZirconia,
        ConstructionType.Crown,
        11,
        11,
        "",
        new DateOnly(2026, 6, 5),
        OrderStatus.Created,
        null,
        createdAt,
        createdAt,
        "127.0.0.1",
        "test");
}
