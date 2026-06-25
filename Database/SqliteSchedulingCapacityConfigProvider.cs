using Database.Entities;
using Microsoft.EntityFrameworkCore;
using Orders;

namespace Database;

public sealed class SqliteSchedulingCapacityConfigProvider : ISchedulingCapacityConfigAdminRepository
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

    public async Task<IReadOnlyList<SchedulingCapacityConfigAdminRecord>> ListAdminAsync(CancellationToken ct = default)
    {
        await using var ctx = _contextFactory();
        var entities = await ctx.SchedulingCapacityConfigs.AsNoTracking()
            .OrderByDescending(c => c.ActiveFromDate)
            .ThenByDescending(c => c.Id)
            .ToListAsync(ct);
        return entities.Select(ToAdminRecord).ToArray();
    }

    public async Task<SchedulingCapacityConfigAdminRecord> CreateAsync(SchedulingCapacityConfigCreate create, DateTimeOffset now, CancellationToken ct = default)
    {
        SchedulingConfigValidation.Validate(create);
        await using var ctx = _contextFactory();
        if (await ctx.SchedulingCapacityConfigs.AnyAsync(c => c.ActiveFromDate == create.ActiveFromDate, ct))
            throw new DuplicateSchedulingCapacityConfigDateException(create.ActiveFromDate);

        var entity = new SchedulingCapacityConfigEntity
        {
            ActiveFromDate = create.ActiveFromDate,
            DailyCapacityUnits = create.DailyCapacityUnits,
            WeeklyCapacityUnits = create.WeeklyCapacityUnits,
            CreatedAt = now,
            UpdatedAt = now
        };
        ctx.SchedulingCapacityConfigs.Add(entity);
        try
        {
            await ctx.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            throw new DuplicateSchedulingCapacityConfigDateException(create.ActiveFromDate);
        }
        return ToAdminRecord(entity);
    }

    private static SchedulingCapacityConfig ToDomain(SchedulingCapacityConfigEntity entity) =>
        new(entity.Id, entity.ActiveFromDate, entity.DailyCapacityUnits, entity.WeeklyCapacityUnits);

    private static SchedulingCapacityConfigAdminRecord ToAdminRecord(SchedulingCapacityConfigEntity entity) =>
        new(entity.Id, entity.ActiveFromDate, entity.DailyCapacityUnits, entity.WeeklyCapacityUnits, entity.CreatedAt, entity.UpdatedAt);
}
