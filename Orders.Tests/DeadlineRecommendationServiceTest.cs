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

        var days = await service.CalculateLeadTimeBusinessDaysAsync(Material.Pfm, workItems);

        Assert.That(days, Is.EqualTo(expectedLeadDays));
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

        var days = await service.CalculateLeadTimeBusinessDaysAsync(Material.Pfm, workItems);

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
            await service.CalculateLeadTimeBusinessDaysAsync(Material.Pfm, [new OrderWorkItem(ConstructionType.Crown, new ToothRange(11, 11))]));

        Assert.That(ex!.Message, Does.Contain("teeth per extra lead day").IgnoreCase);
        Assert.That(ex.Message, Does.Contain(nameof(Material.Pfm)));
    }

    [Test]
    public async Task CalculateLeadTimeBusinessDaysAsync_GivenNonPfmStrayTeethPerExtraLeadDay_IgnoresIt()
    {
        var service = CreateService(configs: [TestMaterialSchedulingConfigProvider.DefaultConfig(Material.Pmma) with { FixedLeadTimeBusinessDays = 2, TeethPerExtraLeadDay = 1 }]);

        var days = await service.CalculateLeadTimeBusinessDaysAsync(
            Material.Pmma,
            [new OrderWorkItem(ConstructionType.Bridge, new ToothRange(18, 13))]);

        Assert.That(days, Is.EqualTo(2));
    }

    [TestCase(0, 1, true, "fixed lead-time")]
    [TestCase(2, 0, true, "capacity units")]
    [TestCase(2, 1, false, "inactive")]
    public void RecommendAsync_GivenInvalidConfig_FailsClearly(int fixedLeadDays, decimal capacityUnitsPerTooth, bool isActive, string expectedMessage)
    {
        var service = CreateService(configs:
        [
            TestMaterialSchedulingConfigProvider.DefaultConfig(Material.Pmma) with
            {
                FixedLeadTimeBusinessDays = fixedLeadDays,
                CapacityUnitsPerTooth = capacityUnitsPerTooth,
                IsActive = isActive
            }
        ]);

        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await service.RecommendAsync(Input(Material.Pmma, SofiaLocal(2026, 6, 2, 10, 30))));

        Assert.That(ex!.Message, Does.Contain(expectedMessage).IgnoreCase);
    }

    private static DeadlineRecommendationService CreateService(IEnumerable<DateOnly>? extraClosedDates = null, IReadOnlyList<MaterialSchedulingConfig>? configs = null)
    {
        var availability = new DateAvailabilityService(new TestNonWorkingDayProvider(extraClosedDates ?? []));
        return new DeadlineRecommendationService(availability, new TestMaterialSchedulingConfigProvider(configs));
    }

    private static OrderSchedulingInput Input(Material material, DateTimeOffset createdAtUtc) =>
        new(material, [new OrderWorkItem(ConstructionType.Crown, new ToothRange(11, 11))], createdAtUtc);

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
}
