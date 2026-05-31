using Orders;

namespace Orders.Tests;

public class SchedulingOrderServiceTest
{
    private const int RacingOrderCount = 20;
    private const string RaceOrderCode = "ABC-234";

    [Test]
    public async Task CreateOrderAsync_WhenManyConcurrentRequestsRaceOnSameOrderCode_RetriesAndAllSucceed()
    {
        var repo = new RaceyOrderRepository(RaceOrderCode, RacingOrderCount);
        var service = new SchedulingOrderService(
            TestSchedulingConfigProvider.Create(),
            repo,
            new DateAvailabilityService(new WeekendOnlyNonWorkingDayProvider()),
            new SequenceOrderCodeGenerator(BuildRaceCodes()),
            new FixedClock(new DateTimeOffset(2026, 5, 31, 12, 0, 0, TimeSpan.Zero)));
        var draft = new OrderDraft(
            "Race case",
            new DateOnly(2026, 6, 2),
            ProductCategory.Permanent,
            WorkType.Crown,
            Material.FullContourZirconia,
            ConstructionType.Crown,
            new ToothRange(11, 11),
            new DateOnly(2026, 6, 5),
            null);

        var tasks = Enumerable.Range(1, RacingOrderCount)
            .Select(i => Task.Factory.StartNew(
                () => service.CreateOrderAsync(TestActors.Demo, draft with { CaseName = $"Race case {i}" }, "127.0.0.1", $"test-{i}"),
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default).Unwrap())
            .ToArray();

        var results = await Task.WhenAll(tasks);

        Assert.That(results, Has.Length.EqualTo(RacingOrderCount));
        Assert.That(results.Select(r => r.OrderCode), Is.Unique);
        Assert.That(results.Select(r => r.OrderCode), Does.Contain(RaceOrderCode));
        Assert.That(repo.RaceBarrierParticipants, Is.EqualTo(RacingOrderCount));
    }

    private static string[] BuildRaceCodes() =>
        Enumerable.Repeat(RaceOrderCode, RacingOrderCount)
            .Concat(Enumerable.Range(1, RacingOrderCount).Select(i => $"R{i:00}-XYZ"))
            .ToArray();

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

    private sealed class RaceyOrderRepository : IOrderRepository, IDisposable
    {
        private readonly object _gate = new();
        private readonly string _raceOrderCode;
        private readonly Barrier _raceBarrier;
        private readonly Dictionary<string, OrderRecord> _ordersByCode = new(StringComparer.OrdinalIgnoreCase);
        private int _raceBarrierParticipants;
        private long _nextId = 1;

        public RaceyOrderRepository(string raceOrderCode, int raceParticipantCount)
        {
            _raceOrderCode = raceOrderCode;
            _raceBarrier = new Barrier(raceParticipantCount);
        }

        public int RaceBarrierParticipants => _raceBarrierParticipants;

        public Task<OrderRecord> CreateOrderAsync(OrderRecord order, CancellationToken ct = default)
        {
            if (order.OrderCode == _raceOrderCode)
            {
                Interlocked.Increment(ref _raceBarrierParticipants);
                if (!_raceBarrier.SignalAndWait(TimeSpan.FromSeconds(10)))
                    throw new InvalidOperationException("Timed out waiting for racing order creation requests.");
            }

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

        public void Dispose() => _raceBarrier.Dispose();
    }
}
