using Orders;
using Utilities;

namespace Orders.Tests;

[Parallelizable(ParallelScope.All)]
public class SchedulingOrderServiceTest
{
    [Test]
    public async Task CreateOrderAsync_AppendsOrderCreatedAuditAfterPersistence()
    {
        var audit = new CapturingAuditLog();
        var fixture = new Fixture(1, auditLog: audit);

        var created = await fixture.Service.CreateOrderAsync(TestActors.Technician, fixture.CreateOrderDraft("Audit create"), "127.0.0.1", "test-agent", "OTHER");

        Assert.That(audit.Events, Has.Count.EqualTo(1));
        var evt = audit.Events[0];
        Assert.That(evt.Operation, Is.EqualTo("OrderCreated"));
        Assert.That(evt.EntityType, Is.EqualTo("SchedulingOrder"));
        Assert.That(evt.EntityId, Is.EqualTo(created.OrderCode));
        Assert.That(evt.ActorRole, Is.EqualTo("Technician"));
        Assert.That(evt.ActorClinicCode, Is.EqualTo(TestActors.Technician.ClinicCode));
        Assert.That(evt.ActorCredentialId, Is.EqualTo(TestActors.Technician.CredentialId));
        Assert.That(evt.Ip, Is.EqualTo("127.0.0.1"));
        Assert.That(evt.UserAgent, Is.EqualTo("test-agent"));
        Assert.That(evt.MetadataJson, Does.Contain("OTHER"));
    }

    [Test]
    public async Task UpdateAndCancelOrderAsync_AppendsAuditEvents()
    {
        var audit = new CapturingAuditLog();
        var fixture = new Fixture(1, auditLog: audit);
        var created = await fixture.Service.CreateOrderAsync(TestActors.Demo, fixture.CreateOrderDraft("Original"), "127.0.0.1", "test");

        await fixture.Service.UpdateOrderAsync(TestActors.Demo, created.OrderCode, fixture.CreateOrderDraft("Updated"), "10.0.0.1", "ua-edit");
        await fixture.Service.CancelOrderAsync(TestActors.Demo, created.OrderCode, "10.0.0.2", "ua-cancel");

        Assert.That(audit.Events.Select(e => e.Operation), Is.EqualTo(new[] { "OrderCreated", "OrderUpdated", "OrderCancelled" }));
        Assert.That(audit.Events[1].MetadataJson, Does.Contain("CaseName"));
        Assert.That(audit.Events[2].MetadataJson, Does.Contain("previousStatus"));
    }

    [Test]
    public void FailedValidationOrAuthorization_DoesNotAppendMutationAuditEvent()
    {
        var audit = new CapturingAuditLog();
        var fixture = new Fixture(1, auditLog: audit);

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await fixture.Service.CreateOrderAsync(TestActors.Demo, fixture.CreateOrderDraft(" "), "127.0.0.1", "test"));
        Assert.ThrowsAsync<KeyNotFoundException>(async () =>
            await fixture.Service.UpdateOrderAsync(TestActors.Demo, "missing", fixture.CreateOrderDraft("Nope")));

