using Orders;

namespace Orders.Tests;

[Parallelizable(ParallelScope.All)]
public class DeadlineRecommendationServiceTest
{
    [Test]
    public async Task RecommendAsync_GivenPmmaBeforeCutoff_ReturnsThursday()
    {
        var service = CreateService();
        var createdAtUtc = SofiaLocal(2026, 6, 2, 10, 30);

        var result = await service.RecommendAsync(Input(Material.Pmma, createdAtUtc));

        Assert.That(result.EffectiveIntakeBusinessDate, Is.EqualTo(new DateOnly(2026, 6, 2)));
        Assert.That(result.LeadTimeBusinessDays, Is.EqualTo(2));
        Assert.That(result.EarliestSelectableDeadline, Is.EqualTo(new DateOnly(2026, 6, 4)));
    }

    [Test]
    public async Task RecommendAsync_GivenPmmaAfterCutoff_ReturnsFriday()
    {
        var service = CreateService();
        var createdAtUtc = SofiaLocal(2026, 6, 2, 11, 30);

        var result = await service.RecommendAsync(Input(Material.Pmma, createdAtUtc));

        Assert.That(result.EffectiveIntakeBusinessDate, Is.EqualTo(new DateOnly(2026, 6, 3)));
        Assert.That(result.EarliestSelectableDeadline, Is.EqualTo(new DateOnly(2026, 6, 5)));
    }

    [Test]
    public async Task RecommendAsync_GivenOrderCreatedSaturday_UsesMondayIntakeAndWednesdayDeadline()
    {
        var service = CreateService();
        var createdAtUtc = SofiaLocal(2026, 6, 6, 10, 30);

        var result = await service.RecommendAsync(Input(Material.Pmma, createdAtUtc));

        Assert.That(result.EffectiveIntakeBusinessDate, Is.EqualTo(new DateOnly(2026, 6, 8)));
        Assert.That(result.EarliestSelectableDeadline, Is.EqualTo(new DateOnly(2026, 6, 10)));
    }

    [Test]
    public async Task RecommendAsync_GivenHolidayDuringLeadTime_SkipsHolidayForCounting()
    {
        var service = CreateService([new DateOnly(2026, 6, 3)]);
        var createdAtUtc = SofiaLocal(2026, 6, 2, 10, 30);

        var result = await service.RecommendAsync(Input(Material.Pmma, createdAtUtc));

        Assert.That(result.EffectiveIntakeBusinessDate, Is.EqualTo(new DateOnly(2026, 6, 2)));
        Assert.That(result.PostLeadTimeCandidateDate, Is.EqualTo(new DateOnly(2026, 6, 5)));
        Assert.That(result.EarliestSelectableDeadline, Is.EqualTo(new DateOnly(2026, 6, 5)));
    }

    [Test]
    public async Task RecommendAsync_GivenFridayIntake_CountsMondayAsLeadTimeButNotSelectableDeadline()
    {
        var service = CreateService();
        var createdAtUtc = SofiaLocal(2026, 6, 5, 10, 30);

        var result = await service.RecommendAsync(Input(Material.Pmma, createdAtUtc));

        Assert.That(result.EffectiveIntakeBusinessDate, Is.EqualTo(new DateOnly(2026, 6, 5)));
        Assert.That(result.PostLeadTimeCandidateDate, Is.EqualTo(new DateOnly(2026, 6, 9)));
        Assert.That(result.EarliestSelectableDeadline, Is.EqualTo(new DateOnly(2026, 6, 9)));
    }

