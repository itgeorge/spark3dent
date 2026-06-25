namespace Orders;

public sealed record SchedulingCapacityConfig(
    long Id,
    DateOnly ActiveFromDate,
    decimal DailyCapacityUnits,
    decimal WeeklyCapacityUnits);

public interface ISchedulingCapacityConfigProvider
{
    Task<SchedulingCapacityConfig> GetForDateAsync(DateOnly date, CancellationToken ct = default);
    Task<IReadOnlyList<SchedulingCapacityConfig>> ListAsync(CancellationToken ct = default);
}

public sealed record CapacityUsage(decimal DailyUsed, decimal WeeklyUsed);

public sealed record DailyCapacityUsage(DateOnly Date, decimal Used, decimal Limit);

public sealed record WeeklyCapacityUsage(DateOnly WeekEndDate, decimal Used, decimal Limit);

public enum DeadlineValidationRule
{
    MinimumLeadTime,
    CalendarDeadlineBlocked,
    DailyCapacityExceeded,
    WeeklyCapacityExceeded,
    SearchFailure,
    Other
}

public sealed record DeadlineValidationResult(
    DateOnly MinimumDate,
    DateOnly? RecommendedDate,
    DeliveryDateStatus Status,
    decimal OrderCapacityUnits,
    IReadOnlyList<DeadlineValidationRule> FailedRules);

public static class SchedulingWeek
{
    public static (DateOnly Start, DateOnly End) GetRange(DateOnly date)
    {
        var diff = ((int)date.DayOfWeek + 6) % 7;
        var start = date.AddDays(-diff);
        return (start, start.AddDays(6));
    }
}
