using Orders;

namespace Orders.Tests;

[Parallelizable(ParallelScope.All)]
public class SchedulingOrderServiceTest
{
    [Test]
    public async Task UpdateOrderAsync_GivenClinicOwnOrder_UpdatesFields()
    {
        var fixture = new Fixture(1);
        var created = await fixture.Service.CreateOrderAsync(TestActors.Demo, fixture.CreateOrderDraft("Original"), "127.0.0.1", "test");
        var draft = fixture.CreateOrderDraft("Updated case") with
        {
            TeethRange = new ToothRange(12, 12),
            RequestedDeliveryDate = new DateOnly(2026, 6, 9),
            Notes = "updated note"
        };

        var updated = await fixture.Service.UpdateOrderAsync(TestActors.Demo, created.OrderCode, draft);

        Assert.That(updated.OrderCode, Is.EqualTo(created.OrderCode));
        Assert.That(updated.CaseName, Is.EqualTo("Updated case"));
        Assert.That(updated.ToothStart, Is.EqualTo(12));
        Assert.That(updated.Notes, Is.EqualTo("updated note"));
        Assert.That(updated.CreatedAt, Is.EqualTo(created.CreatedAt));
        Assert.That(updated.Status, Is.EqualTo(OrderStatus.Created));
    }

    [Test]
    public async Task UpdateOrderAsync_GivenClinicOtherOrder_ReturnsNotFound()
    {
        var fixture = new Fixture(1);
        var created = await fixture.Service.CreateOrderAsync(TestActors.Technician, fixture.CreateOrderDraft("Other"), "127.0.0.1", "test", "OTHER");

        Assert.ThrowsAsync<KeyNotFoundException>(async () =>
            await fixture.Service.UpdateOrderAsync(TestActors.Demo, created.OrderCode, fixture.CreateOrderDraft("Nope")));
    }

    [Test]
    public async Task UpdateOrderAsync_GivenTechnicianAnyOrder_UpdatesFields()
    {
        var fixture = new Fixture(1);
        var created = await fixture.Service.CreateOrderAsync(TestActors.Demo, fixture.CreateOrderDraft("Original"), "127.0.0.1", "test");

        var updated = await fixture.Service.UpdateOrderAsync(TestActors.Technician, created.OrderCode, fixture.CreateOrderDraft("Tech edit"));

        Assert.That(updated.CaseName, Is.EqualTo("Tech edit"));
    }

