namespace Orders;

public interface INonWorkingDayProvider
{
    Task<IReadOnlySet<DateOnly>> GetNonWorkingDaysAsync(int year, CancellationToken ct = default);
}

public sealed class WeekendOnlyNonWorkingDayProvider : INonWorkingDayProvider
{
    public Task<IReadOnlySet<DateOnly>> GetNonWorkingDaysAsync(int year, CancellationToken ct = default)
    {
        var dates = new HashSet<DateOnly>();
        for (var d = new DateOnly(year, 1, 1); d.Year == year; d = d.AddDays(1))
        {
            if (DateAvailabilityService.IsWeekend(d)) dates.Add(d);
        }
        return Task.FromResult<IReadOnlySet<DateOnly>>(dates);
    }
}