    [TestCase(1, 5)]
    [TestCase(10, 5)]
    [TestCase(11, 6)]
    public async Task CalculateLeadTimeBusinessDaysAsync_GivenPfmToothCounts_AppliesExtraLeadFormula(int toothCount, int expectedLeadDays)
    {
        var service = CreateService();
        var validTeeth = new[] { 18, 17, 16, 15, 14, 13, 12, 11, 21, 22, 23 };
        var workItems = validTeeth.Take(toothCount)
            .Select(tooth => new OrderWorkItem(ConstructionType.Crown, new ToothRange(tooth, tooth)))
            .ToArray();

        var days = await service.CalculateLeadTimeBusinessDaysAsync(Material.Pfm, workItems, new DateOnly(2026, 6, 10));

        Assert.That(days, Is.EqualTo(expectedLeadDays));
    }

    [Test]
    public async Task CalculateCapacityUnitsAsync_UsesDistinctTeethAndDecimalMaterialCapacity()
    {
        var service = CreateService(configs:
        [
            TestMaterialSchedulingConfigProvider.DefaultConfig(Material.Pfm) with { CapacityUnitsPerTooth = 1.5m }
        ]);
        var workItems = new[]
        {
            new OrderWorkItem(ConstructionType.Bridge, new ToothRange(11, 13)),
            new OrderWorkItem(ConstructionType.Crown, new ToothRange(21, 21))
        };

        var capacity = await service.CalculateCapacityUnitsAsync(Material.Pfm, workItems, new DateOnly(2026, 6, 10));

        Assert.That(capacity, Is.EqualTo(6.0m));
    }

    [Test]
    public async Task ValidateRequestedDateAsync_UsesMaterialConfigEffectiveForDeadlineDate()
    {
        var createdAtUtc = SofiaLocal(2026, 6, 5, 10, 30);
        var service = CreateService(
            configs:
            [
                TestMaterialSchedulingConfigProvider.DefaultConfig(Material.Pmma) with { ActiveFromDate = new DateOnly(2026, 1, 1), CapacityUnitsPerTooth = 1.0m },
                TestMaterialSchedulingConfigProvider.DefaultConfig(Material.Pmma) with { ActiveFromDate = new DateOnly(2026, 6, 10), CapacityUnitsPerTooth = 2.0m }
            ],
            capacityConfigs: [new SchedulingCapacityConfig(1, new DateOnly(2026, 1, 1), 2.5m, 100m)],
            orders: [BuildOrder(1, "EXISTING", new DateOnly(2026, 6, 10))]);

        var beforeFutureConfig = await service.ValidateRequestedDateAsync(Input(Material.Pmma, createdAtUtc), new DateOnly(2026, 6, 9));
        var onFutureConfig = await service.ValidateRequestedDateAsync(Input(Material.Pmma, createdAtUtc), new DateOnly(2026, 6, 10));

        Assert.Multiple(() =>
        {
            Assert.That(beforeFutureConfig.FailedRules, Does.Not.Contain(DeadlineValidationRule.DailyCapacityExceeded));
            Assert.That(beforeFutureConfig.OrderCapacityUnits, Is.EqualTo(1.0m));
            Assert.That(onFutureConfig.FailedRules, Does.Contain(DeadlineValidationRule.DailyCapacityExceeded));
            Assert.That(onFutureConfig.OrderCapacityUnits, Is.EqualTo(2.0m));
        });
    }

    [Test]
    public async Task RecommendAsync_UsesMaterialConfigEffectiveForRecommendedDate()
    {
        var createdAtUtc = SofiaLocal(2026, 6, 9, 10, 30);
        var service = CreateService(
            configs:
            [
                TestMaterialSchedulingConfigProvider.DefaultConfig(Material.Pmma) with { ActiveFromDate = new DateOnly(2026, 1, 1), FixedLeadTimeBusinessDays = 2 },
                TestMaterialSchedulingConfigProvider.DefaultConfig(Material.Pmma) with { ActiveFromDate = new DateOnly(2026, 6, 10), FixedLeadTimeBusinessDays = 4 }
            ]);

        var result = await service.RecommendAsync(Input(Material.Pmma, createdAtUtc));

        Assert.That(result.LeadTimeBusinessDays, Is.EqualTo(4));
        Assert.That(result.EarliestSelectableDeadline, Is.EqualTo(new DateOnly(2026, 6, 16)));
    }

