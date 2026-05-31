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

    private sealed class CountingNonWorkingDayProvider : INonWorkingDayProvider
    {
        public Dictionary<int, int> CallsByYear { get; } = new();

        public Task<IReadOnlySet<DateOnly>> GetNonWorkingDaysAsync(int year, CancellationToken ct = default)
        {
            CallsByYear[year] = CallsByYear.GetValueOrDefault(year) + 1;
            return new WeekendOnlyNonWorkingDayProvider().GetNonWorkingDaysAsync(year, ct);
        }
    }
}