    [Test]
    public async Task CancelOrderAsync_SetsCancelledAndRejectsFurtherChanges()
    {
        var fixture = new Fixture(1);
        var created = await fixture.Service.CreateOrderAsync(TestActors.Demo, fixture.CreateOrderDraft("Cancel me"), "127.0.0.1", "test");

        var cancelled = await fixture.Service.CancelOrderAsync(TestActors.Demo, created.OrderCode);

        Assert.That(cancelled.Status, Is.EqualTo(OrderStatus.Cancelled));
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await fixture.Service.UpdateOrderAsync(TestActors.Demo, created.OrderCode, fixture.CreateOrderDraft("No edit")));
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await fixture.Service.CancelOrderAsync(TestActors.Demo, created.OrderCode));
    }

    [Test]
    public async Task CreateOrderAsync_GivenTechnicianTargetClinic_CreatesForTargetClinicWithTechnicianCredential()
    {
        var fixture = new Fixture(1);

        var created = await fixture.Service.CreateOrderAsync(TestActors.Technician, fixture.CreateOrderDraft("For other"), "127.0.0.1", "test", "OTHER");

        Assert.That(created.ClinicCode, Is.EqualTo("OTHER"));
        Assert.That(created.ClinicDisplayName, Is.EqualTo("Other Clinic"));
        Assert.That(created.CredentialId, Is.EqualTo(TestActors.Technician.CredentialId));
    }

    [Test]
    public void CreateOrderAsync_GivenClinicTargetingOtherClinic_Rejects()
    {
        var fixture = new Fixture(1);

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await fixture.Service.CreateOrderAsync(TestActors.Demo, fixture.CreateOrderDraft("Bad target"), "127.0.0.1", "test", "OTHER"));
    }

    [Test]
    public void CreateOrderAsync_GivenTechnicianWithoutTargetClinic_Rejects()
    {
        var fixture = new Fixture(1);

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await fixture.Service.CreateOrderAsync(TestActors.Technician, fixture.CreateOrderDraft("Missing target"), "127.0.0.1", "test"));
    }

    [Test]
    public async Task CreateOrderAsync_WhenManyConcurrentRequestsReceiveSameFirstOrderCode_RetriesAndAllSucceed()
    {
        const int racingOrderCount = 20;
        var fixture = new Fixture(racingOrderCount);
        using var startBarrier = new Barrier(racingOrderCount);

        var tasks = Enumerable.Range(1, racingOrderCount)
            .Select(i => Task.Factory.StartNew(async () =>
            {
                if (!startBarrier.SignalAndWait(TimeSpan.FromSeconds(10)))
                    throw new InvalidOperationException("Timed out waiting for racing order creation requests.");

                return await fixture.Service.CreateOrderAsync(
                    TestActors.Demo,
                    fixture.CreateOrderDraft($"Race case {i}"),
                    "127.0.0.1",
                    $"test-{i}");
            }).Unwrap());
        var results = await Task.WhenAll(tasks);

        Assert.That(results, Has.Length.EqualTo(racingOrderCount));
        Assert.That(results.Select(r => r.OrderCode), Is.Unique);
        var uniqueGeneratorCodes = fixture.GeneratorCodes.ToHashSet();
        Assert.That(results.Select(r => r.OrderCode), Is.EquivalentTo(uniqueGeneratorCodes));
    }

    [Test]
    public async Task CreateOrderAsync_WhenMaxAttemptsExceeded_LeavesEarlierRaceWinnersPersisted()
    {
        const int racingOrderCount = 10;
        const int maxOrderCodeAttempts = 3;
        const string raceCode = "ABC-234";
        var winningCodes = new[] { "WIN-A", "WIN-B", "WIN-C" };
        var generatorCodes = winningCodes
            .Concat([raceCode])
            .Concat(Enumerable.Repeat(raceCode, racingOrderCount * maxOrderCodeAttempts))
            .ToArray();
        var fixture = new Fixture(generatorCodes, maxOrderCodeAttempts);
        using var startBarrier = new Barrier(racingOrderCount);

        var tasks = Enumerable.Range(1, racingOrderCount)
            .Select(i => Task.Factory.StartNew(async () =>
            {
                if (!startBarrier.SignalAndWait(TimeSpan.FromSeconds(10)))
                    throw new InvalidOperationException("Timed out waiting for racing order creation requests.");

                try
                {
                    var order = await fixture.Service.CreateOrderAsync(
                        TestActors.Demo,
                        fixture.CreateOrderDraft($"Race case {i}"),
                        "127.0.0.1",
                        $"test-{i}");
                    return (Order: (OrderRecord?)order, Error: (Exception?)null);
                }
                catch (Exception ex)
                {
                    return (Order: (OrderRecord?)null, Error: ex);
                }
            }).Unwrap());
        var outcomes = await Task.WhenAll(tasks);

        var winners = outcomes.Where(o => o.Order != null).Select(o => o.Order!).ToArray();
        var failures = outcomes.Where(o => o.Error != null).Select(o => o.Error!).ToArray();

        Assert.That(winners, Has.Length.EqualTo(winningCodes.Length + 1));
        Assert.That(failures, Has.Length.EqualTo(racingOrderCount - winners.Length));
        Assert.That(failures, Is.All.TypeOf<DuplicateOrderCodeException>());
        Assert.That(winners.Select(w => w.OrderCode), Is.EquivalentTo(winningCodes.Append(raceCode)));

        var persistedOrders = await fixture.Repository.ListOrdersAsync();
        Assert.That(persistedOrders, Has.Count.EqualTo(winners.Length));
        Assert.That(persistedOrders.Select(o => o.OrderCode), Is.EquivalentTo(winners.Select(w => w.OrderCode)));
        foreach (var winner in winners)
        {
            Assert.That(
                await fixture.Repository.GetOrderByCodeAsync(winner.OrderCode),
                Is.EqualTo(persistedOrders.Single(o => o.OrderCode == winner.OrderCode)));
        }
    }

    private sealed class Fixture
    {
        private readonly IList<string> _generatorCodes;
        public IList<string> GeneratorCodes => _generatorCodes;
        public IOrderRepository Repository { get; }

        public Fixture(int racingOrderCount, int maxOrderCodeAttempts = 20)
            : this(Enumerable
                .Repeat("racecode", racingOrderCount) // race all on the first code, only 1st should win this code
                .Concat(Enumerable.Range(2, racingOrderCount - 1) // generate unique code for each that didn't win 1st race
                    .Select(i => $"R{i:00}-XYZ")
                    .ToArray()).ToArray(),
                maxOrderCodeAttempts)
        {
        }

        public Fixture(IReadOnlyList<string> generatorCodes, int maxOrderCodeAttempts = 20)
        {
            _generatorCodes = generatorCodes.ToList();
            Repository = new InMemoryOrderRepository();
            var dateAvailabilityService = new DateAvailabilityService(new WeekendOnlyNonWorkingDayProvider());
            var orderCodeGenerator = new SequenceOrderCodeGenerator(_generatorCodes.ToArray());
            var clock = new FixedClock(new DateTimeOffset(2026, 5, 31, 12, 0, 0, TimeSpan.Zero));
            Service = new SchedulingOrderService(
                TestSchedulingConfigProvider.Create(),
                Repository,
                dateAvailabilityService,
                orderCodeGenerator,
                clock,
                maxOrderCodeAttempts);
        }

        public SchedulingOrderService Service { get; }

        public OrderDraft CreateOrderDraft(string caseName) =>
            new(
                caseName,
                new DateOnly(2026, 6, 2),
                ProductCategory.Permanent,
                WorkType.Crown,
                Material.FullContourZirconia,
                ConstructionType.Crown,
                new ToothRange(11, 11),
                new DateOnly(2026, 6, 5),
                Shade.A3,
                null);

        private sealed class SequenceOrderCodeGenerator : IOrderCodeGenerator
        {
            private readonly Queue<string> _codes;
            private readonly object _gate = new();

            public SequenceOrderCodeGenerator(params string[] codes) => _codes = new Queue<string>(codes);

            public string Generate(OrderDraft draft)
            {
                lock (_gate)
                {
                    if (_codes.Count == 0) throw new InvalidOperationException("No more test order codes.");
                    return _codes.Dequeue();
                }
            }
        }

        private sealed class InMemoryOrderRepository : IOrderRepository
        {
            private readonly object _gate = new();
            private readonly Dictionary<string, OrderRecord> _ordersByCode = new(StringComparer.OrdinalIgnoreCase);
            private long _nextId = 1;

            public Task<OrderRecord> CreateOrderAsync(OrderRecord order, CancellationToken ct = default)
            {
                lock (_gate)
                {
                    if (_ordersByCode.ContainsKey(order.OrderCode))
                        throw new DuplicateOrderCodeException(order.OrderCode);
                    var saved = order with { Id = _nextId++ };
                    _ordersByCode.Add(saved.OrderCode, saved);
                    return Task.FromResult(saved);
                }
            }

            public Task<OrderRecord?> GetOrderByCodeAsync(string orderCode, CancellationToken ct = default)
            {
                lock (_gate)
                    return Task.FromResult(_ordersByCode.TryGetValue(orderCode, out var order) ? order : null);
            }

            public Task<OrderRecord> UpdateOrderAsync(OrderRecord order, CancellationToken ct = default)
            {
                lock (_gate)
                {
                    if (!_ordersByCode.ContainsKey(order.OrderCode))
                        throw new InvalidOperationException("Order not found.");
                    _ordersByCode[order.OrderCode] = order;
                    return Task.FromResult(order);
                }
            }

            public Task<IReadOnlyList<OrderRecord>> ListOrdersAsync(int limit = 100, CancellationToken ct = default)
            {
                lock (_gate)
                    return Task.FromResult<IReadOnlyList<OrderRecord>>(_ordersByCode.Values.ToList());
            }

            public Task<IReadOnlyList<OrderRecord>> ListOrdersForClinicAsync(string clinicCode, int limit = 100, CancellationToken ct = default)
            {
                lock (_gate)
                    return Task.FromResult<IReadOnlyList<OrderRecord>>(
                        _ordersByCode.Values.Where(o => string.Equals(o.ClinicCode, clinicCode, StringComparison.OrdinalIgnoreCase)).ToList());
            }
        }
    }
}