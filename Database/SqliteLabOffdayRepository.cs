using Database.Entities;
using Microsoft.EntityFrameworkCore;
using Orders;

namespace Database;

public sealed class SqliteLabOffdayRepository : ILabOffdayRepository
{
    private readonly Func<AppDbContext> _contextFactory;

    public SqliteLabOffdayRepository(Func<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<IReadOnlyList<LabOffdayRecord>> ListIntersectingAsync(DateOnly start, DateOnly end, CancellationToken ct = default)
    {
        if (end < start) throw new InvalidOperationException("Lab offday end date must be on or after start date.");

        await using var ctx = _contextFactory();
        var entities = await ctx.SchedulingLabOffdays.AsNoTracking()
            .Where(x => x.StartDate <= end && start <= x.EndDate)
            .OrderBy(x => x.StartDate)
            .ThenBy(x => x.EndDate)
            .ThenBy(x => x.Id)
            .ToListAsync(ct);
        return entities.Select(ToDomain).ToArray();
    }

    public async Task<IReadOnlyList<LabOffdayRecord>> ListAllAsync(CancellationToken ct = default)
    {
        await using var ctx = _contextFactory();
        var entities = await ctx.SchedulingLabOffdays.AsNoTracking()
            .OrderByDescending(x => x.StartDate)
            .ThenByDescending(x => x.EndDate)
            .ThenByDescending(x => x.Id)
            .ToListAsync(ct);
        return entities.Select(ToDomain).ToArray();
    }

    public async Task<LabOffdayRecord> CreateAsync(LabOffdayCreate create, DateTimeOffset now, CancellationToken ct = default)
    {
        SchedulingConfigValidation.Validate(create);
        await using var ctx = _contextFactory();
        if (await HasOverlapAsync(ctx, create.StartDate, create.EndDate, excludedId: null, ct))
            throw new LabOffdayOverlapException(create.StartDate, create.EndDate);

        var entity = new SchedulingLabOffdayEntity
        {
            StartDate = create.StartDate,
            EndDate = create.EndDate,
            CreatedAt = now,
            UpdatedAt = now
        };
        ctx.SchedulingLabOffdays.Add(entity);
        await ctx.SaveChangesAsync(ct);
        return ToDomain(entity);
    }

    public async Task<LabOffdayRecord> UpdateAsync(long id, LabOffdayUpdate update, DateTimeOffset now, CancellationToken ct = default)
    {
        SchedulingConfigValidation.Validate(update);
        await using var ctx = _contextFactory();
        var entity = await ctx.SchedulingLabOffdays.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new KeyNotFoundException($"Lab offday {id} was not found.");
        if (await HasOverlapAsync(ctx, update.StartDate, update.EndDate, id, ct))
            throw new LabOffdayOverlapException(update.StartDate, update.EndDate);

        entity.StartDate = update.StartDate;
        entity.EndDate = update.EndDate;
        entity.UpdatedAt = now;
        await ctx.SaveChangesAsync(ct);
        return ToDomain(entity);
    }

    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        await using var ctx = _contextFactory();
        var entity = await ctx.SchedulingLabOffdays.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new KeyNotFoundException($"Lab offday {id} was not found.");
        ctx.SchedulingLabOffdays.Remove(entity);
        await ctx.SaveChangesAsync(ct);
    }

    private static Task<bool> HasOverlapAsync(AppDbContext ctx, DateOnly start, DateOnly end, long? excludedId, CancellationToken ct) =>
        ctx.SchedulingLabOffdays.AnyAsync(x =>
            x.StartDate <= end && start <= x.EndDate && (!excludedId.HasValue || x.Id != excludedId.Value), ct);

    private static LabOffdayRecord ToDomain(SchedulingLabOffdayEntity entity) =>
        new(entity.Id, entity.StartDate, entity.EndDate, entity.CreatedAt, entity.UpdatedAt);
}
