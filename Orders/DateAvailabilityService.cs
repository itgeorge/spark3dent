namespace Orders;

public sealed record DeliveryDateStatus(
    DateOnly Date,
    bool IsClosed,
    bool IsFirstBusinessDayAfterClosure,
    bool IsBeforeMinimum,
    bool IsSelectable,
    string? Reason);

public sealed class DateAvailabilityService
{
    private readonly INonWorkingDayProvider _nonWorkingDayProvider;

    public DateAvailabilityService(INonWorkingDayProvider nonWorkingDayProvider)
    {
        _nonWorkingDayProvider = nonWorkingDayProvider;
    }

    public async Task<DateOnly> CalculateMinimumDateAsync(DateOnly impressionDate, int minBusinessDays, CancellationToken ct = default)
    {
        if (minBusinessDays < 0) throw new InvalidOperationException("Minimum business days must be non-negative.");

        var calendar = new NonWorkingDayCalendar(_nonWorkingDayProvider);
        var current = impressionDate;
        var counted = 0;
        while (counted < minBusinessDays)
        {
            current = current.AddDays(1);
            if (!await calendar.IsClosedAsync(current, ct)) counted++;
        }

        return current;
    }

    public async Task<DeliveryDateStatus> GetStatusAsync(DateOnly date, DateOnly minimumDate, CancellationToken ct = default)
    {
        var calendar = new NonWorkingDayCalendar(_nonWorkingDayProvider);
        return await GetStatusAsync(date, minimumDate, calendar, ct);
    }

    public async Task<IReadOnlyList<DeliveryDateStatus>> GetStatusesAsync(DateOnly start, DateOnly end, DateOnly minimumDate, CancellationToken ct = default)
    {
        if (end < start) throw new InvalidOperationException("End date must be on or after start date.");

        var calendar = new NonWorkingDayCalendar(_nonWorkingDayProvider);
        var result = new List<DeliveryDateStatus>();
        for (var d = start; d <= end; d = d.AddDays(1))
            result.Add(await GetStatusAsync(d, minimumDate, calendar, ct));
        return result;
    }

    public async Task ValidateDeliveryDateAsync(DateOnly date, DateOnly minimumDate, CancellationToken ct = default)
    {
        var status = await GetStatusAsync(date, minimumDate, ct);
        if (!status.IsSelectable)
            throw new InvalidOperationException($"Delivery date {date:yyyy-MM-dd} is not available: {status.Reason}.");
    }

    public static bool IsWeekend(DateOnly date) => date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

    private static async Task<DeliveryDateStatus> GetStatusAsync(DateOnly date, DateOnly minimumDate, NonWorkingDayCalendar calendar, CancellationToken ct)
    {
        var isClosed = await calendar.IsClosedAsync(date, ct);
        var isFirst = !isClosed && await calendar.IsClosedAsync(date.AddDays(-1), ct);
        var isBeforeMinimum = date < minimumDate;
        var reason = GetUnavailableReason(date, isClosed, isFirst, isBeforeMinimum);
        return new DeliveryDateStatus(date, isClosed, isFirst, isBeforeMinimum, reason == null, reason);
    }

    private static string? GetUnavailableReason(DateOnly date, bool isClosed, bool isFirst, bool isBeforeMinimum)
    {
        if (isBeforeMinimum) return "Before minimum lead time";
        if (isClosed) return IsWeekend(date) ? "Weekend" : "Closed/non-working day";
        if (isFirst) return "First business day after weekend/closure";
        return null;
    }

    private sealed class NonWorkingDayCalendar
    {
        private readonly INonWorkingDayProvider _provider;
        private readonly Dictionary<int, IReadOnlySet<DateOnly>> _cache = new();

        public NonWorkingDayCalendar(INonWorkingDayProvider provider) => _provider = provider;

        public async Task<bool> IsClosedAsync(DateOnly date, CancellationToken ct)
        {
            var days = await GetDaysAsync(date.Year, ct);
            return days.Contains(date);
        }

        private async Task<IReadOnlySet<DateOnly>> GetDaysAsync(int year, CancellationToken ct)
        {
            if (_cache.TryGetValue(year, out var days)) return days;
            days = await _provider.GetNonWorkingDaysAsync(year, ct);
            _cache[year] = days;
            return days;
        }
    }
}
