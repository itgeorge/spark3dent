using Database.Entities;
using Microsoft.EntityFrameworkCore;
using Orders;

namespace Database;

public sealed class SqliteDeadlineRecommendationLogRepository : IDeadlineRecommendationLogRepository
{
    private readonly Func<AppDbContext> _contextFactory;

    public SqliteDeadlineRecommendationLogRepository(Func<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<DeadlineRecommendationLog> AddAsync(DeadlineRecommendationLog log, CancellationToken ct = default)
    {
        await using var ctx = _contextFactory();
        var entity = ToEntity(log);
        ctx.SchedulingDeadlineRecommendationLogs.Add(entity);
        await ctx.SaveChangesAsync(ct);
        return ToDomain(entity);
    }

    public async Task<IReadOnlyList<DeadlineRecommendationLog>> ListForOrderAsync(long orderId, CancellationToken ct = default)
    {
        await using var ctx = _contextFactory();
        var rows = await ctx.SchedulingDeadlineRecommendationLogs
            .AsNoTracking()
            .Where(l => l.OrderId == orderId)
            .OrderByDescending(l => l.CreatedAtUnixTimeMilliseconds)
            .ThenByDescending(l => l.Id)
            .ToListAsync(ct);
        return rows.Select(ToDomain).ToList();
    }

    private static SchedulingDeadlineRecommendationLogEntity ToEntity(DeadlineRecommendationLog log) => new()
    {
        Id = log.Id,
        OrderId = log.OrderId,
        OrderCode = log.OrderCode,
        CreatedAtUtc = log.CreatedAtUtc,
        CreatedAtUnixTimeMilliseconds = log.CreatedAtUtc.ToUnixTimeMilliseconds(),
        CreatedByOrganizationType = log.CreatedByOrganizationType,
        CreatedByOrganizationCode = log.CreatedByOrganizationCode,
        CreatedByMemberId = log.CreatedByMemberId,
        CreatedByMemberLabel = log.CreatedByMemberLabel,
        OrderCreatedAtUtc = log.OrderCreatedAtUtc,
        EffectiveIntakeBusinessDate = log.EffectiveIntakeBusinessDate,
        CutoffTimeUsed = log.CutoffTimeUsed,
        Material = log.Material.ToString(),
        ToothCount = log.ToothCount,
        LeadTimeBusinessDaysUsed = log.LeadTimeBusinessDaysUsed,
        FixedLeadTimeBusinessDaysUsed = log.FixedLeadTimeBusinessDaysUsed,
        ExtraLeadTimeBusinessDaysUsed = log.ExtraLeadTimeBusinessDaysUsed,
        TeethPerExtraLeadDayUsed = log.TeethPerExtraLeadDayUsed,
        CapacityUnitsPerToothUsed = log.CapacityUnitsPerToothUsed,
        CalculatedOrderCapacityUnits = log.CalculatedOrderCapacityUnits,
        MinimumDeadlineDateFromLeadTime = log.MinimumDeadlineDateFromLeadTime,
        FinalRecommendedDeadlineDate = log.FinalRecommendedDeadlineDate,
        SelectedDeadlineDate = log.SelectedDeadlineDate,
        SearchStartedAtDate = log.SearchStartedAtDate,
        SearchEndedAtDate = log.SearchEndedAtDate,
        SearchLimitDate = log.SearchLimitDate,
        ResultStatus = log.ResultStatus,
        FailureReason = log.FailureReason,
        CandidateChecksJson = log.CandidateChecksJson,
        ConfigSnapshotJson = log.ConfigSnapshotJson
    };

    private static DeadlineRecommendationLog ToDomain(SchedulingDeadlineRecommendationLogEntity e) => new(
        e.Id,
        e.OrderId,
        e.OrderCode,
        e.CreatedAtUtc,
        e.CreatedByOrganizationType,
        e.CreatedByOrganizationCode,
        e.CreatedByMemberId,
        e.CreatedByMemberLabel,
        e.OrderCreatedAtUtc,
        e.EffectiveIntakeBusinessDate,
        e.CutoffTimeUsed,
        Enum.Parse<Material>(e.Material),
        e.ToothCount,
        e.LeadTimeBusinessDaysUsed,
        e.FixedLeadTimeBusinessDaysUsed,
        e.ExtraLeadTimeBusinessDaysUsed,
        e.TeethPerExtraLeadDayUsed,
        e.CapacityUnitsPerToothUsed,
        e.CalculatedOrderCapacityUnits,
        e.MinimumDeadlineDateFromLeadTime,
        e.FinalRecommendedDeadlineDate,
        e.SelectedDeadlineDate,
        e.SearchStartedAtDate,
        e.SearchEndedAtDate,
        e.SearchLimitDate,
        e.ResultStatus,
        e.FailureReason,
        e.CandidateChecksJson,
        e.ConfigSnapshotJson);
}
