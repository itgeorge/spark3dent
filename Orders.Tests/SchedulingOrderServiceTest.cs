using Orders;

namespace Orders.Tests;

[Parallelizable(ParallelScope.All)]
public class SchedulingOrderServiceTest
{
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

            public Task<IReadOnlyList<OrderRecord>> ListOrdersAsync(int limit = 100, CancellationToken ct = default)
            {
                lock (_gate)
                    return Task.FromResult<IReadOnlyList<OrderRecord>>(_ordersByCode.Values.ToList());
            }
        }
    }
}