    [Test]
    public async Task RecommendCapacityAwareDateAsync_GivenDailyCapacityFull_ReturnsNextSelectableDateAndMarksReason()
    {
        var createdAtUtc = SofiaLocal(2026, 6, 2, 10, 30);
        var existingOrders = new[]
        {
            BuildOrder(1, "EX-THU", new DateOnly(2026, 6, 4))
        };
        var service = CreateService(
            orders: existingOrders,
            capacityConfigs: [new SchedulingCapacityConfig(1, new DateOnly(2026, 1, 1), 1m, 10m)]);

        var recommended = await service.RecommendCapacityAwareDateAsync(Input(Material.Pmma, createdAtUtc));
        var statuses = await service.GetCapacityAwareDateStatusesAsync(Input(Material.Pmma, createdAtUtc), new DateOnly(2026, 6, 4), new DateOnly(2026, 6, 5));
        var thursday = statuses.Statuses.Single(s => s.Date == new DateOnly(2026, 6, 4));
        var friday = statuses.Statuses.Single(s => s.Date == new DateOnly(2026, 6, 5));

        Assert.That(recommended, Is.EqualTo(new DateOnly(2026, 6, 5)));
        Assert.That(thursday.IsSelectable, Is.False);
        Assert.That(thursday.IsDailyCapacityExceeded, Is.True);
        Assert.That(thursday.Reason, Is.EqualTo("Daily capacity exceeded"));
        Assert.That(friday.IsSelectable, Is.True);
    }

    [Test]
    public async Task RecommendCapacityAwareDateAsync_GivenWeeklyCapacityFull_ReturnsNextWeekSelectableDate()
    {
        var createdAtUtc = SofiaLocal(2026, 6, 2, 10, 30);
        var existingOrders = new[]
        {
            BuildOrder(1, "EX-WEEK", new DateOnly(2026, 6, 4))
        };
        var service = CreateService(
            orders: existingOrders,
            capacityConfigs: [new SchedulingCapacityConfig(1, new DateOnly(2026, 1, 1), 10m, 1m)]);

        var recommended = await service.RecommendCapacityAwareDateAsync(Input(Material.Pmma, createdAtUtc));
        var statuses = await service.GetCapacityAwareDateStatusesAsync(Input(Material.Pmma, createdAtUtc), new DateOnly(2026, 6, 4), new DateOnly(2026, 6, 9));
        var thursday = statuses.Statuses.Single(s => s.Date == new DateOnly(2026, 6, 4));
        var friday = statuses.Statuses.Single(s => s.Date == new DateOnly(2026, 6, 5));

        Assert.That(recommended, Is.EqualTo(new DateOnly(2026, 6, 9)));
        Assert.That(thursday.IsWeeklyCapacityExceeded, Is.True);
        Assert.That(friday.IsWeeklyCapacityExceeded, Is.True);
    }

    [Test]
    public async Task RecommendCapacityAwareDateAsync_GivenHolidayWeek_ReducesWeeklyCapacityProportionally()
    {
        var createdAtUtc = SofiaLocal(2026, 6, 2, 10, 30);
        var holidayMonday = new DateOnly(2026, 6, 1);
        var existingOrders = new[]
        {
            BuildOrder(1, "EX-HOLIDAY-WEEK", new DateOnly(2026, 6, 3)) with { CalculatedCapacityUnits = 4m }
        };
        var service = CreateService(
            extraClosedDates: [holidayMonday],
            orders: existingOrders,
            capacityConfigs: [new SchedulingCapacityConfig(1, new DateOnly(2026, 1, 1), 10m, 5m)]);

        var recommended = await service.RecommendCapacityAwareDateAsync(Input(Material.Pmma, createdAtUtc));
        var statuses = await service.GetCapacityAwareDateStatusesAsync(Input(Material.Pmma, createdAtUtc), new DateOnly(2026, 6, 4), new DateOnly(2026, 6, 5));
        var weeklyUsage = await service.GetWeeklyCapacityUsageByWeekEndAsync(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 7));
        var thursday = statuses.Statuses.Single(s => s.Date == new DateOnly(2026, 6, 4));
        var friday = statuses.Statuses.Single(s => s.Date == new DateOnly(2026, 6, 5));
        var weekEnd = new DateOnly(2026, 6, 7);

