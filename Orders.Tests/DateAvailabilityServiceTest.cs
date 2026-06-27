using Orders;

namespace Orders.Tests;

public class DateAvailabilityServiceTest
{
    [Test]
    public async Task GetStatusAsync_GivenMondayAfterWeekend_ReturnsFirstBusinessDayAfterClosure()
    {
        var availability = new DateAvailabilityService(new WeekendOnlyNonWorkingDayProvider());
        var monday = new DateOnly(2026, 6, 1);

        var status = await availability.GetStatusAsync(monday, monday);

        Assert.That(status.IsSelectable, Is.False);
        Assert.That(status.IsFirstBusinessDayAfterClosure, Is.True);
    }

    [Test]
    public async Task GetStatusAsync_GivenTuesdayAfterWeekend_ReturnsSelectable()
    {
        var availability = new DateAvailabilityService(new WeekendOnlyNonWorkingDayProvider());
        var tuesday = new DateOnly(2026, 6, 2);

        var status = await availability.GetStatusAsync(tuesday, tuesday);

        Assert.That(status.IsSelectable, Is.True);
    }

    [Test]
    public async Task GetNonWorkingDaysAsync_GivenBulgarianHoliday_ReturnsHolidayAsClosed()
    {
        var provider = new BulgariaHardcodedNonWorkingDayProvider();

        var days = await provider.GetNonWorkingDaysAsync(2026);

        Assert.That(days.Contains(new DateOnly(2026, 5, 1)), Is.True);
        Assert.That(days.Contains(new DateOnly(2026, 6, 6)), Is.True);
        Assert.That(days.Contains(new DateOnly(2026, 6, 2)), Is.False);
    }

    [TestCaseSource(nameof(BulgarianNonWorkingDates))]
    public async Task GetStatusAsync_GivenBulgarianNonWorkingDate_ReturnsClosed(DateOnly date)
    {
        var availability = new DateAvailabilityService(new BulgariaHardcodedNonWorkingDayProvider());

        var status = await availability.GetStatusAsync(date, date);

        Assert.That(status.IsClosed, Is.True);
    }

    [TestCaseSource(nameof(BulgarianWorkingDates))]
    public async Task GetStatusAsync_GivenBulgarianWorkingDate_ReturnsOpen(DateOnly date)
    {
        var availability = new DateAvailabilityService(new BulgariaHardcodedNonWorkingDayProvider());

        var status = await availability.GetStatusAsync(date, date);

        Assert.That(status.IsClosed, Is.False);
    }

    [Test]
    public async Task GetNonWorkingDaysAsync_GivenYearOutsideHardcodedRange_DelegatesToFallback()
    {
        var provider = new BulgariaHardcodedNonWorkingDayProvider();

        var days = await provider.GetNonWorkingDaysAsync(2030);
        var fallbackDays = await new WeekendOnlyNonWorkingDayProvider().GetNonWorkingDaysAsync(2030);

        Assert.That(days, Is.EquivalentTo(fallbackDays));
    }

    [Test]
    public async Task GetStatusAsync_GivenLabOffday_ReturnsClosedAndBlocksNextBusinessDay()
    {
        var service = new DateAvailabilityService(new FixedNonWorkingDayProvider(new DateOnly(2026, 6, 3)));

        var offday = await service.GetStatusAsync(new DateOnly(2026, 6, 3), new DateOnly(2026, 6, 1));
        var dayAfter = await service.GetStatusAsync(new DateOnly(2026, 6, 4), new DateOnly(2026, 6, 1));

        Assert.Multiple(() =>
        {
            Assert.That(offday.IsClosed, Is.True);
            Assert.That(offday.IsSelectable, Is.False);
            Assert.That(dayAfter.IsFirstBusinessDayAfterClosure, Is.True);
            Assert.That(dayAfter.IsSelectable, Is.False);
        });
    }

    [Test]
    public async Task CalculateMinimumDateAsync_SkipsLabOffday()
    {
        var service = new DateAvailabilityService(new FixedNonWorkingDayProvider(new DateOnly(2026, 6, 3)));

        var minimum = await service.CalculateMinimumDateAsync(new DateOnly(2026, 6, 1), 3);

        Assert.That(minimum, Is.EqualTo(new DateOnly(2026, 6, 5)));
    }

