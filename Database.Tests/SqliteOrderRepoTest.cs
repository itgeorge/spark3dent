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
    public async Task UpdateOrderAsync_PersistsChangedFieldsAndCancelledStatus()
    {
        var repo = new SqliteOrderRepo(_contextFactory);
        var created = await repo.CreateOrderAsync(BuildOrder("UPD-234", "before", DateTimeOffset.Parse("2026-05-31T10:00:00Z")));
        var updatedAt = DateTimeOffset.Parse("2026-05-31T12:00:00Z");

        var updated = await repo.UpdateOrderAsync(created with
        {
            CaseName = "after",
            Status = OrderStatus.Cancelled,
            WorkItems = [new OrderWorkItem(ConstructionType.Crown, new ToothRange(12, 12))],
            UpdatedAt = updatedAt
        });
        var reloaded = await repo.GetOrderByCodeAsync("UPD-234");

        Assert.That(updated.CaseName, Is.EqualTo("after"));
        Assert.That(reloaded, Is.Not.Null);
        Assert.That(reloaded!.CaseName, Is.EqualTo("after"));
        Assert.That(reloaded.Status, Is.EqualTo(OrderStatus.Cancelled));
        Assert.That(reloaded.WorkItems.Single().ToothStart, Is.EqualTo(12));
        Assert.That(reloaded.UpdatedAt, Is.EqualTo(updatedAt));
    }

    [Test]
    public async Task ListOrdersAsync_OrdersByRequestedDeliveryDateDescendingInDatabase()
    {
        var repo = new SqliteOrderRepo(_contextFactory);
        await repo.CreateOrderAsync(BuildOrder(
            "AAA-234",
            "created first, due sooner",
            DateTimeOffset.Parse("2026-05-31T10:00:00Z"),
            requestedDeliveryDate: new DateOnly(2026, 6, 5)));
        await repo.CreateOrderAsync(BuildOrder(
            "BBB-234",
            "created later, due later",
            DateTimeOffset.Parse("2026-05-31T11:00:00Z"),
            requestedDeliveryDate: new DateOnly(2026, 6, 10)));

        var orders = await repo.ListOrdersAsync();

        Assert.That(orders.Select(o => o.OrderCode), Is.EqualTo(new[] { "BBB-234", "AAA-234" }));
    }

    [Test]
    public async Task ListOrdersAsync_WhenDeliveryDatesTie_OrdersNewestFirstWithinDate()
    {
        var repo = new SqliteOrderRepo(_contextFactory);
        await repo.CreateOrderAsync(BuildOrder(
            "AAA-234",
            "created first",
            DateTimeOffset.Parse("2026-05-31T10:00:00Z"),
            requestedDeliveryDate: new DateOnly(2026, 6, 5)));
        await repo.CreateOrderAsync(BuildOrder(
            "BBB-234",
            "created later",
            DateTimeOffset.Parse("2026-05-31T11:00:00Z"),
            requestedDeliveryDate: new DateOnly(2026, 6, 5)));

        var orders = await repo.ListOrdersAsync();

        Assert.That(orders.Select(o => o.OrderCode), Is.EqualTo(new[] { "BBB-234", "AAA-234" }));
    }

    [Test]
    public async Task ListActiveOrdersForCalendarAsync_FiltersClinicDateRangeAndCancelledOrders()
    {
        var repo = new SqliteOrderRepo(_contextFactory);
        await repo.CreateOrderAsync(BuildOrder(
            "AAA-234",
            "demo on start",
            DateTimeOffset.Parse("2026-05-31T10:00:00Z"),
            clinicCode: "DEMO",
            requestedDeliveryDate: new DateOnly(2026, 6, 5)));
        await repo.CreateOrderAsync(BuildOrder(
            "BBB-234",
            "demo outside",
            DateTimeOffset.Parse("2026-05-31T11:00:00Z"),
            clinicCode: "DEMO",
            requestedDeliveryDate: new DateOnly(2026, 6, 4)));
        var cancelled = await repo.CreateOrderAsync(BuildOrder(
            "CCC-234",
            "demo cancelled",
            DateTimeOffset.Parse("2026-05-31T12:00:00Z"),
            clinicCode: "DEMO",
            requestedDeliveryDate: new DateOnly(2026, 6, 10)));
        await repo.UpdateOrderAsync(cancelled with { Status = OrderStatus.Cancelled });
        await repo.CreateOrderAsync(BuildOrder(
            "DDD-234",
            "other on end",
            DateTimeOffset.Parse("2026-05-31T13:00:00Z"),
            clinicCode: "OTHER",
            requestedDeliveryDate: new DateOnly(2026, 6, 10)));

        var demoOrders = await repo.ListActiveOrdersForCalendarAsync("DEMO", new DateOnly(2026, 6, 5), new DateOnly(2026, 6, 10));
        var allOrders = await repo.ListActiveOrdersForCalendarAsync(null, new DateOnly(2026, 6, 5), new DateOnly(2026, 6, 10));

        Assert.That(demoOrders.Select(o => o.OrderCode), Is.EqualTo(new[] { "AAA-234" }));
        Assert.That(allOrders.Select(o => o.OrderCode), Is.EqualTo(new[] { "AAA-234", "DDD-234" }));
    }

    [Test]
    public async Task ListOrdersForClinicAsync_ReturnsOnlyClinicOrdersByRequestedDeliveryDateDescending()
    {
        var repo = new SqliteOrderRepo(_contextFactory);
        await repo.CreateOrderAsync(BuildOrder(
            "AAA-234",
            "demo due sooner",
            DateTimeOffset.Parse("2026-05-31T10:00:00Z"),
            clinicCode: "DEMO",
            requestedDeliveryDate: new DateOnly(2026, 6, 5)));
        await repo.CreateOrderAsync(BuildOrder(
            "BBB-234",
            "other due sooner",
            DateTimeOffset.Parse("2026-05-31T11:00:00Z"),
            clinicCode: "OTHER",
            requestedDeliveryDate: new DateOnly(2026, 6, 1)));
        await repo.CreateOrderAsync(BuildOrder(
            "CCC-234",
            "demo due later",
            DateTimeOffset.Parse("2026-05-31T12:00:00Z"),
            clinicCode: "DEMO",
            requestedDeliveryDate: new DateOnly(2026, 6, 10)));

        var orders = await repo.ListOrdersForClinicAsync("DEMO");

        Assert.That(orders.Select(o => o.OrderCode), Is.EqualTo(new[] { "CCC-234", "AAA-234" }));
    }

    [Test]
    public async Task CreateOrderAsync_PersistsWorkItemsJsonAndRoundTripsThroughQueries()
    {
        var repo = new SqliteOrderRepo(_contextFactory);
        var order = BuildOrder("WRK-234", "work items", DateTimeOffset.Parse("2026-05-31T10:00:00Z")) with
        {
            WorkItems =
            [
                new OrderWorkItem(ConstructionType.Bridge, new ToothRange(11, 13)),
                new OrderWorkItem(ConstructionType.Crown, new ToothRange(23, 23))
            ]
        };

        await repo.CreateOrderAsync(order);

        await using (var ctx = _contextFactory())
        {
            var entity = await ctx.SchedulingOrders.SingleAsync(o => o.OrderCode == "WRK-234");
            Assert.That(entity.WorkItemsJson, Does.Contain("bridge"));
            Assert.That(entity.WorkItemsJson, Does.Contain("23"));
        }

        var byCode = await repo.GetOrderByCodeAsync("WRK-234");
        var fromList = (await repo.ListOrdersAsync()).Single(o => o.OrderCode == "WRK-234");
        var fromCalendar = (await repo.ListActiveOrdersForCalendarAsync(null, new DateOnly(2026, 6, 5), new DateOnly(2026, 6, 5))).Single(o => o.OrderCode == "WRK-234");

        Assert.That(byCode!.WorkItems.Select(i => i.ToothStart), Is.EqualTo(new[] { 13, 23 }));
        Assert.That(fromList.WorkItems, Has.Count.EqualTo(2));
        Assert.That(fromCalendar.WorkItems, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task CurrentSchedulingOrderSchema_DoesNotContainLegacySingleWorkItemColumns()
    {
        await using var ctx = _contextFactory();
        var columns = await ctx.Database.SqlQueryRaw<string>("SELECT name AS Value FROM pragma_table_info('SchedulingOrders')").ToListAsync();

        Assert.That(columns, Does.Contain("WorkItemsJson"));
        Assert.That(columns, Does.Not.Contain("WorkType"));
        Assert.That(columns, Does.Not.Contain("ConstructionType"));
        Assert.That(columns, Does.Not.Contain("ToothStart"));
        Assert.That(columns, Does.Not.Contain("ToothEnd"));
        Assert.That(columns, Does.Not.Contain("AbutmentTeeth"));
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

    private static OrderRecord BuildOrder(
        string code,
        string caseName,
        DateTimeOffset createdAt,
        Shade shade = Shade.Unspecified,
        string clinicCode = "DEMO",
        DateOnly? requestedDeliveryDate = null) => new(
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
        Material.FullContourZirconia,
        [new OrderWorkItem(ConstructionType.Crown, new ToothRange(11, 11))],
        requestedDeliveryDate ?? new DateOnly(2026, 6, 5),
        OrderStatus.Created,
        shade,
        null,
        createdAt,
        createdAt,
        "127.0.0.1",
        "test");
}
