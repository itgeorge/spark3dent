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

/// <summary>
/// Official Bulgarian public holidays through 2029, sourced from https://xn--b1aekbb1acci5f.com/ .
/// Dates outside the hardcoded set delegate to the fallback provider (weekends by default).
/// </summary>
public sealed class BulgariaHardcodedNonWorkingDayProvider : INonWorkingDayProvider
{
    private static readonly IReadOnlyDictionary<int, IReadOnlySet<DateOnly>> OfficialHolidaysByYear = BuildOfficialHolidaysByYear();

    private readonly WeekendOnlyNonWorkingDayProvider _fallback;

    public BulgariaHardcodedNonWorkingDayProvider()
        : this(new WeekendOnlyNonWorkingDayProvider())
    {
    }

    internal BulgariaHardcodedNonWorkingDayProvider(WeekendOnlyNonWorkingDayProvider fallback)
    {
        _fallback = fallback;
    }

    public async Task<IReadOnlySet<DateOnly>> GetNonWorkingDaysAsync(int year, CancellationToken ct = default)
    {
        var fallbackDays = await _fallback.GetNonWorkingDaysAsync(year, ct);
        if (!OfficialHolidaysByYear.TryGetValue(year, out var holidays))
            return fallbackDays;

        var result = new HashSet<DateOnly>(fallbackDays);
        result.UnionWith(holidays);
        return result;
    }

    private static IReadOnlyDictionary<int, IReadOnlySet<DateOnly>> BuildOfficialHolidaysByYear()
    {
        DateOnly D(int year, int month, int day) => new(year, month, day);

        return new Dictionary<int, IReadOnlySet<DateOnly>>
        {
            [2024] = new HashSet<DateOnly>
            {
                D(2024, 1, 1), D(2024, 3, 3), D(2024, 3, 4), D(2024, 5, 1), D(2024, 5, 3), D(2024, 5, 4),
                D(2024, 5, 5), D(2024, 5, 6), D(2024, 5, 24), D(2024, 9, 6), D(2024, 9, 22), D(2024, 9, 23),
                D(2024, 12, 24), D(2024, 12, 25), D(2024, 12, 26),
            },
            [2025] = new HashSet<DateOnly>
            {
                D(2025, 1, 1), D(2025, 3, 3), D(2025, 4, 18), D(2025, 4, 19), D(2025, 4, 20), D(2025, 4, 21),
                D(2025, 5, 1), D(2025, 5, 6), D(2025, 5, 24), D(2025, 5, 26), D(2025, 9, 6), D(2025, 9, 8),
                D(2025, 9, 22), D(2025, 12, 24), D(2025, 12, 25), D(2025, 12, 26),
            },
            [2026] = new HashSet<DateOnly>
            {
                D(2026, 1, 1), D(2026, 1, 2), D(2026, 3, 3), D(2026, 4, 10), D(2026, 4, 11), D(2026, 4, 12),
                D(2026, 4, 13), D(2026, 5, 1), D(2026, 5, 6), D(2026, 5, 24), D(2026, 5, 25), D(2026, 9, 6),
                D(2026, 9, 7), D(2026, 9, 22), D(2026, 12, 24), D(2026, 12, 25), D(2026, 12, 26), D(2026, 12, 28),
            },
            [2027] = new HashSet<DateOnly>
            {
                D(2027, 1, 1), D(2027, 3, 3), D(2027, 4, 30), D(2027, 5, 1), D(2027, 5, 2), D(2027, 5, 3),
                D(2027, 5, 4), D(2027, 5, 6), D(2027, 5, 24), D(2027, 9, 6), D(2027, 9, 22), D(2027, 12, 24),
                D(2027, 12, 25), D(2027, 12, 26), D(2027, 12, 27), D(2027, 12, 28),
            },
            [2028] = new HashSet<DateOnly>
            {
                D(2028, 1, 1), D(2028, 1, 3), D(2028, 3, 3), D(2028, 4, 14), D(2028, 4, 15), D(2028, 4, 16),
                D(2028, 4, 17), D(2028, 5, 1), D(2028, 5, 6), D(2028, 5, 8), D(2028, 5, 24), D(2028, 9, 6),
                D(2028, 9, 22), D(2028, 12, 24), D(2028, 12, 25), D(2028, 12, 26), D(2028, 12, 27),
            },
            [2029] = new HashSet<DateOnly>
            {
                D(2029, 1, 1), D(2029, 3, 3), D(2029, 3, 5), D(2029, 4, 6), D(2029, 4, 7), D(2029, 4, 8),
                D(2029, 4, 9), D(2029, 5, 1), D(2029, 5, 6), D(2029, 5, 7), D(2029, 5, 24), D(2029, 9, 6),
                D(2029, 9, 22), D(2029, 9, 24), D(2029, 12, 24), D(2029, 12, 25), D(2029, 12, 26),
            },
        };
    }
}
