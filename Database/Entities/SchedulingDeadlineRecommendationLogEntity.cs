using Orders;

namespace Database.Entities;

public class SchedulingDeadlineRecommendationLogEntity
{
    public long Id { get; set; }
    public long? OrderId { get; set; }
    public string? OrderCode { get; set; }
    public long? ReservationId { get; set; }
    public string EntityType { get; set; } = "order";
    public DateTimeOffset CreatedAtUtc { get; set; }
    public long CreatedAtUnixTimeMilliseconds { get; set; }
    public string CreatedByOrganizationType { get; set; } = string.Empty;
    public string CreatedByOrganizationCode { get; set; } = string.Empty;
    public string CreatedByMemberId { get; set; } = string.Empty;
    public string CreatedByMemberLabel { get; set; } = string.Empty;
    public DateTimeOffset OrderCreatedAtUtc { get; set; }
    public DateOnly EffectiveIntakeBusinessDate { get; set; }
    public TimeOnly CutoffTimeUsed { get; set; }
    public string Material { get; set; } = string.Empty;
    public int ToothCount { get; set; }
    public int LeadTimeBusinessDaysUsed { get; set; }
    public int FixedLeadTimeBusinessDaysUsed { get; set; }
    public int ExtraLeadTimeBusinessDaysUsed { get; set; }
    public int? TeethPerExtraLeadDayUsed { get; set; }
    public decimal CapacityUnitsPerToothUsed { get; set; }
    public decimal CalculatedOrderCapacityUnits { get; set; }
    public DateOnly MinimumDeadlineDateFromLeadTime { get; set; }
    public DateOnly? FinalRecommendedDeadlineDate { get; set; }
    public DateOnly SelectedDeadlineDate { get; set; }
    public DateOnly SearchStartedAtDate { get; set; }
    public DateOnly SearchEndedAtDate { get; set; }
    public DateOnly SearchLimitDate { get; set; }
    public string ResultStatus { get; set; } = string.Empty;
    public string? FailureReason { get; set; }
    public string CandidateChecksJson { get; set; } = string.Empty;
    public string ConfigSnapshotJson { get; set; } = string.Empty;
}