        Assert.Multiple(() =>
        {
            Assert.That(recommended, Is.EqualTo(new DateOnly(2026, 6, 9)));
            Assert.That(thursday.IsWeeklyCapacityExceeded, Is.True);
            Assert.That(thursday.WeeklyCapacityLimit, Is.EqualTo(4m));
            Assert.That(friday.IsWeeklyCapacityExceeded, Is.True);
            Assert.That(weeklyUsage[weekEnd].Used, Is.EqualTo(4m));
            Assert.That(weeklyUsage[weekEnd].Limit, Is.EqualTo(4m));
        });
    }

    [Test]
    public async Task RecommendCapacityAwareDateAsync_CancelledOrdersDoNotConsumeCapacity()
    {
        var createdAtUtc = SofiaLocal(2026, 6, 3, 10, 30);
        var existingOrders = new[]
        {
            BuildOrder(1, "CANCELLED", new DateOnly(2026, 6, 5)) with { Status = OrderStatus.Cancelled }
        };
        var service = CreateService(
            orders: existingOrders,
            capacityConfigs: [new SchedulingCapacityConfig(1, new DateOnly(2026, 1, 1), 1m, 10m)]);

        var recommended = await service.RecommendCapacityAwareDateAsync(Input(Material.Pmma, createdAtUtc));

        Assert.That(recommended, Is.EqualTo(new DateOnly(2026, 6, 5)));
    }

    [Test]
    public async Task ValidateRequestedDateAsync_AllowsLargeOrderOverDailyCapacity_WhenDayHasNoOtherOrders()
    {
        var createdAtUtc = SofiaLocal(2026, 6, 8, 10, 30);
        var service = CreateService(
            capacityConfigs: [new SchedulingCapacityConfig(1, new DateOnly(2026, 1, 1), 12m, 100m)]);
        var input = new OrderSchedulingInput(
            Material.Pmma,
            [new OrderWorkItem(ConstructionType.Bridge, new ToothRange(18, 25))],
            createdAtUtc);

        var result = await service.ValidateRequestedDateAsync(input, new DateOnly(2026, 6, 11));

        Assert.Multiple(() =>
        {
            Assert.That(result.Status.IsSelectable, Is.True);
            Assert.That(result.Status.IsDailyCapacityExceeded, Is.False);
            Assert.That(result.OrderCapacityUnits, Is.EqualTo(13m));
        });
    }

    [Test]
    public async Task ValidateRequestedDateAsync_ExcludesCurrentOrderOnUpdate()
    {
        var createdAtUtc = SofiaLocal(2026, 6, 3, 10, 30);
        var existingOrders = new[]
        {
            BuildOrder(7, "SELF", new DateOnly(2026, 6, 5))
        };
        var service = CreateService(
            orders: existingOrders,
            capacityConfigs: [new SchedulingCapacityConfig(1, new DateOnly(2026, 1, 1), 1m, 10m)]);

        var result = await service.ValidateRequestedDateAsync(
            Input(Material.Pmma, createdAtUtc, excludedOrderId: 7),
            new DateOnly(2026, 6, 5));

        Assert.That(result.Status.IsSelectable, Is.True);
    }

    [Test]
    public async Task RecommendCapacityAwareDateAsync_GivenExistingNullCapacity_FallsBackToCurrentMaterialConfig()
    {
        var createdAtUtc = SofiaLocal(2026, 6, 2, 10, 30);
        var existingOrders = new[]
        {
            BuildOrder(1, "LEGACY", new DateOnly(2026, 6, 4), [
                new OrderWorkItem(ConstructionType.Crown, new ToothRange(11, 11)),
                new OrderWorkItem(ConstructionType.Crown, new ToothRange(12, 12))
            ]) with { CalculatedCapacityUnits = null }
        };
        var service = CreateService(
            orders: existingOrders,
            configs:
            [
                TestMaterialSchedulingConfigProvider.DefaultConfig(Material.Pmma) with { CapacityUnitsPerTooth = 1.5m }
            ],
            capacityConfigs: [new SchedulingCapacityConfig(1, new DateOnly(2026, 1, 1), 4m, 10m)]);

        var statuses = await service.GetCapacityAwareDateStatusesAsync(Input(Material.Pmma, createdAtUtc), new DateOnly(2026, 6, 4), new DateOnly(2026, 6, 5));
        var thursday = statuses.Statuses.Single(s => s.Date == new DateOnly(2026, 6, 4));

        Assert.That(thursday.IsSelectable, Is.False);
        Assert.That(thursday.IsDailyCapacityExceeded, Is.True);
        Assert.That(thursday.ExistingDailyCapacityUsed, Is.EqualTo(3.0m));
    }

    [Test]
    public async Task ValidateRequestedDateWithAuditAsync_ContainsLeadTimeAndConfigSnapshot()
    {
        var service = CreateService(configs:
        [
            TestMaterialSchedulingConfigProvider.DefaultConfig(Material.FullContourZirconia) with
            {
                FixedLeadTimeBusinessDays = 3,
                CapacityUnitsPerTooth = 1.5m
            }
        ]);
        var createdAtUtc = SofiaLocal(2026, 6, 2, 10, 30);

        var result = await service.ValidateRequestedDateWithAuditAsync(
            new OrderSchedulingInput(Material.FullContourZirconia,
            [
                new OrderWorkItem(ConstructionType.Bridge, new ToothRange(11, 13)),
                new OrderWorkItem(ConstructionType.Crown, new ToothRange(21, 21))
            ], createdAtUtc),
            new DateOnly(2026, 6, 5),
            orderRepositoryOverride: null);

        var audit = result.Audit;
        Assert.Multiple(() =>
        {
            Assert.That(audit.EffectiveIntakeBusinessDate, Is.EqualTo(new DateOnly(2026, 6, 2)));
            Assert.That(audit.FixedLeadTimeBusinessDaysUsed, Is.EqualTo(3));
            Assert.That(audit.ExtraLeadTimeBusinessDaysUsed, Is.EqualTo(0));
            Assert.That(audit.LeadTimeBusinessDaysUsed, Is.EqualTo(3));
            Assert.That(audit.ToothCount, Is.EqualTo(4));
            Assert.That(audit.CapacityUnitsPerToothUsed, Is.EqualTo(1.5m));
            Assert.That(audit.CalculatedOrderCapacityUnits, Is.EqualTo(6.0m));
            Assert.That(audit.MinimumDeadlineDateFromLeadTime, Is.EqualTo(new DateOnly(2026, 6, 5)));
            Assert.That(audit.FinalRecommendedDeadlineDate, Is.EqualTo(new DateOnly(2026, 6, 5)));
            Assert.That(audit.ConfigSnapshotJson, Does.Contain("fullContourZirconia"));
        });
    }

    [Test]
    public async Task ValidateRequestedDateWithAuditAsync_GivenPfm_CapturesExtraLeadDays()
    {
        var service = CreateService(configs: [TestMaterialSchedulingConfigProvider.DefaultConfig(Material.Pfm) with { TeethPerExtraLeadDay = 10 }]);
        var teeth = new[] { 18, 17, 16, 15, 14, 13, 12, 11, 21, 22, 23 };
        var workItems = teeth.Select(t => new OrderWorkItem(ConstructionType.Crown, new ToothRange(t, t))).ToArray();

        var result = await service.ValidateRequestedDateWithAuditAsync(
            new OrderSchedulingInput(Material.Pfm, workItems, SofiaLocal(2026, 6, 2, 10, 30)),
            new DateOnly(2026, 6, 10),
            orderRepositoryOverride: null);

        Assert.Multiple(() =>
        {
            Assert.That(result.Audit.FixedLeadTimeBusinessDaysUsed, Is.EqualTo(4));
            Assert.That(result.Audit.ExtraLeadTimeBusinessDaysUsed, Is.EqualTo(2));
            Assert.That(result.Audit.LeadTimeBusinessDaysUsed, Is.EqualTo(6));
            Assert.That(result.Audit.TeethPerExtraLeadDayUsed, Is.EqualTo(10));
            Assert.That(result.Audit.ToothCount, Is.EqualTo(11));
        });
    }

    [Test]
    public async Task ValidateRequestedDateWithAuditAsync_CandidateTrailRecordsCapacityRejectionAndAcceptedDate()
    {
        var createdAtUtc = SofiaLocal(2026, 6, 2, 10, 30);
        var service = CreateService(
            orders: [BuildOrder(1, "EX-THU", new DateOnly(2026, 6, 4))],
            capacityConfigs: [new SchedulingCapacityConfig(1, new DateOnly(2026, 1, 1), 1m, 10m)]);

        var result = await service.ValidateRequestedDateWithAuditAsync(Input(Material.Pmma, createdAtUtc), new DateOnly(2026, 6, 5), orderRepositoryOverride: null);

        var rejected = result.Audit.CandidateChecks.Single(c => c.CandidateDate == new DateOnly(2026, 6, 4));
        var accepted = result.Audit.CandidateChecks.Single(c => c.CandidateDate == new DateOnly(2026, 6, 5));
        Assert.Multiple(() =>
        {
            Assert.That(rejected.Accepted, Is.False);
            Assert.That(rejected.IsSelectableDeadline, Is.True);
            Assert.That(rejected.ExistingDailyCapacityUsed, Is.EqualTo(1m));
            Assert.That(rejected.DailyCapacityLimitUsed, Is.EqualTo(1m));
            Assert.That(rejected.OrderCapacityUnits, Is.EqualTo(1m));
            Assert.That(rejected.DailyCapacityWouldPass, Is.False);
            Assert.That(rejected.RejectionReasons, Does.Contain(nameof(DeadlineValidationRule.DailyCapacityExceeded)));
            Assert.That(accepted.Accepted, Is.True);
            Assert.That(accepted.IsSelectableDeadline, Is.True);
            Assert.That(accepted.WeeklyCapacityWouldPass, Is.True);
        });
    }

    [Test]
    public async Task RecommendAsync_UsesProviderFixedLeadTimeInsteadOfHardcodedValues()
    {
        var service = CreateService(configs: [TestMaterialSchedulingConfigProvider.DefaultConfig(Material.Pmma) with { FixedLeadTimeBusinessDays = 3 }]);
        var createdAtUtc = SofiaLocal(2026, 6, 2, 10, 30);

        var result = await service.RecommendAsync(Input(Material.Pmma, createdAtUtc));

        Assert.That(result.LeadTimeBusinessDays, Is.EqualTo(3));
        Assert.That(result.EarliestSelectableDeadline, Is.EqualTo(new DateOnly(2026, 6, 5)));
    }

    [Test]
    public async Task CalculateLeadTimeBusinessDaysAsync_GivenConfiguredPfmTeethPerExtraLeadDay_UsesConfiguredValue()
    {
        var service = CreateService(configs: [TestMaterialSchedulingConfigProvider.DefaultConfig(Material.Pfm) with { TeethPerExtraLeadDay = 5 }]);
        var workItems = new[] { new OrderWorkItem(ConstructionType.Bridge, new ToothRange(18, 13)) };

        var days = await service.CalculateLeadTimeBusinessDaysAsync(Material.Pfm, workItems, new DateOnly(2026, 6, 10));

        Assert.That(OrderWorkItem.AllTeeth(workItems), Has.Length.EqualTo(6));
        Assert.That(days, Is.EqualTo(6));
    }

    [Test]
    public void RecommendAsync_GivenMissingConfig_FailsClearly()
    {
        var service = CreateService(configs: []);

        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await service.RecommendAsync(Input(Material.Pmma, SofiaLocal(2026, 6, 2, 10, 30))));

        Assert.That(ex!.Message, Does.Contain("missing").IgnoreCase);
        Assert.That(ex.Message, Does.Contain(nameof(Material.Pmma)));
    }

    [TestCase(null)]
    [TestCase(0)]
    public void CalculateLeadTimeBusinessDaysAsync_GivenInvalidPfmTeethPerExtraLeadDay_FailsClearly(int? teethPerExtraLeadDay)
    {
        var service = CreateService(configs: [TestMaterialSchedulingConfigProvider.DefaultConfig(Material.Pfm) with { TeethPerExtraLeadDay = teethPerExtraLeadDay }]);

        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await service.CalculateLeadTimeBusinessDaysAsync(Material.Pfm, [new OrderWorkItem(ConstructionType.Crown, new ToothRange(11, 11))], new DateOnly(2026, 6, 10)));

        Assert.That(ex!.Message, Does.Contain("teeth per extra lead day").IgnoreCase);
        Assert.That(ex.Message, Does.Contain(nameof(Material.Pfm)));
    }

    [Test]
    public async Task CalculateLeadTimeBusinessDaysAsync_GivenNonPfmStrayTeethPerExtraLeadDay_IgnoresIt()
    {
        var service = CreateService(configs: [TestMaterialSchedulingConfigProvider.DefaultConfig(Material.Pmma) with { FixedLeadTimeBusinessDays = 2, TeethPerExtraLeadDay = 1 }]);

        var days = await service.CalculateLeadTimeBusinessDaysAsync(
            Material.Pmma,
            [new OrderWorkItem(ConstructionType.Bridge, new ToothRange(18, 13))],
            new DateOnly(2026, 6, 10));

        Assert.That(days, Is.EqualTo(2));
    }

    [TestCase(0, 1, "fixed lead-time")]
    [TestCase(2, 0, "capacity units")]
    public void RecommendAsync_GivenInvalidConfig_FailsClearly(int fixedLeadDays, decimal capacityUnitsPerTooth, string expectedMessage)
    {
        var service = CreateService(configs:
        [
            TestMaterialSchedulingConfigProvider.DefaultConfig(Material.Pmma) with
            {
                FixedLeadTimeBusinessDays = fixedLeadDays,
                CapacityUnitsPerTooth = capacityUnitsPerTooth
            }
        ]);

        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await service.RecommendAsync(Input(Material.Pmma, SofiaLocal(2026, 6, 2, 10, 30))));

        Assert.That(ex!.Message, Does.Contain(expectedMessage).IgnoreCase);
    }

    private static DeadlineRecommendationService CreateService(
        IEnumerable<DateOnly>? extraClosedDates = null,
        IReadOnlyList<MaterialSchedulingConfig>? configs = null,
        IReadOnlyList<SchedulingCapacityConfig>? capacityConfigs = null,
        IReadOnlyList<OrderRecord>? orders = null)
    {
        var availability = new DateAvailabilityService(new TestNonWorkingDayProvider(extraClosedDates ?? []));
        return new DeadlineRecommendationService(
            availability,
            new TestMaterialSchedulingConfigProvider(configs),
            new TestSchedulingCapacityConfigProvider(capacityConfigs),
            new InMemoryOrderRepository(orders));
    }

    private static OrderSchedulingInput Input(Material material, DateTimeOffset createdAtUtc, long? excludedOrderId = null) =>
        new(material, [new OrderWorkItem(ConstructionType.Crown, new ToothRange(11, 11))], createdAtUtc, excludedOrderId);

    private static OrderRecord BuildOrder(long id, string code, DateOnly requestedDeliveryDate, IReadOnlyList<OrderWorkItem>? workItems = null) =>
        new(
            id,
            code,
            "DEMO",
            "Demo Clinic",
            "seed",
            "Seed",
            code,
            new DateOnly(2026, 6, 2),
            ProductCategory.Permanent,
            Material.Pmma,
            workItems ?? [new OrderWorkItem(ConstructionType.Crown, new ToothRange(11, 11))],
            requestedDeliveryDate,
            OrderStatus.Created,
            Shade.Unspecified,
            null,
            DateTimeOffset.Parse("2026-06-02T07:30:00Z"),
            DateTimeOffset.Parse("2026-06-02T07:30:00Z"),
            "127.0.0.1",
            "test",
            null,
            1m);

    private static DateTimeOffset SofiaLocal(int year, int month, int day, int hour, int minute)
    {
        var localUnspecified = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);
        var offset = LabTimeZone.BulgariaSofia.GetUtcOffset(localUnspecified);
        return new DateTimeOffset(localUnspecified, offset).ToUniversalTime();
    }

    private sealed class TestNonWorkingDayProvider : INonWorkingDayProvider
    {
        private readonly IReadOnlySet<DateOnly> _extraClosedDates;

        public TestNonWorkingDayProvider(IEnumerable<DateOnly> extraClosedDates) =>
            _extraClosedDates = extraClosedDates.ToHashSet();

        public async Task<IReadOnlySet<DateOnly>> GetNonWorkingDaysAsync(int year, CancellationToken ct = default)
        {
            var weekends = await new WeekendOnlyNonWorkingDayProvider().GetNonWorkingDaysAsync(year, ct);
            return weekends.Concat(_extraClosedDates.Where(d => d.Year == year)).ToHashSet();
        }
    }

    private sealed class InMemoryOrderRepository : IOrderRepository
    {
        private readonly IReadOnlyList<OrderRecord> _orders;

        public InMemoryOrderRepository(IReadOnlyList<OrderRecord>? orders = null) => _orders = orders ?? [];

        public Task<OrderRecord> CreateOrderAsync(OrderRecord order, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<OrderRecord?> GetOrderByCodeAsync(string orderCode, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<OrderRecord> UpdateOrderAsync(OrderRecord order, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<OrderRecord>> ListOrdersAsync(int limit = 100, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<OrderRecord>> ListOrdersForClinicAsync(string clinicCode, int limit = 100, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<OrderPage> ListOrdersPageAsync(OrderVisibilityScope scope, int limit, OrderCursor? cursor, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<OrderPage> ListOrdersPageContainingOrderAsync(OrderVisibilityScope scope, OrderRecord target, int limit, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<OrderRecord>> FindOrdersByCodeSuffixAsync(OrderVisibilityScope scope, string codeSuffix, int limit = 2, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<OrderRecord>> ListActiveOrdersForCalendarAsync(OrderVisibilityScope scope, DateOnly start, DateOnly end, CancellationToken ct = default) => throw new NotImplementedException();

        public Task<IReadOnlyList<OrderRecord>> ListActiveOrdersByDeadlineRangeAsync(DateOnly start, DateOnly end, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<OrderRecord>>(_orders
                .Where(o => o.Status != OrderStatus.Cancelled && o.RequestedDeliveryDate >= start && o.RequestedDeliveryDate <= end)
                .OrderBy(o => o.RequestedDeliveryDate)
                .ThenBy(o => o.Id)
                .ToList());
    }
}
