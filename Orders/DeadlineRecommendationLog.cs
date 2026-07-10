using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orders;

public sealed record DeadlineRecommendationCandidateCheck(
    DateOnly CandidateDate,
    bool IsSelectableDeadline,
    string? CalendarBlockReason,
    decimal? DailyCapacityLimitUsed,
    decimal? WeeklyCapacityLimitUsed,
    decimal? ExistingDailyCapacityUsed,
    decimal? ExistingWeeklyCapacityUsed,
    decimal OrderCapacityUnits,
    bool DailyCapacityWouldPass,
    bool WeeklyCapacityWouldPass,
    bool Accepted,
    IReadOnlyList<string> RejectionReasons);

public sealed record DeadlineRecommendationAudit(
    DateTimeOffset ImpressionTimestampUtc,
    DateOnly EffectiveIntakeBusinessDate,
    TimeOnly CutoffTimeUsed,
    Material Material,
    int ToothCount,
    int LeadTimeBusinessDaysUsed,
    int FixedLeadTimeBusinessDaysUsed,
    int ExtraLeadTimeBusinessDaysUsed,
    int? TeethPerExtraLeadDayUsed,
    decimal CapacityUnitsPerToothUsed,
    decimal CalculatedOrderCapacityUnits,
    DateOnly MinimumDeadlineDateFromLeadTime,
    DateOnly? FinalRecommendedDeadlineDate,
    DateOnly SelectedDeadlineDate,
    DateOnly SearchStartedAtDate,
    DateOnly SearchEndedAtDate,
    DateOnly SearchLimitDate,
    string ResultStatus,
    string? FailureReason,
    IReadOnlyList<DeadlineRecommendationCandidateCheck> CandidateChecks,
    string ConfigSnapshotJson)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public string CandidateChecksJson => JsonSerializer.Serialize(CandidateChecks, JsonOptions);
}

public sealed record DeadlineValidationWithAuditResult(
    DeadlineValidationResult Validation,
    DeadlineRecommendationAudit Audit);

public sealed record DeadlineRecommendationLog(
    long Id,
    long? OrderId,
    string? OrderCode,
    DateTimeOffset CreatedAtUtc,
    string CreatedByOrganizationType,
    string CreatedByOrganizationCode,
    string CreatedByMemberId,
    string CreatedByMemberLabel,
    DateTimeOffset OrderCreatedAtUtc,
    DateOnly EffectiveIntakeBusinessDate,
    TimeOnly CutoffTimeUsed,
    Material Material,
    int ToothCount,
    int LeadTimeBusinessDaysUsed,
    int FixedLeadTimeBusinessDaysUsed,
    int ExtraLeadTimeBusinessDaysUsed,
    int? TeethPerExtraLeadDayUsed,
    decimal CapacityUnitsPerToothUsed,
    decimal CalculatedOrderCapacityUnits,
    DateOnly MinimumDeadlineDateFromLeadTime,
    DateOnly? FinalRecommendedDeadlineDate,
    DateOnly SelectedDeadlineDate,
    DateOnly SearchStartedAtDate,
    DateOnly SearchEndedAtDate,
    DateOnly SearchLimitDate,
    string ResultStatus,
    string? FailureReason,
    string CandidateChecksJson,
    string ConfigSnapshotJson,
    string EntityType = "order",
    long? ReservationId = null);

public interface IDeadlineRecommendationLogRepository
{
    Task<DeadlineRecommendationLog> AddAsync(DeadlineRecommendationLog log, CancellationToken ct = default);
    Task<IReadOnlyList<DeadlineRecommendationLog>> ListForOrderAsync(long orderId, CancellationToken ct = default);
    Task<IReadOnlyList<DeadlineRecommendationLog>> ListForReservationAsync(long reservationId, CancellationToken ct = default);
}

public sealed class NoOpDeadlineRecommendationLogRepository : IDeadlineRecommendationLogRepository
{
    public static readonly NoOpDeadlineRecommendationLogRepository Instance = new();

    private NoOpDeadlineRecommendationLogRepository()
    {
    }

    public Task<DeadlineRecommendationLog> AddAsync(DeadlineRecommendationLog log, CancellationToken ct = default) => Task.FromResult(log);

    public Task<IReadOnlyList<DeadlineRecommendationLog>> ListForOrderAsync(long orderId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<DeadlineRecommendationLog>>([]);

    public Task<IReadOnlyList<DeadlineRecommendationLog>> ListForReservationAsync(long reservationId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<DeadlineRecommendationLog>>([]);
}
