namespace Orders;

public sealed record DeadlineOverrideRequest(
    bool ConfirmDeadlineOverride,
    string? DeadlineOverrideReason);

public sealed record DeadlineOverrideLog(
    long Id,
    long OrderId,
    string OrderCode,
    DateTimeOffset CreatedAtUtc,
    string CreatedByOrganizationType,
    string CreatedByOrganizationCode,
    string CreatedByMemberId,
    string CreatedByMemberLabel,
    DateOnly SelectedDeadlineDate,
    DateOnly? SystemRecommendedDeadlineDate,
    DateOnly MinimumDeadlineDate,
    decimal OrderCapacityUnits,
    string RulesBypassedJson,
    string OverrideReason,
    long? RecommendationLogId,
    decimal? ExistingDailyCapacityUsed,
    decimal? ExistingWeeklyCapacityUsed,
    decimal? DailyCapacityLimitUsed,
    decimal? WeeklyCapacityLimitUsed,
    decimal? DailyCapacityAfterOverride,
    decimal? WeeklyCapacityAfterOverride,
    string? CalendarReason);

public interface IDeadlineOverrideLogRepository
{
    Task<DeadlineOverrideLog> AddAsync(DeadlineOverrideLog log, CancellationToken ct = default);
    Task<IReadOnlyList<DeadlineOverrideLog>> ListForOrderAsync(long orderId, CancellationToken ct = default);
}

public sealed class NoOpDeadlineOverrideLogRepository : IDeadlineOverrideLogRepository
{
    public static readonly NoOpDeadlineOverrideLogRepository Instance = new();

    private NoOpDeadlineOverrideLogRepository()
    {
    }

    public Task<DeadlineOverrideLog> AddAsync(DeadlineOverrideLog log, CancellationToken ct = default) => Task.FromResult(log);

    public Task<IReadOnlyList<DeadlineOverrideLog>> ListForOrderAsync(long orderId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<DeadlineOverrideLog>>([]);
}

public sealed class DeadlineOverrideRequiredException : InvalidOperationException
{
    public DeadlineOverrideRequiredException(
        string message,
        bool overrideAllowed,
        IReadOnlyList<DeadlineValidationRule> failedRules,
        DateOnly? recommendedDate)
        : base(message)
    {
        OverrideAllowed = overrideAllowed;
        FailedRules = failedRules;
        RecommendedDate = recommendedDate;
    }

    public bool OverrideAllowed { get; }
    public IReadOnlyList<DeadlineValidationRule> FailedRules { get; }
    public DateOnly? RecommendedDate { get; }
}
