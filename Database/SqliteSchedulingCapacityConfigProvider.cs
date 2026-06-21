using Database.Entities;
using Microsoft.EntityFrameworkCore;
using Orders;

namespace Database;

public sealed class SqliteSchedulingCapacityConfigProvider : ISchedulingCapacityConfigProvider
{
    private readonly Func<AppDbContext> _contextFactory;

    public SqliteSchedulingCapacityConfigProvider(Func<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<SchedulingCapacityConfig> GetForDateAsync(DateOnly date, CancellationToken ct = default)
    {
        await using var ctx = _contextFactory();
        var entity = await ctx.SchedulingCapacityConfigs.AsNoTracking()
            .Where(c => c.ActiveFromDate <= date)
            .OrderByDescending(c => c.ActiveFromDate)
            .ThenByDescending(c => c.Id)
            .FirstOrDefaultAsync(ct);
        if (entity == null)
            throw new InvalidOperationException($"Scheduling capacity config is missing for {date:yyyy-MM-dd}.");
        return ToDomain(entity);
    }

    public async Task<IReadOnlyList<SchedulingCapacityConfig>> ListAsync(CancellationToken ct = default)
    {
        await using var ctx = _contextFactory();
        var entities = await ctx.SchedulingCapacityConfigs.AsNoTracking()
            .OrderBy(c => c.ActiveFromDate)
            .ThenBy(c => c.Id)
            .ToListAsync(ct);
        return entities.Select(ToDomain).ToArray();
    }

    private static SchedulingCapacityConfig ToDomain(SchedulingCapacityConfigEntity entity) =>
        new(entity.Id, entity.ActiveFromDate, entity.DailyCapacityUnits, entity.WeeklyCapacityUnits);
}
