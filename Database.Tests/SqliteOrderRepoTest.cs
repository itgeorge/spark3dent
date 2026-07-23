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
    public async Task CreateOrderAsync_PersistsPmmaTelioMaterial_WhenReadBackByCodeListAndCalendar()
    {
        var repo = new SqliteOrderRepo(_contextFactory);
        await repo.CreateOrderAsync(BuildOrder("TEL-234", "telio", DateTimeOffset.Parse("2026-05-31T10:00:00Z")) with
        {
            Material = Material.PmmaTelio,
            ProductCategory = ProductCategory.Temporary
        });

        var byCode = await repo.GetOrderByCodeAsync("TEL-234");
        var fromList = (await repo.ListOrdersAsync()).Single(o => o.OrderCode == "TEL-234");
        var fromCalendar = (await repo.ListActiveOrdersForCalendarAsync(new OrderVisibilityScope(null, null), new DateOnly(2026, 6, 5), new DateOnly(2026, 6, 5))).Single(o => o.OrderCode == "TEL-234");

        Assert.That(byCode!.Material, Is.EqualTo(Material.PmmaTelio));
        Assert.That(fromList.Material, Is.EqualTo(Material.PmmaTelio));
        Assert.That(fromCalendar.Material, Is.EqualTo(Material.PmmaTelio));
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
    public async Task CreateOrderAsync_PersistsColorNote_WhenReadBackByCodeListAndCalendar()
    {
        var repo = new SqliteOrderRepo(_contextFactory);
        await repo.CreateOrderAsync(BuildOrder("CLR-234", "color note", DateTimeOffset.Parse("2026-05-31T10:00:00Z")) with
        {
            ColorNote = "body A3, cervical A3.5"
        });

        var byCode = await repo.GetOrderByCodeAsync("CLR-234");
        var fromList = (await repo.ListOrdersAsync()).Single(o => o.OrderCode == "CLR-234");
        var fromCalendar = (await repo.ListActiveOrdersForCalendarAsync(new OrderVisibilityScope(null, null), new DateOnly(2026, 6, 5), new DateOnly(2026, 6, 5))).Single(o => o.OrderCode == "CLR-234");

        Assert.That(byCode!.ColorNote, Is.EqualTo("body A3, cervical A3.5"));
        Assert.That(fromList.ColorNote, Is.EqualTo("body A3, cervical A3.5"));
        Assert.That(fromCalendar.ColorNote, Is.EqualTo("body A3, cervical A3.5"));
    }

    [Test]
    public async Task CreateAndUpdateOrderAsync_PersistsCalculatedCapacityUnits_AsDecimalOrNull()
    {
        var repo = new SqliteOrderRepo(_contextFactory);
        await repo.CreateOrderAsync(BuildOrder("CAP-234", "capacity", DateTimeOffset.Parse("2026-05-31T10:00:00Z")) with
        {
            CalculatedCapacityUnits = 1.5m
        });
        await repo.CreateOrderAsync(BuildOrder("NULL-234", "legacy", DateTimeOffset.Parse("2026-05-31T10:01:00Z")) with
        {
            CalculatedCapacityUnits = null
        });

        var updated = await repo.UpdateOrderAsync((await repo.GetOrderByCodeAsync("CAP-234"))! with { CalculatedCapacityUnits = 2.75m });
        var withCapacity = await repo.GetOrderByCodeAsync("CAP-234");
        var withoutCapacity = await repo.GetOrderByCodeAsync("NULL-234");

        Assert.That(updated.CalculatedCapacityUnits, Is.EqualTo(2.75m));
        Assert.That(withCapacity!.CalculatedCapacityUnits, Is.EqualTo(2.75m));
        Assert.That(withoutCapacity!.CalculatedCapacityUnits, Is.Null);
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

        var demoOrders = await repo.ListActiveOrdersForCalendarAsync(new OrderVisibilityScope("DEMO", null), new DateOnly(2026, 6, 5), new DateOnly(2026, 6, 10));
        var allOrders = await repo.ListActiveOrdersForCalendarAsync(new OrderVisibilityScope(null, null), new DateOnly(2026, 6, 5), new DateOnly(2026, 6, 10));

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
    public async Task ListActiveOrdersByDeadlineRangeAsync_ReturnsAllClinicsAndExcludesCancelled()
    {
        var repo = new SqliteOrderRepo(_contextFactory);
        await repo.CreateOrderAsync(BuildOrder("AAA-234", "demo", DateTimeOffset.Parse("2026-05-31T10:00:00Z"), clinicCode: "DEMO", requestedDeliveryDate: new DateOnly(2026, 6, 5)));
        var cancelled = await repo.CreateOrderAsync(BuildOrder("BBB-234", "cancelled", DateTimeOffset.Parse("2026-05-31T11:00:00Z"), clinicCode: "DEMO", requestedDeliveryDate: new DateOnly(2026, 6, 6)));
        await repo.UpdateOrderAsync(cancelled with { Status = OrderStatus.Cancelled });
        await repo.CreateOrderAsync(BuildOrder("CCC-234", "other", DateTimeOffset.Parse("2026-05-31T12:00:00Z"), clinicCode: "OTHER", requestedDeliveryDate: new DateOnly(2026, 6, 7)));

        var orders = await repo.ListActiveOrdersByDeadlineRangeAsync(new DateOnly(2026, 6, 5), new DateOnly(2026, 6, 7));

        Assert.That(orders.Select(o => o.OrderCode), Is.EqualTo(new[] { "AAA-234", "CCC-234" }));
    }

    [Test]
    public async Task ListOrdersPageAsync_ReturnsCursorPagesWithoutDuplicatesAndWithClinicScope()
    {
        var repo = new SqliteOrderRepo(_contextFactory);
        await repo.CreateOrderAsync(BuildOrder("OLD-234", "old", DateTimeOffset.Parse("2026-05-31T10:00:00Z"), requestedDeliveryDate: new DateOnly(2026, 6, 5)));
        await repo.CreateOrderAsync(BuildOrder("MID-234", "mid", DateTimeOffset.Parse("2026-05-31T11:00:00Z"), requestedDeliveryDate: new DateOnly(2026, 6, 6)));
        await repo.CreateOrderAsync(BuildOrder("NEW-234", "new", DateTimeOffset.Parse("2026-05-31T12:00:00Z"), requestedDeliveryDate: new DateOnly(2026, 6, 7)));
        await repo.CreateOrderAsync(BuildOrder("OTH-234", "other", DateTimeOffset.Parse("2026-05-31T13:00:00Z"), clinicCode: "OTHER", requestedDeliveryDate: new DateOnly(2026, 6, 8)));

        var first = await repo.ListOrdersPageAsync(new OrderVisibilityScope("DEMO", null), 2, null);
        var second = await repo.ListOrdersPageAsync(new OrderVisibilityScope("DEMO", null), 2, OrderCursorCodec.Decode(first.NextCursor));
        var tech = await repo.ListOrdersPageAsync(new OrderVisibilityScope(null, null), 10, null);

        Assert.That(first.Items.Select(o => o.OrderCode), Is.EqualTo(new[] { "NEW-234", "MID-234" }));
        Assert.That(first.HasMore, Is.True);
        Assert.That(first.NextCursor, Is.Not.Null);
        Assert.That(second.Items.Select(o => o.OrderCode), Is.EqualTo(new[] { "OLD-234" }));
        Assert.That(second.HasMore, Is.False);
        Assert.That(first.Items.Concat(second.Items).Select(o => o.OrderCode), Is.Unique);
        Assert.That(tech.Items.Select(o => o.OrderCode), Does.Contain("OTH-234"));
    }

    [Test]
    public async Task ListOrdersPageContainingOrderAsync_ReturnsPageContainingTargetAndFindSuffixRespectsScope()
    {
        var repo = new SqliteOrderRepo(_contextFactory);
        await repo.CreateOrderAsync(BuildOrder("26-0605-Z1AA", "old", DateTimeOffset.Parse("2026-05-31T10:00:00Z"), requestedDeliveryDate: new DateOnly(2026, 6, 5)));
        var target = await repo.CreateOrderAsync(BuildOrder("26-0606-Z1BB", "target", DateTimeOffset.Parse("2026-05-31T11:00:00Z"), requestedDeliveryDate: new DateOnly(2026, 6, 6)));
        await repo.CreateOrderAsync(BuildOrder("27-0605-Z1AA", "other", DateTimeOffset.Parse("2026-05-31T12:00:00Z"), clinicCode: "OTHER", requestedDeliveryDate: new DateOnly(2027, 6, 5)));

        var page = await repo.ListOrdersPageContainingOrderAsync(new OrderVisibilityScope("DEMO", null), target, 1);
        var demoShortMatches = await repo.FindOrdersByCodeSuffixAsync(new OrderVisibilityScope("DEMO", null), "0605-Z1AA", 2);
        var techShortMatches = await repo.FindOrdersByCodeSuffixAsync(new OrderVisibilityScope(null, null), "0605-Z1AA", 2);

        Assert.That(page.Items.Select(o => o.OrderCode), Does.Contain(target.OrderCode));
        Assert.That(demoShortMatches.Select(o => o.OrderCode), Is.EqualTo(new[] { "26-0605-Z1AA" }));
        Assert.That(techShortMatches, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task FindOrdersByCodeSuffixAsync_FiltersByClinicMemberScope()
    {
        var repo = new SqliteOrderRepo(_contextFactory);
        await repo.CreateOrderAsync(BuildOrder("26-0605-MEM1", "member one", DateTimeOffset.Parse("2026-05-31T10:00:00Z"), memberId: "assistant-1"));
        await repo.CreateOrderAsync(BuildOrder("26-0605-MEM2", "member two", DateTimeOffset.Parse("2026-05-31T11:00:00Z"), memberId: "assistant-2"));

        var scopedMatches = await repo.FindOrdersByCodeSuffixAsync(new OrderVisibilityScope("DEMO", "assistant-1"), "0605-MEM2", 2);
        var clinicMatches = await repo.FindOrdersByCodeSuffixAsync(new OrderVisibilityScope("DEMO", null), "0605-MEM2", 2);
        var labMatches = await repo.FindOrdersByCodeSuffixAsync(new OrderVisibilityScope(null, null), "0605-MEM2", 2);

        Assert.That(scopedMatches, Is.Empty);
        Assert.That(clinicMatches.Select(o => o.OrderCode), Is.EqualTo(new[] { "26-0605-MEM2" }));
        Assert.That(labMatches.Select(o => o.OrderCode), Is.EqualTo(new[] { "26-0605-MEM2" }));
    }

    [Test]
    public async Task ListActiveOrdersForCalendarAsync_FiltersByClinicMemberScope()
    {
        var repo = new SqliteOrderRepo(_contextFactory);
        await repo.CreateOrderAsync(BuildOrder("CAL-A-234", "member a", DateTimeOffset.Parse("2026-05-31T10:00:00Z"), memberId: "assistant-1", requestedDeliveryDate: new DateOnly(2026, 6, 6)));
        await repo.CreateOrderAsync(BuildOrder("CAL-B-234", "member b", DateTimeOffset.Parse("2026-05-31T11:00:00Z"), memberId: "assistant-2", requestedDeliveryDate: new DateOnly(2026, 6, 6)));

        var scoped = await repo.ListActiveOrdersForCalendarAsync(new OrderVisibilityScope("DEMO", "assistant-1"), new DateOnly(2026, 6, 6), new DateOnly(2026, 6, 6));
        var clinic = await repo.ListActiveOrdersForCalendarAsync(new OrderVisibilityScope("DEMO", null), new DateOnly(2026, 6, 6), new DateOnly(2026, 6, 6));
        var lab = await repo.ListActiveOrdersForCalendarAsync(new OrderVisibilityScope(null, null), new DateOnly(2026, 6, 6), new DateOnly(2026, 6, 6));

        Assert.That(scoped.Select(o => o.OrderCode), Is.EqualTo(new[] { "CAL-A-234" }));
        Assert.That(clinic.Select(o => o.OrderCode), Is.EqualTo(new[] { "CAL-A-234", "CAL-B-234" }));
        Assert.That(lab.Select(o => o.OrderCode), Is.EqualTo(new[] { "CAL-A-234", "CAL-B-234" }));
    }

    [Test]
    public async Task ListActiveOrdersByDeadlineRangeAsync_RemainsUnscopedByMember()
    {
        var repo = new SqliteOrderRepo(_contextFactory);
        await repo.CreateOrderAsync(BuildOrder("DLN-A-234", "member a", DateTimeOffset.Parse("2026-05-31T10:00:00Z"), memberId: "assistant-1", requestedDeliveryDate: new DateOnly(2026, 6, 6)));
        await repo.CreateOrderAsync(BuildOrder("DLN-B-234", "member b", DateTimeOffset.Parse("2026-05-31T11:00:00Z"), memberId: "assistant-2", requestedDeliveryDate: new DateOnly(2026, 6, 6)));

        var orders = await repo.ListActiveOrdersByDeadlineRangeAsync(new DateOnly(2026, 6, 6), new DateOnly(2026, 6, 6));

        Assert.That(orders.Select(o => o.OrderCode), Is.EqualTo(new[] { "DLN-A-234", "DLN-B-234" }));
    }

    [Test]
    public async Task ListOrdersPageAsync_FiltersByClinicMemberScope()
    {
        var repo = new SqliteOrderRepo(_contextFactory);
        await repo.CreateOrderAsync(BuildOrder("MEM-A-234", "member a", DateTimeOffset.Parse("2026-05-31T10:00:00Z"), memberId: "assistant-1"));
        await repo.CreateOrderAsync(BuildOrder("MEM-B-234", "member b", DateTimeOffset.Parse("2026-05-31T11:00:00Z"), memberId: "assistant-2"));

        var scoped = await repo.ListOrdersPageAsync(new OrderVisibilityScope("DEMO", "assistant-1"), 10, null);

        Assert.That(scoped.Items.Select(o => o.OrderCode), Is.EqualTo(new[] { "MEM-A-234" }));
    }

    [Test]
    public void OrderCursorCodec_InvalidCursorThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => OrderCursorCodec.Decode("not-a-cursor"));
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
        var fromCalendar = (await repo.ListActiveOrdersForCalendarAsync(new OrderVisibilityScope(null, null), new DateOnly(2026, 6, 5), new DateOnly(2026, 6, 5))).Single(o => o.OrderCode == "WRK-234");

        Assert.That(byCode!.WorkItems.Select(i => i.ToothStart), Is.EqualTo(new[] { 13, 23 }));
        Assert.That(fromList.WorkItems, Has.Count.EqualTo(2));
        Assert.That(fromCalendar.WorkItems, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task CreateOrderAsync_DoesNotPersistMemberPinHashFingerprint()
    {
        var repo = new SqliteOrderRepo(_contextFactory);

        await repo.CreateOrderAsync(BuildOrder("NFP-234", "no fingerprint", DateTimeOffset.Parse("2026-05-31T10:00:00Z")));

        await using var ctx = _contextFactory();
        var entity = await ctx.SchedulingOrders.SingleAsync(o => o.OrderCode == "NFP-234");
        Assert.That(entity.MemberId, Is.EqualTo("cred-1"));
        Assert.That(entity.MemberLabel, Is.EqualTo("Credential 1"));
        Assert.That(entity.MemberPinHashFingerprint, Is.Empty);
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
        string? memberId = null,
        DateOnly? requestedDeliveryDate = null) => new(
        0,
        code,
        clinicCode,
        clinicCode == "DEMO" ? "Demo Clinic" : "Other Clinic",
        memberId ?? "cred-1",
        memberId == "assistant-2" ? "Assistant 2" : "Credential 1",
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
