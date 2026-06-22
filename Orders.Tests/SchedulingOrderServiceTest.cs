using Orders;
using Utilities;

namespace Orders.Tests;

[Parallelizable(ParallelScope.All)]
public class SchedulingOrderServiceTest
{
    [Test]
    public async Task CreateOrderAsync_PersistsDeadlineRecommendationLogAfterSuccessfulCreate()
    {
        var logs = new CapturingDeadlineRecommendationLogRepository();
        var fixture = new Fixture(1, deadlineRecommendationLogs: logs);

        var created = await fixture.Service.CreateOrderAsync(TestActors.Demo, fixture.CreateOrderDraft("Logged"), "127.0.0.1", "test");

        Assert.That(logs.Logs, Has.Count.EqualTo(1));
        var log = logs.Logs.Single();
        Assert.Multiple(() =>
        {
            Assert.That(log.OrderId, Is.EqualTo(created.Id));
            Assert.That(log.OrderCode, Is.EqualTo(created.OrderCode));
            Assert.That(log.SelectedDeadlineDate, Is.EqualTo(created.RequestedDeliveryDate));
            Assert.That(log.FinalRecommendedDeadlineDate, Is.EqualTo(new DateOnly(2026, 6, 4)));
            Assert.That(log.CalculatedOrderCapacityUnits, Is.EqualTo(created.CalculatedCapacityUnits));
            Assert.That(log.CandidateChecksJson, Does.Contain("accepted"));
            Assert.That(log.ConfigSnapshotJson, Does.Contain("materialConfig"));
        });
    }

    [Test]
    public async Task UpdateOrderAsync_AppendsAnotherDeadlineRecommendationLog()
    {
        var logs = new CapturingDeadlineRecommendationLogRepository();
        var fixture = new Fixture(["LOG-1", "LOG-2"], deadlineRecommendationLogs: logs);
        var created = await fixture.Service.CreateOrderAsync(TestActors.Demo, fixture.CreateOrderDraft("Logged"), "127.0.0.1", "test");

        var updated = await fixture.Service.UpdateOrderAsync(TestActors.Demo, created.OrderCode, fixture.CreateOrderDraft("Logged update") with
        {
            RequestedDeliveryDate = new DateOnly(2026, 6, 9)
        });

        Assert.That(logs.Logs, Has.Count.EqualTo(2));
        Assert.That(logs.Logs.Select(l => l.OrderCode), Is.All.EqualTo(created.OrderCode));
        Assert.That(logs.Logs.Last().SelectedDeadlineDate, Is.EqualTo(updated.RequestedDeliveryDate));
    }

