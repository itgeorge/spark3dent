namespace Database.Entities;

public class SchedulingDeadlineOverrideLogEntity
{
    public long Id { get; set; }
    public long OrderId { get; set; }
    public string OrderCode { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public long CreatedAtUnixTimeMilliseconds { get; set; }
    public string CreatedByOrganizationType { get; set; } = string.Empty;
    public string CreatedByOrganizationCode { get; set; } = string.Empty;
    public string CreatedByMemberId { get; set; } = string.Empty;
    public string CreatedByMemberLabel { get; set; } = string.Empty;
    public DateOnly SelectedDeadlineDate { get; set; }
    public DateOnly? SystemRecommendedDeadlineDate { get; set; }
    public DateOnly MinimumDeadlineDate { get; set; }
    public decimal OrderCapacityUnits { get; set; }
    public string RulesBypassedJson { get; set; } = string.Empty;
    public string OverrideReason { get; set; } = string.Empty;
    public long? RecommendationLogId { get; set; }
    public decimal? ExistingDailyCapacityUsed { get; set; }
    public decimal? ExistingWeeklyCapacityUsed { get; set; }
    public decimal? DailyCapacityLimitUsed { get; set; }
    public decimal? WeeklyCapacityLimitUsed { get; set; }
    public decimal? DailyCapacityAfterOverride { get; set; }
    public decimal? WeeklyCapacityAfterOverride { get; set; }
    public string? CalendarReason { get; set; }
}
