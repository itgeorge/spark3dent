using System;
using System.IO;
using System.Linq;
using System.Threading;
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
    public async Task CreateOrderAsync_GivenConcurrentDuplicateOrderCodes_CreatesOneAndFailsOthersWithDuplicateOrderCodeException()
    {
        const int racingOrderCount = 20;
        const string sharedOrderCode = "RCE-234";
        var repo = new SqliteOrderRepo(_contextFactory);

        var outcomes = await RaceCreateSameOrderCodeAsync(repo, racingOrderCount, sharedOrderCode);

        Assert.That(outcomes.Count(o => o.CreatedOrder != null), Is.EqualTo(1));
        var failures = outcomes.Where(o => o.Exception != null).ToList();
        Assert.That(failures, Has.Count.EqualTo(racingOrderCount - 1));
        Assert.That(failures.Select(f => f.Exception), Is.All.TypeOf<DuplicateOrderCodeException>());
        Assert.That(failures.Select(f => ((DuplicateOrderCodeException)f.Exception!).OrderCode), Is.All.EqualTo(sharedOrderCode));
    }

    [Test]
    public async Task CreateOrderAsync_GivenExistingOrdersAndConcurrentDuplicateOrderCodes_PersistsOnlyOneRacingOrder()
    {
        const int racingOrderCount = 20;
        const string sharedOrderCode = "RCE-345";
        var repo = new SqliteOrderRepo(_contextFactory);
        await repo.CreateOrderAsync(BuildOrder("EXA-234", "existing-a", DateTimeOffset.Parse("2026-05-31T09:00:00Z")));
        await repo.CreateOrderAsync(BuildOrder("EXB-234", "existing-b", DateTimeOffset.Parse("2026-05-31T09:01:00Z")));

        var outcomes = await RaceCreateSameOrderCodeAsync(repo, racingOrderCount, sharedOrderCode);
        var orders = await repo.ListOrdersAsync(limit: 100);

        Assert.That(outcomes.Count(o => o.CreatedOrder != null), Is.EqualTo(1));
        Assert.That(orders, Has.Count.EqualTo(3));
        Assert.That(orders.Select(o => o.OrderCode), Does.Contain("EXA-234"));
        Assert.That(orders.Select(o => o.OrderCode), Does.Contain("EXB-234"));
        Assert.That(orders.Select(o => o.OrderCode), Does.Contain(sharedOrderCode));
    }

    [Test]
    public async Task CreateOrderAsync_PersistsShade_WhenReadBackByCodeAndList()
    {
        var repo = new SqliteOrderRepo(_contextFactory);
        await repo.CreateOrderAsync(BuildOrder("SHA-234", "shade case", DateTimeOffset.Parse("2026-05-31T10:00:00Z"), shade: Shade.A3_5));

        var byCode = await repo.GetOrderByCodeAsync("SHA-234");
        var fromList = (await repo.ListOrdersAsync()).Single(o => o.OrderCode == "SHA-234");

        Assert.That(byCode, Is.Not.Null);
        Assert.That(byCode!.Shade, Is.EqualTo(Shade.A3_5));
        Assert.That(fromList.Shade, Is.EqualTo(Shade.A3_5));
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

    [Test]
    public async Task ListOrdersForClinicAsync_ReturnsOnlyClinicOrdersNewestFirst()
    {
        var repo = new SqliteOrderRepo(_contextFactory);
        await repo.CreateOrderAsync(BuildOrder("AAA-234", "demo old", DateTimeOffset.Parse("2026-05-31T10:00:00Z"), clinicCode: "DEMO"));
        await repo.CreateOrderAsync(BuildOrder("BBB-234", "other", DateTimeOffset.Parse("2026-05-31T11:00:00Z"), clinicCode: "OTHER"));
        await repo.CreateOrderAsync(BuildOrder("CCC-234", "demo new", DateTimeOffset.Parse("2026-05-31T12:00:00Z"), clinicCode: "DEMO"));

        var orders = await repo.ListOrdersForClinicAsync("DEMO");

        Assert.That(orders.Select(o => o.OrderCode), Is.EqualTo(new[] { "CCC-234", "AAA-234" }));
    }

    private static async Task<CreateOutcome[]> RaceCreateSameOrderCodeAsync(SqliteOrderRepo repo, int racingOrderCount, string sharedOrderCode)
    {
        using var startBarrier = new Barrier(racingOrderCount);
        var tasks = Enumerable.Range(1, racingOrderCount)
            .Select(i => Task.Factory.StartNew(
                async () =>
                {
                    if (!startBarrier.SignalAndWait(TimeSpan.FromSeconds(10)))
                        throw new InvalidOperationException("Timed out waiting for racing order repository requests.");

                    try
                    {
                        var order = await repo.CreateOrderAsync(BuildOrder(
                            sharedOrderCode,
                            $"race-{i}",
                            DateTimeOffset.Parse("2026-05-31T10:00:00Z").AddSeconds(i)));
                        return CreateOutcome.Success(order);
                    }
                    catch (Exception ex)
                    {
                        return CreateOutcome.Failure(ex);
                    }
                },
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default).Unwrap())
            .ToArray();

        return await Task.WhenAll(tasks);
    }

    private sealed record CreateOutcome(OrderRecord? CreatedOrder, Exception? Exception)
    {
        public static CreateOutcome Success(OrderRecord order) => new(order, null);
        public static CreateOutcome Failure(Exception exception) => new(null, exception);
    }

    private static OrderRecord BuildOrder(string code, string caseName, DateTimeOffset createdAt, Shade shade = Shade.Unspecified, string clinicCode = "DEMO") => new(
        0,
        code,
        clinicCode,
        clinicCode == "DEMO" ? "Demo Clinic" : "Other Clinic",
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
        shade,
        null,
        createdAt,
        createdAt,
        "127.0.0.1",
        "test");
}