    [Test]
    public async Task GetImpressionStatusAsync_GivenLabOffday_ReturnsClosedNotSelectable()
    {
        var service = new DateAvailabilityService(new FixedNonWorkingDayProvider(new DateOnly(2026, 6, 4)));

        var status = await service.GetImpressionStatusAsync(new DateOnly(2026, 6, 4), new DateOnly(2026, 6, 1));

        Assert.Multiple(() =>
        {
            Assert.That(status.IsClosed, Is.True);
            Assert.That(status.IsSelectable, Is.False);
            Assert.That(status.Reason, Is.EqualTo("Closed/non-working day"));
        });
    }

    [Test]
    public async Task GetImpressionStatusAsync_GivenTodayOrPast_ReturnsNotSelectable()
    {
        var service = new DateAvailabilityService(new WeekendOnlyNonWorkingDayProvider());

        var status = await service.GetImpressionStatusAsync(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 1));

        Assert.Multiple(() =>
        {
            Assert.That(status.IsPastOrToday, Is.True);
            Assert.That(status.IsSelectable, Is.False);
            Assert.That(status.Reason, Does.Contain("future date"));
        });
    }

    [Test]
    public async Task GetImpressionStatusAsync_GivenFirstBusinessDayAfterClosure_AllowsWhileDeliveryBlocks()
    {
        var service = new DateAvailabilityService(new FixedNonWorkingDayProvider(new DateOnly(2026, 6, 3)));
        var firstBusinessDayAfterClosure = new DateOnly(2026, 6, 4);

        var impression = await service.GetImpressionStatusAsync(firstBusinessDayAfterClosure, new DateOnly(2026, 6, 1));
        var delivery = await service.GetStatusAsync(firstBusinessDayAfterClosure, firstBusinessDayAfterClosure);

        Assert.Multiple(() =>
        {
            Assert.That(impression.IsSelectable, Is.True);
            Assert.That(impression.IsClosed, Is.False);
            Assert.That(delivery.IsFirstBusinessDayAfterClosure, Is.True);
            Assert.That(delivery.IsSelectable, Is.False);
        });
    }

    [Test]
    public async Task GetStatusesAsync_LoadsNonWorkingDaysOncePerYearForRange()
    {
        var provider = new CountingNonWorkingDayProvider();
        var service = new DateAvailabilityService(provider);

        await service.GetStatusesAsync(
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 6, 10),
            new DateOnly(2026, 6, 1));

        Assert.That(provider.CallsByYear[2026], Is.EqualTo(1));
    }

    private static IEnumerable<DateOnly> BulgarianNonWorkingDates()
    {
        yield return new DateOnly(2026, 3, 3);
        yield return new DateOnly(2026, 5, 25);
        yield return new DateOnly(2026, 9, 6);
        yield return new DateOnly(2026, 9, 7);
        yield return new DateOnly(2026, 12, 28);
        yield return new DateOnly(2027, 5, 3);
        yield return new DateOnly(2027, 5, 4);
        yield return new DateOnly(2027, 5, 6);
        yield return new DateOnly(2028, 5, 8);
        yield return new DateOnly(2028, 9, 6);
        yield return new DateOnly(2028, 9, 22);
        yield return new DateOnly(2029, 9, 24);
        yield return new DateOnly(2029, 12, 24);
    }

    private static IEnumerable<DateOnly> BulgarianWorkingDates()
    {
        yield return new DateOnly(2026, 3, 2);
        yield return new DateOnly(2026, 5, 5);
        yield return new DateOnly(2029, 9, 7);
        yield return new DateOnly(2029, 12, 27);
    }

    private sealed class CountingNonWorkingDayProvider : INonWorkingDayProvider
    {
        public Dictionary<int, int> CallsByYear { get; } = new();

        public Task<IReadOnlySet<DateOnly>> GetNonWorkingDaysAsync(int year, CancellationToken ct = default)
        {
            CallsByYear[year] = CallsByYear.GetValueOrDefault(year) + 1;
            return new WeekendOnlyNonWorkingDayProvider().GetNonWorkingDaysAsync(year, ct);
        }
    }

    private sealed class FixedNonWorkingDayProvider : INonWorkingDayProvider
    {
        private readonly HashSet<DateOnly> _dates;

        public FixedNonWorkingDayProvider(params DateOnly[] dates)
        {
            _dates = dates.ToHashSet();
        }

        public Task<IReadOnlySet<DateOnly>> GetNonWorkingDaysAsync(int year, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlySet<DateOnly>>(_dates.Where(d => d.Year == year).ToHashSet());
    }
}
