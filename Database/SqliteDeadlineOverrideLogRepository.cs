using Database.Entities;
using Microsoft.EntityFrameworkCore;
using Orders;

namespace Database;

public sealed class SqliteDeadlineOverrideLogRepository : IDeadlineOverrideLogRepository
{
    private readonly Func<AppDbContext> _contextFactory;

    public SqliteDeadlineOverrideLogRepository(Func<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<DeadlineOverrideLog> AddAsync(DeadlineOverrideLog log, CancellationToken ct = default)
    {
        await using var ctx = _contextFactory();
        var entity = ToEntity(log);
        ctx.SchedulingDeadlineOverrideLogs.Add(entity);
        await ctx.SaveChangesAsync(ct);
        return ToDomain(entity);
    }

    public async Task<IReadOnlyList<DeadlineOverrideLog>> ListForOrderAsync(long orderId, CancellationToken ct = default)
    {
        await using var ctx = _contextFactory();
        var rows = await ctx.SchedulingDeadlineOverrideLogs
            .AsNoTracking()
            .Where(l => l.OrderId == orderId)
            .OrderByDescending(l => l.CreatedAtUnixTimeMilliseconds)
            .ThenByDescending(l => l.Id)
            .ToListAsync(ct);
        return rows.Select(ToDomain).ToList();
    }

    public async Task<IReadOnlyList<DeadlineOverrideLog>> ListForReservationAsync(long reservationId, CancellationToken ct = default)
    {
        await using var ctx = _contextFactory();
        var rows = await ctx.SchedulingDeadlineOverrideLogs
            .AsNoTracking()
            .Where(l => l.ReservationId == reservationId)
            .OrderByDescending(l => l.CreatedAtUnixTimeMilliseconds)
            .ThenByDescending(l => l.Id)
            .ToListAsync(ct);
        return rows.Select(ToDomain).ToList();
    }

    private static SchedulingDeadlineOverrideLogEntity ToEntity(DeadlineOverrideLog log) => new()
    {
        Id = log.Id,
        OrderId = log.OrderId,
        OrderCode = log.OrderCode,
        ReservationId = log.ReservationId,
        EntityType = log.EntityType,
        CreatedAtUtc = log.CreatedAtUtc,
        CreatedAtUnixTimeMilliseconds = log.CreatedAtUtc.ToUnixTimeMilliseconds(),
        CreatedByOrganizationType = log.CreatedByOrganizationType,
        CreatedByOrganizationCode = log.CreatedByOrganizationCode,
        CreatedByMemberId = log.CreatedByMemberId,
        CreatedByMemberLabel = log.CreatedByMemberLabel,
        SelectedDeadlineDate = log.SelectedDeadlineDate,
        SystemRecommendedDeadlineDate = log.SystemRecommendedDeadlineDate,
        MinimumDeadlineDate = log.MinimumDeadlineDate,
        OrderCapacityUnits = log.OrderCapacityUnits,
        RulesBypassedJson = log.RulesBypassedJson,
        OverrideReason = log.OverrideReason,
        RecommendationLogId = log.RecommendationLogId,
        ExistingDailyCapacityUsed = log.ExistingDailyCapacityUsed,
        ExistingWeeklyCapacityUsed = log.ExistingWeeklyCapacityUsed,
        DailyCapacityLimitUsed = log.DailyCapacityLimitUsed,
        WeeklyCapacityLimitUsed = log.WeeklyCapacityLimitUsed,
        DailyCapacityAfterOverride = log.DailyCapacityAfterOverride,
        WeeklyCapacityAfterOverride = log.WeeklyCapacityAfterOverride,
        CalendarReason = log.CalendarReason
    };

    private static DeadlineOverrideLog ToDomain(SchedulingDeadlineOverrideLogEntity e) => new(
        e.Id,
        e.OrderId,
        e.OrderCode,
        e.CreatedAtUtc,
        e.CreatedByOrganizationType,
        e.CreatedByOrganizationCode,
        e.CreatedByMemberId,
        e.CreatedByMemberLabel,
        e.SelectedDeadlineDate,
        e.SystemRecommendedDeadlineDate,
        e.MinimumDeadlineDate,
        e.OrderCapacityUnits,
        e.RulesBypassedJson,
        e.OverrideReason,
        e.RecommendationLogId,
        e.ExistingDailyCapacityUsed,
        e.ExistingWeeklyCapacityUsed,
        e.DailyCapacityLimitUsed,
        e.WeeklyCapacityLimitUsed,
        e.DailyCapacityAfterOverride,
        e.WeeklyCapacityAfterOverride,
        e.CalendarReason,
        string.IsNullOrWhiteSpace(e.EntityType) ? "order" : e.EntityType,
        e.ReservationId);
}