    [Test]
    public async Task RejectedCreate_DoesNotPersistDeadlineRecommendationLog()
    {
        var logs = new CapturingDeadlineRecommendationLogRepository();
        var fixture = new Fixture(
            ["LOG-1", "LOG-2"],
            deadlineRecommendationLogs: logs,
            capacityConfigs: [new SchedulingCapacityConfig(1, new DateOnly(2026, 1, 1), 1m, 10m)]);
        await fixture.Service.CreateOrderAsync(TestActors.Demo, fixture.CreateOrderDraft("Existing") with
        {
            Material = Material.Pmma,
            ProductCategory = ProductCategory.Temporary,
            RequestedDeliveryDate = new DateOnly(2026, 6, 4)
        }, "127.0.0.1", "test");
        logs.Clear();

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await fixture.Service.CreateOrderAsync(TestActors.Demo, fixture.CreateOrderDraft("Rejected") with
            {
                Material = Material.Pmma,
                ProductCategory = ProductCategory.Temporary,
                RequestedDeliveryDate = new DateOnly(2026, 6, 4)
            }, "127.0.0.1", "test"));
        Assert.That(logs.Logs, Is.Empty);
    }

    [Test]
    public async Task CreateOrderAsync_AppendsOrderCreatedAuditAfterPersistence()
    {
        var audit = new CapturingAuditLog();
        var fixture = new Fixture(1, auditLog: audit);

        var created = await fixture.Service.CreateOrderAsync(TestActors.Lab, fixture.CreateOrderDraft("Audit create"), "127.0.0.1", "test-agent", "OTHER");

        Assert.That(audit.Events, Has.Count.EqualTo(1));
        var evt = audit.Events[0];
        Assert.That(evt.Operation, Is.EqualTo("OrderCreated"));
        Assert.That(evt.EntityType, Is.EqualTo("SchedulingOrder"));
        Assert.That(evt.EntityId, Is.EqualTo(created.OrderCode));
        Assert.That(evt.ActorOrganizationType, Is.EqualTo("Lab"));
        Assert.That(evt.ActorOrganizationCode, Is.EqualTo(TestActors.Lab.OrganizationCode));
        Assert.That(evt.ActorMemberId, Is.EqualTo(TestActors.Lab.MemberId));
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
    public async Task CreateAndUpdateOrderAsync_TrimsAndPersistsColorNote()
    {
        var audit = new CapturingAuditLog();
        var fixture = new Fixture(1, auditLog: audit);

        var created = await fixture.Service.CreateOrderAsync(TestActors.Demo, fixture.CreateOrderDraft("Color note") with
        {
            ColorNote = "  cervical third slightly warmer  "
        }, "127.0.0.1", "test");
        var updated = await fixture.Service.UpdateOrderAsync(TestActors.Demo, created.OrderCode, fixture.CreateOrderDraft("Color note") with
        {
            ColorNote = "  incisal edge translucent  "
        }, "10.0.0.1", "ua-edit");

        Assert.That(created.ColorNote, Is.EqualTo("cervical third slightly warmer"));
        Assert.That(updated.ColorNote, Is.EqualTo("incisal edge translucent"));
        Assert.That(audit.Events[1].MetadataJson, Does.Contain("ColorNote"));
    }

    [Test]
    public async Task UpdateOrderAsync_GivenClinicOwnOrder_UpdatesFields()
    {
        var fixture = new Fixture(1);
        var created = await fixture.Service.CreateOrderAsync(TestActors.Demo, fixture.CreateOrderDraft("Original"), "127.0.0.1", "test");
        var draft = fixture.CreateOrderDraft("Updated case") with
        {
            WorkItems = [new OrderWorkItem(ConstructionType.Crown, new ToothRange(12, 12))],
            RequestedDeliveryDate = new DateOnly(2026, 6, 9),
            Notes = "updated note"
        };

        var updated = await fixture.Service.UpdateOrderAsync(TestActors.Demo, created.OrderCode, draft);

        Assert.That(updated.OrderCode, Is.EqualTo(created.OrderCode));
        Assert.That(updated.CaseName, Is.EqualTo("Updated case"));
        Assert.That(updated.WorkItems.Single().ToothStart, Is.EqualTo(12));
        Assert.That(updated.Notes, Is.EqualTo("updated note"));
        Assert.That(updated.CreatedAt, Is.EqualTo(created.CreatedAt));
        Assert.That(updated.Status, Is.EqualTo(OrderStatus.Created));
    }

    [Test]
    public async Task UpdateOrderAsync_GivenClinicOtherOrder_ReturnsNotFound()
    {
        var fixture = new Fixture(1);
        var created = await fixture.Service.CreateOrderAsync(TestActors.Lab, fixture.CreateOrderDraft("Other"), "127.0.0.1", "test", "OTHER");

        Assert.ThrowsAsync<KeyNotFoundException>(async () =>
            await fixture.Service.UpdateOrderAsync(TestActors.Demo, created.OrderCode, fixture.CreateOrderDraft("Nope")));
    }

    [Test]
    public async Task UpdateOrderAsync_GivenLabAnyOrder_UpdatesFields()
    {
        var fixture = new Fixture(1);
        var created = await fixture.Service.CreateOrderAsync(TestActors.Demo, fixture.CreateOrderDraft("Original"), "127.0.0.1", "test");

        var updated = await fixture.Service.UpdateOrderAsync(TestActors.Lab, created.OrderCode, fixture.CreateOrderDraft("Tech edit"));

        Assert.That(updated.CaseName, Is.EqualTo("Tech edit"));
    }

    [Test]
    public async Task UpdateOrderAsync_ValidatesLeadTimeAgainstExistingOrderCreatedAt()
    {
        var clock = new MutableClock(new DateTimeOffset(2026, 6, 2, 7, 30, 0, TimeSpan.Zero));
        var fixture = new Fixture(["A"], clock: clock);
        var created = await fixture.Service.CreateOrderAsync(TestActors.Demo, fixture.CreateOrderDraft("Original"), "127.0.0.1", "test");
        clock.UtcNow = new DateTimeOffset(2026, 6, 3, 8, 30, 0, TimeSpan.Zero);

        var updated = await fixture.Service.UpdateOrderAsync(TestActors.Demo, created.OrderCode, fixture.CreateOrderDraft("Still valid") with
        {
            RequestedDeliveryDate = new DateOnly(2026, 6, 5)
        });

        Assert.That(updated.RequestedDeliveryDate, Is.EqualTo(new DateOnly(2026, 6, 5)));
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
    public async Task CreateOrderAsync_GivenLabTargetClinic_CreatesForTargetClinicWithLabMember()
    {
        var fixture = new Fixture(1);

        var created = await fixture.Service.CreateOrderAsync(TestActors.Lab, fixture.CreateOrderDraft("For other"), "127.0.0.1", "test", "OTHER");

        Assert.That(created.ClinicCode, Is.EqualTo("OTHER"));
        Assert.That(created.ClinicDisplayName, Is.EqualTo("Other Clinic"));
        Assert.That(created.MemberId, Is.EqualTo(TestActors.Lab.MemberId));
    }

    [Test]
    public void CreateOrderAsync_GivenClinicTargetingOtherClinic_Rejects()
    {
        var fixture = new Fixture(1);

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await fixture.Service.CreateOrderAsync(TestActors.Demo, fixture.CreateOrderDraft("Bad target"), "127.0.0.1", "test", "OTHER"));
    }

    [Test]
    public void CreateOrderAsync_GivenLabWithoutTargetClinic_Rejects()
    {
        var fixture = new Fixture(1);

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await fixture.Service.CreateOrderAsync(TestActors.Lab, fixture.CreateOrderDraft("Missing target"), "127.0.0.1", "test"));
    }

    [Test]
    public async Task ListOrdersPageForActorAsync_ReturnsPagedActorScopedOrders()
    {
        var fixture = new Fixture(["A-1", "A-2", "A-3", "B-1"]);
        await fixture.Service.CreateOrderAsync(TestActors.Demo, fixture.CreateOrderDraft("old") with
        {
            RequestedDeliveryDate = new DateOnly(2026, 6, 5)
        }, "127.0.0.1", "test");
        await fixture.Service.CreateOrderAsync(TestActors.Demo, fixture.CreateOrderDraft("new") with
        {
            RequestedDeliveryDate = new DateOnly(2026, 6, 9)
        }, "127.0.0.1", "test");
        await fixture.Service.CreateOrderAsync(TestActors.Lab, fixture.CreateOrderDraft("other") with
        {
            RequestedDeliveryDate = new DateOnly(2026, 6, 10)
        }, "127.0.0.1", "test", "OTHER");

        var first = await fixture.Service.ListOrdersPageForActorAsync(TestActors.Demo, 1);
        var second = await fixture.Service.ListOrdersPageForActorAsync(TestActors.Demo, 1, first.NextCursor);
        var tech = await fixture.Service.ListOrdersPageForActorAsync(TestActors.Lab, 10);

        Assert.That(first.Items.Select(o => o.OrderCode), Is.EqualTo(new[] { "A-2" }));
        Assert.That(first.HasMore, Is.True);
        Assert.That(second.Items.Select(o => o.OrderCode), Is.EqualTo(new[] { "A-1" }));
        Assert.That(tech.Items.Select(o => o.OrderCode), Does.Contain("A-3"));
    }

    [Test]
    public async Task FindOrderContextForActorAsync_RespectsScopeShortCodesAndCancelledListRecommendation()
    {
        var fixture = new Fixture(["26-0605-Z1AA", "27-0605-Z1AA", "26-0606-Z1BB"]);
        var own = await fixture.Service.CreateOrderAsync(TestActors.Demo, fixture.CreateOrderDraft("own"), "127.0.0.1", "test");
        await fixture.Service.CreateOrderAsync(TestActors.Lab, fixture.CreateOrderDraft("other same short") with
        {
            RequestedDeliveryDate = new DateOnly(2027, 6, 8)
        }, "127.0.0.1", "test", "OTHER");
        var cancelled = await fixture.Service.CreateOrderAsync(TestActors.Demo, fixture.CreateOrderDraft("cancelled") with
        {
            RequestedDeliveryDate = new DateOnly(2026, 6, 10)
        }, "127.0.0.1", "test");
        await fixture.Service.CancelOrderAsync(TestActors.Demo, cancelled.OrderCode);

        var ownByShort = await fixture.Service.FindOrderContextForActorAsync(TestActors.Demo, own.OrderCode[3..], 2);
        var cancelledResult = await fixture.Service.FindOrderContextForActorAsync(TestActors.Demo, cancelled.OrderCode, 2);

        Assert.That(ownByShort.Order.OrderCode, Is.EqualTo(own.OrderCode));
        Assert.That(ownByShort.ListPage.Items.Select(o => o.OrderCode), Does.Contain(own.OrderCode));
        Assert.ThrowsAsync<KeyNotFoundException>(async () =>
            await fixture.Service.FindOrderContextForActorAsync(TestActors.Demo, "27-0605-Z1AA", 2));
        Assert.ThrowsAsync<AmbiguousOrderCodeException>(async () =>
            await fixture.Service.FindOrderContextForActorAsync(TestActors.Lab, own.OrderCode[3..], 2));
        Assert.That(cancelledResult.ListModeRecommended, Is.True);
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
        var other = await fixture.Service.CreateOrderAsync(TestActors.Lab, fixture.CreateOrderDraft("Other") with
        {
            RequestedDeliveryDate = new DateOnly(2026, 6, 10)
        }, "127.0.0.1", "test", "OTHER");
        var cancelled = await fixture.Service.CreateOrderAsync(TestActors.Demo, fixture.CreateOrderDraft("Cancelled") with
        {
            RequestedDeliveryDate = new DateOnly(2026, 6, 10)
        }, "127.0.0.1", "test");
        await fixture.Service.CancelOrderAsync(TestActors.Demo, cancelled.OrderCode);

        var clinicOrders = await fixture.Service.ListCalendarOrdersAsync(TestActors.Demo, new DateOnly(2026, 6, 5), new DateOnly(2026, 6, 10));
        var techOrders = await fixture.Service.ListCalendarOrdersAsync(TestActors.Lab, new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 10));

        Assert.That(clinicOrders.Select(o => o.OrderCode), Is.EquivalentTo(new[] { demoEarly.OrderCode, demoLate.OrderCode }));
        Assert.That(techOrders.Select(o => o.OrderCode), Is.EquivalentTo(new[] { demoLate.OrderCode, other.OrderCode }));
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await fixture.Service.ListCalendarOrdersAsync(TestActors.Demo, new DateOnly(2026, 6, 11), new DateOnly(2026, 6, 10)));
    }

    [Test]
    public async Task CreateOrderAsync_GivenMultipleOrderWorkItems_PersistsAllItemsOnly()
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
        Assert.That(created.WorkItems[0].ConstructionType, Is.EqualTo(ConstructionType.Bridge));
        Assert.That(created.WorkItems[0].ToothStart, Is.EqualTo(13));
        Assert.That(created.WorkItems[0].ToothEnd, Is.EqualTo(11));
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
            fixture.CreateOrderDraft("cross jaw") with { WorkItems = [new OrderWorkItem(ConstructionType.InlayOverlay, new ToothRange(28, 31))] },
            fixture.CreateOrderDraft("overlap") with { WorkItems = [new OrderWorkItem(ConstructionType.Bridge, new ToothRange(11, 13)), new OrderWorkItem(ConstructionType.Crown, new ToothRange(12, 12))] }
        };

        foreach (var draft in cases)
        {
            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await fixture.Service.CreateOrderAsync(TestActors.Demo, draft, "127.0.0.1", "test"));
        }
    }

    [Test]
    public async Task CalculateMinimumDeliveryDateAsync_UsesMaterialLeadTimeAndDistinctTeethInsteadOfWorkRuleSum()
    {
        var fixture = new Fixture(["A"]);
        var draft = fixture.CreateOrderDraft("Lead") with
        {
            Material = Material.Pfm,
            WorkItems =
            [
                new OrderWorkItem(ConstructionType.Bridge, new ToothRange(18, 24))
            ],
            RequestedDeliveryDate = new DateOnly(2026, 6, 9)
        };

        var minimum = await fixture.Service.CalculateMinimumDeliveryDateAsync(draft);

        Assert.That(OrderWorkItem.AllTeeth(draft.WorkItems), Has.Length.EqualTo(12));
        Assert.That(minimum, Is.EqualTo(new DateOnly(2026, 6, 9)));
    }

    [Test]
    public async Task CreateOrderAsync_GivenLabBeforeMinimumDeliveryDate_AllowsSelection()
    {
        var fixture = new Fixture(1);
        var minimum = await fixture.Service.CalculateMinimumDeliveryDateAsync(fixture.CreateOrderDraft("Test"));
        var earlyDate = minimum.AddDays(-1);
        while (DateAvailabilityService.IsWeekend(earlyDate))
            earlyDate = earlyDate.AddDays(-1);

        var draft = fixture.CreateOrderDraft("Early lab") with { RequestedDeliveryDate = earlyDate };
        var created = await fixture.Service.CreateOrderAsync(TestActors.Lab, draft, "127.0.0.1", "test", "OTHER");

        Assert.That(created.RequestedDeliveryDate, Is.EqualTo(earlyDate));
    }

    [Test]
    public async Task CreateAndUpdateOrderAsync_PersistsCalculatedCapacityUnits()
    {
        var fixture = new Fixture(
            ["CAP-001", "CAP-002"],
            materialConfigs:
            [
                TestMaterialSchedulingConfigProvider.DefaultConfig(Material.FullContourZirconia) with { CapacityUnitsPerTooth = 1.5m }
            ]);

        var created = await fixture.Service.CreateOrderAsync(TestActors.Demo, fixture.CreateOrderDraft("Capacity") with
        {
            WorkItems =
            [
                new OrderWorkItem(ConstructionType.Bridge, new ToothRange(11, 13)),
                new OrderWorkItem(ConstructionType.Crown, new ToothRange(21, 21))
            ],
            RequestedDeliveryDate = new DateOnly(2026, 6, 10)
        }, "127.0.0.1", "test");
        var updated = await fixture.Service.UpdateOrderAsync(TestActors.Demo, created.OrderCode, fixture.CreateOrderDraft("Capacity") with
        {
            WorkItems =
            [
                new OrderWorkItem(ConstructionType.Crown, new ToothRange(11, 11)),
                new OrderWorkItem(ConstructionType.Crown, new ToothRange(12, 12))
            ],
            RequestedDeliveryDate = new DateOnly(2026, 6, 10)
        });

        Assert.That(created.CalculatedCapacityUnits, Is.EqualTo(6.0m));
        Assert.That(updated.CalculatedCapacityUnits, Is.EqualTo(3.0m));
    }

    [Test]
    public async Task CreateOrderAsync_GivenClinicBeforeMinimumDeliveryDate_Rejects()
    {
        var fixture = new Fixture(1);
        var minimum = await fixture.Service.CalculateMinimumDeliveryDateAsync(fixture.CreateOrderDraft("Test"));
        var earlyDate = minimum.AddDays(-1);
        while (DateAvailabilityService.IsWeekend(earlyDate))
            earlyDate = earlyDate.AddDays(-1);

        var draft = fixture.CreateOrderDraft("Early clinic") with { RequestedDeliveryDate = earlyDate };

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await fixture.Service.CreateOrderAsync(TestActors.Demo, draft, "127.0.0.1", "test"));
    }

    [Test]
    public void CreateOrderAsync_GivenLabClosedOrFirstAfterClosureDate_Rejects()
    {
        var fixture = new Fixture(["A", "B"]);

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await fixture.Service.CreateOrderAsync(
                TestActors.Lab,
                fixture.CreateOrderDraft("Closed") with { RequestedDeliveryDate = new DateOnly(2026, 6, 6) },
                "127.0.0.1",
                "test",
                "OTHER"));
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await fixture.Service.CreateOrderAsync(
                TestActors.Lab,
                fixture.CreateOrderDraft("First after closure") with { RequestedDeliveryDate = new DateOnly(2026, 6, 1) },
                "127.0.0.1",
                "test",
                "OTHER"));
    }

    [Test]
    public async Task CreateOrderAsync_GivenLabBeforeMinimumAndCapacityExceeded_Rejects()
    {
        var fixture = new Fixture(
            ["A", "B", "C"],
            capacityConfigs: [new SchedulingCapacityConfig(1, new DateOnly(2026, 1, 1), 1m, 10m)]);
        var minimum = await fixture.Service.CalculateMinimumDeliveryDateAsync(fixture.CreateOrderDraft("Test"));
        var earlyDate = minimum.AddDays(-1);
        while (DateAvailabilityService.IsWeekend(earlyDate))
            earlyDate = earlyDate.AddDays(-1);

        await fixture.Service.CreateOrderAsync(
            TestActors.Lab,
            fixture.CreateOrderDraft("Existing early") with { RequestedDeliveryDate = earlyDate },
            "127.0.0.1",
            "test",
            "OTHER");

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await fixture.Service.CreateOrderAsync(
                TestActors.Lab,
                fixture.CreateOrderDraft("Blocked early") with { RequestedDeliveryDate = earlyDate },
                "127.0.0.1",
                "test",
                "OTHER"));
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
    public async Task CreateOrderAsync_RevalidatesInsideSerializedWriteTransactionAgainstLatestCapacity()
    {
        var fixture = new Fixture(
            ["A", "B"],
            capacityConfigs: [new SchedulingCapacityConfig(1, new DateOnly(2026, 1, 1), 1m, 10m)]);
        var draft = fixture.CreateOrderDraft("Stale preview") with
        {
            Material = Material.Pmma,
            ProductCategory = ProductCategory.Temporary,
            ImpressionDate = new DateOnly(2026, 6, 8),
            RequestedDeliveryDate = new DateOnly(2026, 6, 10)
        };

        var preview = await fixture.Service.GetDateStatusesAsync(draft, new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 10), new DateTimeOffset(2026, 6, 8, 7, 30, 0, TimeSpan.Zero));
        Assert.That(preview.Single().IsSelectable, Is.True);

        fixture.WriteTransaction.SetBeforeOperation(orders => orders.CreateOrderAsync(BuildSeedOrder("COMP-1", new DateOnly(2026, 6, 10), Material.Pmma, ProductCategory.Temporary)));

        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await fixture.Service.CreateOrderAsync(TestActors.Demo, draft, "127.0.0.1", "test"));

        Assert.That(ex!.Message, Does.Contain("Daily capacity exceeded"));
        Assert.That((await fixture.Repository.ListOrdersAsync()).Select(o => o.OrderCode), Is.EqualTo(new[] { "COMP-1" }));
    }

    [Test]
    public async Task CreateOrderAsync_ConcurrentCreatesForLastCapacitySlot_OnlyOneSucceeds()
    {
        var fixture = new Fixture(
            ["A", "B", "C"],
            capacityConfigs: [new SchedulingCapacityConfig(1, new DateOnly(2026, 1, 1), 1m, 10m)]);
        var draft = fixture.CreateOrderDraft("Race") with
        {
            Material = Material.Pmma,
            ProductCategory = ProductCategory.Temporary,
            ImpressionDate = new DateOnly(2026, 6, 8),
            RequestedDeliveryDate = new DateOnly(2026, 6, 10)
        };
        using var barrier = new Barrier(2);

        var tasks = Enumerable.Range(0, 2).Select(i => Task.Run(async () =>
        {
            barrier.SignalAndWait();
            try
            {
                var order = await fixture.Service.CreateOrderAsync(TestActors.Demo, draft with { CaseName = $"Race {i}" }, "127.0.0.1", $"test-{i}");
                return (Order: order, Error: (Exception?)null);
            }
            catch (Exception ex)
            {
                return (Order: (OrderRecord?)null, Error: ex);
            }
        })).ToArray();

        var outcomes = await Task.WhenAll(tasks);

        Assert.That(outcomes.Count(o => o.Order != null), Is.EqualTo(1));
        Assert.That(outcomes.Count(o => o.Error is InvalidOperationException), Is.EqualTo(1));
        Assert.That(outcomes.Single(o => o.Error != null).Error!.Message, Does.Contain("Daily capacity exceeded"));
        Assert.That((await fixture.Repository.ListOrdersAsync()).Count, Is.EqualTo(1));
    }

    [Test]
    public async Task UpdateOrderAsync_ReFetchesInsideSerializedWriteTransactionAndRejectsLatestCapacity()
    {
        var fixture = new Fixture(
            ["A", "B", "C"],
            capacityConfigs: [new SchedulingCapacityConfig(1, new DateOnly(2026, 1, 1), 1m, 10m)]);
        var existing = await fixture.Service.CreateOrderAsync(TestActors.Demo, fixture.CreateOrderDraft("Editable") with
        {
            Material = Material.Pmma,
            ProductCategory = ProductCategory.Temporary,
            ImpressionDate = new DateOnly(2026, 6, 8),
            RequestedDeliveryDate = new DateOnly(2026, 6, 11)
        }, "127.0.0.1", "test");

        fixture.WriteTransaction.SetBeforeOperation(orders => orders.CreateOrderAsync(BuildSeedOrder("COMP-2", new DateOnly(2026, 6, 10), Material.Pmma, ProductCategory.Temporary)));

        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await fixture.Service.UpdateOrderAsync(TestActors.Demo, existing.OrderCode, fixture.CreateOrderDraft("Editable") with
            {
                Material = Material.Pmma,
                ProductCategory = ProductCategory.Temporary,
                ImpressionDate = new DateOnly(2026, 6, 8),
                RequestedDeliveryDate = new DateOnly(2026, 6, 10)
            }));

        Assert.That(ex!.Message, Does.Contain("Daily capacity exceeded"));
        Assert.That((await fixture.Repository.GetOrderByCodeAsync(existing.OrderCode))!.RequestedDeliveryDate, Is.EqualTo(new DateOnly(2026, 6, 11)));
    }

    [Test]
    public async Task RejectedCommitTimeValidation_DoesNotAppendCreateOrUpdateAuditEvents()
    {
        var audit = new CapturingAuditLog();
        var fixture = new Fixture(
            ["A", "B", "C"],
            auditLog: audit,
            capacityConfigs: [new SchedulingCapacityConfig(1, new DateOnly(2026, 1, 1), 1m, 10m)]);
        var createDraft = fixture.CreateOrderDraft("Create blocked") with
        {
            Material = Material.Pmma,
            ProductCategory = ProductCategory.Temporary,
            ImpressionDate = new DateOnly(2026, 6, 8),
            RequestedDeliveryDate = new DateOnly(2026, 6, 10)
        };

        fixture.WriteTransaction.SetBeforeOperation(orders => orders.CreateOrderAsync(BuildSeedOrder("COMP-3", new DateOnly(2026, 6, 10), Material.Pmma, ProductCategory.Temporary)));
        Assert.ThrowsAsync<InvalidOperationException>(async () => await fixture.Service.CreateOrderAsync(TestActors.Demo, createDraft, "127.0.0.1", "test"));
        Assert.That(audit.Events, Is.Empty);

        var existing = await fixture.Service.CreateOrderAsync(TestActors.Demo, fixture.CreateOrderDraft("Editable") with
        {
            Material = Material.Pmma,
            ProductCategory = ProductCategory.Temporary,
            ImpressionDate = new DateOnly(2026, 6, 8),
            RequestedDeliveryDate = new DateOnly(2026, 6, 11)
        }, "127.0.0.1", "test");
        Assert.That(audit.Events.Select(e => e.Operation), Is.EqualTo(new[] { "OrderCreated" }));

        fixture.WriteTransaction.SetBeforeOperation(orders => orders.CreateOrderAsync(BuildSeedOrder("COMP-4", new DateOnly(2026, 6, 12), Material.Pmma, ProductCategory.Temporary)));
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await fixture.Service.UpdateOrderAsync(TestActors.Demo, existing.OrderCode, fixture.CreateOrderDraft("Editable") with
            {
                Material = Material.Pmma,
                ProductCategory = ProductCategory.Temporary,
                ImpressionDate = new DateOnly(2026, 6, 8),
                RequestedDeliveryDate = new DateOnly(2026, 6, 12)
            }));
        Assert.That(audit.Events.Select(e => e.Operation), Is.EqualTo(new[] { "OrderCreated" }));
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

    private static OrderRecord BuildSeedOrder(string code, DateOnly requestedDeliveryDate, Material material, ProductCategory productCategory) =>
        new(
            0,
            code,
            "DEMO",
            "Demo",
            "seed",
            "Seed",
            "fingerprint",
            code,
            new DateOnly(2026, 6, 8),
            productCategory,
            material,
            [new OrderWorkItem(ConstructionType.Crown, new ToothRange(11, 11))],
            requestedDeliveryDate,
            OrderStatus.Created,
            Shade.Unspecified,
            null,
            DateTimeOffset.Parse("2026-06-08T07:30:00Z"),
            DateTimeOffset.Parse("2026-06-08T07:30:00Z"),
            "127.0.0.1",
            "test",
            null,
            1.0m);

    private sealed class CapturingDeadlineRecommendationLogRepository : IDeadlineRecommendationLogRepository
    {
        private readonly object _gate = new();
        private readonly List<DeadlineRecommendationLog> _logs = new();
        private long _nextId = 1;

        public IReadOnlyList<DeadlineRecommendationLog> Logs
        {
            get
            {
                lock (_gate)
                    return _logs.ToList();
            }
        }

        public Task<DeadlineRecommendationLog> AddAsync(DeadlineRecommendationLog log, CancellationToken ct = default)
        {
            lock (_gate)
            {
                var saved = log with { Id = _nextId++ };
                _logs.Add(saved);
                return Task.FromResult(saved);
            }
        }

        public Task<IReadOnlyList<DeadlineRecommendationLog>> ListForOrderAsync(long orderId, CancellationToken ct = default)
        {
            lock (_gate)
                return Task.FromResult<IReadOnlyList<DeadlineRecommendationLog>>(_logs.Where(l => l.OrderId == orderId).OrderByDescending(l => l.CreatedAtUtc).ThenByDescending(l => l.Id).ToList());
        }

        public void Clear()
        {
            lock (_gate)
                _logs.Clear();
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
        public InMemorySchedulingWriteTransaction WriteTransaction { get; }

        public Fixture(int racingOrderCount, int maxOrderCodeAttempts = 20, IAuditLog? auditLog = null, IReadOnlyList<MaterialSchedulingConfig>? materialConfigs = null, IReadOnlyList<SchedulingCapacityConfig>? capacityConfigs = null, IDeadlineRecommendationLogRepository? deadlineRecommendationLogs = null)
            : this(Enumerable
                .Repeat("racecode", racingOrderCount) // race all on the first code, only 1st should win this code
                .Concat(Enumerable.Range(2, racingOrderCount - 1) // generate unique code for each that didn't win 1st race
                    .Select(i => $"R{i:00}-XYZ")
                    .ToArray()).ToArray(),
                maxOrderCodeAttempts,
                auditLog,
                clock: null,
                materialConfigs,
                capacityConfigs,
                deadlineRecommendationLogs)
        {
        }

        public Fixture(
            IReadOnlyList<string> generatorCodes,
            int maxOrderCodeAttempts = 20,
            IAuditLog? auditLog = null,
            IClock? clock = null,
            IReadOnlyList<MaterialSchedulingConfig>? materialConfigs = null,
            IReadOnlyList<SchedulingCapacityConfig>? capacityConfigs = null,
            IDeadlineRecommendationLogRepository? deadlineRecommendationLogs = null)
        {
            _generatorCodes = generatorCodes.ToList();
            Repository = new InMemoryOrderRepository();
            WriteTransaction = new InMemorySchedulingWriteTransaction(Repository);
            var dateAvailabilityService = new DateAvailabilityService(new WeekendOnlyNonWorkingDayProvider());
            var deadlineRecommendationService = new DeadlineRecommendationService(
                dateAvailabilityService,
                new TestMaterialSchedulingConfigProvider(materialConfigs),
                new TestSchedulingCapacityConfigProvider(capacityConfigs),
                Repository);
            var orderCodeGenerator = new SequenceOrderCodeGenerator(_generatorCodes.ToArray());
            clock ??= new FixedClock(new DateTimeOffset(2026, 5, 31, 12, 0, 0, TimeSpan.Zero));
            Service = new SchedulingOrderService(
                new InMemorySchedulingIdentityRepository(),
                Repository,
                dateAvailabilityService,
                deadlineRecommendationService,
                WriteTransaction,
                orderCodeGenerator,
                clock,
                maxOrderCodeAttempts,
                auditLog,
                deadlineRecommendationLogs);
        }

        public SchedulingOrderService Service { get; }

        public OrderDraft CreateOrderDraft(string caseName) =>
            new(
                caseName,
                new DateOnly(2026, 6, 2),
                ProductCategory.Permanent,
                Material.FullContourZirconia,
                [new OrderWorkItem(ConstructionType.Crown, new ToothRange(11, 11))],
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
                    return Task.FromResult<IReadOnlyList<OrderRecord>>(OrderedScoped(null).Take(limit).ToList());
            }

            public Task<IReadOnlyList<OrderRecord>> ListOrdersForClinicAsync(string clinicCode, int limit = 100, CancellationToken ct = default)
            {
                lock (_gate)
                    return Task.FromResult<IReadOnlyList<OrderRecord>>(OrderedScoped(clinicCode).Take(limit).ToList());
            }

            public Task<OrderPage> ListOrdersPageAsync(string? clinicCode, int limit, OrderCursor? cursor, CancellationToken ct = default)
            {
                lock (_gate)
                {
                    var query = OrderedScoped(clinicCode);
                    if (cursor != null)
                    {
                        query = query.Where(o =>
                            o.RequestedDeliveryDate < cursor.RequestedDeliveryDate
                            || (o.RequestedDeliveryDate == cursor.RequestedDeliveryDate && o.CreatedAt.ToUnixTimeMilliseconds() < cursor.CreatedAtUnixTimeMilliseconds)
                            || (o.RequestedDeliveryDate == cursor.RequestedDeliveryDate && o.CreatedAt.ToUnixTimeMilliseconds() == cursor.CreatedAtUnixTimeMilliseconds && o.Id < cursor.Id));
                    }
                    return Task.FromResult(MakePage(query, limit));
                }
            }

            public Task<OrderPage> ListOrdersPageContainingOrderAsync(string? clinicCode, OrderRecord target, int limit, CancellationToken ct = default)
            {
                lock (_gate)
                {
                    var query = OrderedScoped(clinicCode).ToList();
                    var index = query.FindIndex(o => o.Id == target.Id);
                    var start = index < 0 ? 0 : index / Math.Max(1, limit) * Math.Max(1, limit);
                    return Task.FromResult(MakePage(query.Skip(start), limit));
                }
            }

            public Task<IReadOnlyList<OrderRecord>> FindOrdersByCodeSuffixAsync(string? clinicCode, string codeSuffix, int limit = 2, CancellationToken ct = default)
            {
                lock (_gate)
                    return Task.FromResult<IReadOnlyList<OrderRecord>>(OrderedScoped(clinicCode)
                        .Where(o => o.OrderCode.EndsWith(codeSuffix, StringComparison.OrdinalIgnoreCase))
                        .Take(Math.Clamp(limit, 1, 20))
                        .ToList());
            }

            private IEnumerable<OrderRecord> OrderedScoped(string? clinicCode) => _ordersByCode.Values
                .Where(o => clinicCode == null || string.Equals(o.ClinicCode, clinicCode, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(o => o.RequestedDeliveryDate)
                .ThenByDescending(o => o.CreatedAt.ToUnixTimeMilliseconds())
                .ThenByDescending(o => o.Id);

            private static OrderPage MakePage(IEnumerable<OrderRecord> source, int limit)
            {
                var safeLimit = Math.Clamp(limit, 1, 100);
                var rows = source.Take(safeLimit + 1).ToList();
                var hasMore = rows.Count > safeLimit;
                var items = hasMore ? rows.Take(safeLimit).ToList() : rows;
                var nextCursor = hasMore && items.Count > 0 ? OrderCursorCodec.Encode(OrderCursor.FromOrder(items[^1])) : null;
                return new OrderPage(items, nextCursor, hasMore);
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

            public Task<IReadOnlyList<OrderRecord>> ListActiveOrdersByDeadlineRangeAsync(DateOnly start, DateOnly end, CancellationToken ct = default)
            {
                lock (_gate)
                    return Task.FromResult<IReadOnlyList<OrderRecord>>(
                        _ordersByCode.Values
                            .Where(o => o.Status != OrderStatus.Cancelled
                                && o.RequestedDeliveryDate >= start
                                && o.RequestedDeliveryDate <= end)
                            .OrderBy(o => o.RequestedDeliveryDate)
                            .ThenBy(o => o.Id)
                            .ToList());
            }
        }
    }
}