        Assert.That(audit.Events, Is.Empty);
    }

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
    public async Task ListCalendarOrdersAsync_AppliesActorScopeRangeAndExcludesCancelled()
    {
        var fixture = new Fixture(4);
        var demoEarly = await fixture.Service.CreateOrderAsync(TestActors.Demo, fixture.CreateOrderDraft("Demo early") with
        {
            RequestedDeliveryDate = new DateOnly(2026, 6, 5)
        }, "127.0.0.1", "test");
        var demoLate = await fixture.Service.CreateOrderAsync(TestActors.Demo, fixture.CreateOrderDraft("Demo late") with
        {
            RequestedDeliveryDate = new DateOnly(2026, 6, 10)
        }, "127.0.0.1", "test");
        var other = await fixture.Service.CreateOrderAsync(TestActors.Technician, fixture.CreateOrderDraft("Other") with
        {
            RequestedDeliveryDate = new DateOnly(2026, 6, 10)
        }, "127.0.0.1", "test", "OTHER");
        var cancelled = await fixture.Service.CreateOrderAsync(TestActors.Demo, fixture.CreateOrderDraft("Cancelled") with
        {
            RequestedDeliveryDate = new DateOnly(2026, 6, 10)
        }, "127.0.0.1", "test");
        await fixture.Service.CancelOrderAsync(TestActors.Demo, cancelled.OrderCode);

        var clinicOrders = await fixture.Service.ListCalendarOrdersAsync(TestActors.Demo, new DateOnly(2026, 6, 5), new DateOnly(2026, 6, 10));
        var techOrders = await fixture.Service.ListCalendarOrdersAsync(TestActors.Technician, new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 10));

        Assert.That(clinicOrders.Select(o => o.OrderCode), Is.EquivalentTo(new[] { demoEarly.OrderCode, demoLate.OrderCode }));
        Assert.That(techOrders.Select(o => o.OrderCode), Is.EquivalentTo(new[] { demoLate.OrderCode, other.OrderCode }));
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await fixture.Service.ListCalendarOrdersAsync(TestActors.Demo, new DateOnly(2026, 6, 11), new DateOnly(2026, 6, 10)));
    }

    [Test]
    public async Task CreateOrderAsync_GivenMultipleOrderWorkItems_PersistsPrimaryCompatibilityFieldsAndAllItems()
    {
        var fixture = new Fixture(1);
        var draft = fixture.CreateOrderDraft("Multi") with
        {
            WorkItems =
            [
                new OrderWorkItem(ConstructionType.Bridge, new ToothRange(11, 13)),
                new OrderWorkItem(ConstructionType.Crown, new ToothRange(23, 23))
            ],
            RequestedDeliveryDate = new DateOnly(2026, 6, 10)
        };

        var created = await fixture.Service.CreateOrderAsync(TestActors.Demo, draft, "127.0.0.1", "test");

        Assert.That(created.WorkItems, Has.Count.EqualTo(2));
        Assert.That(created.ConstructionType, Is.EqualTo(ConstructionType.Bridge));
        Assert.That(created.ToothStart, Is.EqualTo(13));
        Assert.That(created.ToothEnd, Is.EqualTo(11));
        Assert.That(created.AbutmentTeeth, Is.EqualTo("13,11"));
    }

    [Test]
    public void CreateOrderAsync_GivenInvalidOrderWorkItems_Rejects()
    {
        var fixture = new Fixture(["A", "B", "C", "D", "E"]);
        var cases = new[]
        {
            fixture.CreateOrderDraft("empty") with { WorkItems = [] },
            fixture.CreateOrderDraft("crown range") with { WorkItems = [new OrderWorkItem(ConstructionType.Crown, new ToothRange(11, 12))] },
            fixture.CreateOrderDraft("bridge single") with { WorkItems = [new OrderWorkItem(ConstructionType.Bridge, new ToothRange(11, 11))] },
            fixture.CreateOrderDraft("cross jaw") with { WorkItems = [new OrderWorkItem(ConstructionType.Facet, new ToothRange(28, 31))] },
            fixture.CreateOrderDraft("overlap") with { WorkItems = [new OrderWorkItem(ConstructionType.Bridge, new ToothRange(11, 13)), new OrderWorkItem(ConstructionType.Crown, new ToothRange(12, 12))] }
        };

        foreach (var draft in cases)
        {
            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await fixture.Service.CreateOrderAsync(TestActors.Demo, draft, "127.0.0.1", "test"));
        }
    }

    [Test]
    public async Task CalculateMinimumDeliveryDateAsync_SumsLeadTimeAcrossOrderWorkItemsUsingDerivedWorkTypes()
    {
        var rules = new List<WorkRule>
        {
            new(ProductCategory.Permanent, WorkType.Crown, Material.FullContourZirconia, ConstructionType.Crown, 2),
            new(ProductCategory.Permanent, WorkType.Bridge, Material.FullContourZirconia, ConstructionType.Bridge, 4),
            new(ProductCategory.Permanent, WorkType.Crown, Material.FullContourZirconia, ConstructionType.Facet, 3)
        };
        var fixture = new Fixture(["A"], configProvider: TestSchedulingConfigProvider.Create(workRules: rules));
        var draft = fixture.CreateOrderDraft("Lead") with
        {
            WorkItems =
            [
                new OrderWorkItem(ConstructionType.Bridge, new ToothRange(11, 13)),
                new OrderWorkItem(ConstructionType.Crown, new ToothRange(23, 23)),
                new OrderWorkItem(ConstructionType.Facet, new ToothRange(31, 32))
            ]
        };

        var minimum = await fixture.Service.CalculateMinimumDeliveryDateAsync(draft);

        Assert.That(minimum, Is.EqualTo(new DateOnly(2026, 6, 15)));
    }

    [Test]
    public async Task UpdateOrderAsync_GivenWorkItemChange_MarksAuditWorkItemsChanged()
    {
        var audit = new CapturingAuditLog();
        var fixture = new Fixture(2, auditLog: audit);
        var created = await fixture.Service.CreateOrderAsync(TestActors.Demo, fixture.CreateOrderDraft("Original"), "127.0.0.1", "test");

        await fixture.Service.UpdateOrderAsync(TestActors.Demo, created.OrderCode, fixture.CreateOrderDraft("Original") with
        {
            WorkItems =
            [
                new OrderWorkItem(ConstructionType.Crown, new ToothRange(11, 11)),
                new OrderWorkItem(ConstructionType.Crown, new ToothRange(12, 12))
            ],
            RequestedDeliveryDate = new DateOnly(2026, 6, 10)
        });

        Assert.That(audit.Events.Last().MetadataJson, Does.Contain("WorkItems"));
        Assert.That(audit.Events.Last().MetadataJson, Does.Contain("newWorkItems"));
    }

    [Test]
    public async Task CreateOrderAsync_GivenLegacyDraft_ExposesOneOrderWorkItem()
    {
        var fixture = new Fixture(1);

        var created = await fixture.Service.CreateOrderAsync(TestActors.Demo, fixture.CreateOrderDraft("Legacy"), "127.0.0.1", "test");

        Assert.That(created.WorkItems, Has.Count.EqualTo(1));
        Assert.That(created.PrimaryWorkItem.ConstructionType, Is.EqualTo(ConstructionType.Crown));
        Assert.That(created.PrimaryWorkItem.ToothStart, Is.EqualTo(11));
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

    private sealed class CapturingAuditLog : IAuditLog
    {
        private readonly object _gate = new();
        private readonly List<AuditEvent> _events = new();

        public IReadOnlyList<AuditEvent> Events
        {
            get
            {
                lock (_gate)
                    return _events.ToList();
            }
        }

        public Task AppendAsync(AuditEvent auditEvent, CancellationToken ct = default)
        {
            lock (_gate)
                _events.Add(auditEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class Fixture
    {
        private readonly IList<string> _generatorCodes;
        public IList<string> GeneratorCodes => _generatorCodes;
        public IOrderRepository Repository { get; }

        public Fixture(int racingOrderCount, int maxOrderCodeAttempts = 20, IAuditLog? auditLog = null)
            : this(Enumerable
                .Repeat("racecode", racingOrderCount) // race all on the first code, only 1st should win this code
                .Concat(Enumerable.Range(2, racingOrderCount - 1) // generate unique code for each that didn't win 1st race
                    .Select(i => $"R{i:00}-XYZ")
                    .ToArray()).ToArray(),
                maxOrderCodeAttempts,
                auditLog)
        {
        }

        public Fixture(IReadOnlyList<string> generatorCodes, int maxOrderCodeAttempts = 20, IAuditLog? auditLog = null, TestSchedulingConfigProvider? configProvider = null)
        {
            _generatorCodes = generatorCodes.ToList();
            Repository = new InMemoryOrderRepository();
            var dateAvailabilityService = new DateAvailabilityService(new WeekendOnlyNonWorkingDayProvider());
            var orderCodeGenerator = new SequenceOrderCodeGenerator(_generatorCodes.ToArray());
            var clock = new FixedClock(new DateTimeOffset(2026, 5, 31, 12, 0, 0, TimeSpan.Zero));
            Service = new SchedulingOrderService(
                configProvider ?? TestSchedulingConfigProvider.Create(),
                Repository,
                dateAvailabilityService,
                orderCodeGenerator,
                clock,
                maxOrderCodeAttempts,
                auditLog);
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

            public Task<IReadOnlyList<OrderRecord>> ListActiveOrdersForCalendarAsync(string? clinicCode, DateOnly start, DateOnly end, CancellationToken ct = default)
            {
                lock (_gate)
                    return Task.FromResult<IReadOnlyList<OrderRecord>>(
                        _ordersByCode.Values
                            .Where(o => o.Status != OrderStatus.Cancelled
                                && o.RequestedDeliveryDate >= start
                                && o.RequestedDeliveryDate <= end
                                && (clinicCode == null || string.Equals(o.ClinicCode, clinicCode, StringComparison.OrdinalIgnoreCase)))
                            .OrderBy(o => o.RequestedDeliveryDate)
                            .ThenBy(o => o.ClinicDisplayName)
                            .ThenBy(o => o.CaseName)
                            .ThenBy(o => o.OrderCode)
                            .ToList());
            }
        }
    }
}