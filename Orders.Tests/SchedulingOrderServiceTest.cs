using Orders;

namespace Orders.Tests;

public class SchedulingOrderServiceTest
{
    [Test]
    public async Task CreateOrderAsync_WhenManyConcurrentRequestsReceiveSameFirstOrderCode_RetriesAndAllSucceed()
    {
        var racingOrderCount = 20;
        var sharedFirstCode = "ABC-234";
        using var startBarrier = new Barrier(racingOrderCount);
        var repo = new InMemoryOrderRepository();
        var service = new SchedulingOrderService(
            TestSchedulingConfigProvider.Create(),
            repo,
            new DateAvailabilityService(new WeekendOnlyNonWorkingDayProvider()),
            new SequenceOrderCodeGenerator(
                Enumerable.Repeat(sharedFirstCode, racingOrderCount)
                    .Concat(Enumerable.Range(1, racingOrderCount).Select(i => $"R{i:00}-XYZ"))
                    .ToArray()),
            new FixedClock(new DateTimeOffset(2026, 5, 31, 12, 0, 0, TimeSpan.Zero)));
        var tasks = Enumerable.Range(1, racingOrderCount)
            .Select(i => Task.Factory.StartNew(
                async () =>
                {
                    if (!startBarrier.SignalAndWait(TimeSpan.FromSeconds(10)))
                        throw new InvalidOperationException("Timed out waiting for racing order creation requests.");

                    return await service.CreateOrderAsync(
                        TestActors.Demo,
                        CreateOrderDraft($"Race case {i}"),
                        "127.0.0.1",
                        $"test-{i}");
                },
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default).Unwrap())
            .ToArray();

        var results = await Task.WhenAll(tasks);

        Assert.That(results, Has.Length.EqualTo(racingOrderCount));
        Assert.That(results.Select(r => r.OrderCode), Is.Unique);
        Assert.That(results.Select(r => r.OrderCode), Does.Contain(sharedFirstCode));
    }

    private static OrderDraft CreateOrderDraft(string caseName) =>
        new(
            caseName,
            new DateOnly(2026, 6, 2),
            ProductCategory.Permanent,
            WorkType.Crown,
            Material.FullContourZirconia,
            ConstructionType.Crown,
            new ToothRange(11, 11),
            new DateOnly(2026, 6, 5),
            null);

    private sealed class SequenceOrderCodeGenerator : IOrderCodeGenerator
    {
        private readonly Queue<string> _codes;
        private readonly object _gate = new();

        public SequenceOrderCodeGenerator(params string[] codes) => _codes = new Queue<string>(codes);

        public string Generate()
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
