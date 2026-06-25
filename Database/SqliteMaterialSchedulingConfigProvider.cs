using Database.Entities;
using Microsoft.EntityFrameworkCore;
using Orders;

namespace Database;

public sealed class SqliteMaterialSchedulingConfigProvider : IMaterialSchedulingConfigAdminRepository
{
    private readonly Func<AppDbContext> _contextFactory;

    public SqliteMaterialSchedulingConfigProvider(Func<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<MaterialSchedulingConfig> GetLatestAsync(Material material, CancellationToken ct = default)
    {
        await using var ctx = _contextFactory();
        var entity = await ctx.SchedulingMaterialConfigs.AsNoTracking()
            .Where(c => c.Material == material)
            .OrderByDescending(c => c.ActiveFromDate)
            .ThenByDescending(c => c.Id)
            .FirstOrDefaultAsync(ct);
        if (entity == null)
            throw new InvalidOperationException($"Material scheduling config is missing for {material}.");
        return ToDomain(entity);
    }

    public async Task<MaterialSchedulingConfig> GetForDateAsync(Material material, DateOnly deadlineDate, CancellationToken ct = default)
    {
        await using var ctx = _contextFactory();
        var entity = await ctx.SchedulingMaterialConfigs.AsNoTracking()
            .Where(c => c.Material == material && c.ActiveFromDate <= deadlineDate)
            .OrderByDescending(c => c.ActiveFromDate)
            .ThenByDescending(c => c.Id)
            .FirstOrDefaultAsync(ct);
        if (entity == null)
            throw new InvalidOperationException($"Material scheduling config is missing for {material} on {deadlineDate:yyyy-MM-dd}.");
        return ToDomain(entity);
    }

    public async Task<IReadOnlyList<MaterialSchedulingConfig>> ListAsync(CancellationToken ct = default)
    {
        await using var ctx = _contextFactory();
        var entities = await ctx.SchedulingMaterialConfigs.AsNoTracking()
            .OrderByDescending(c => c.ActiveFromDate)
            .ThenByDescending(c => c.Id)
            .ToListAsync(ct);
        return entities
            .GroupBy(c => c.Material)
            .Select(g => g.First())
            .OrderBy(MaterialSortKey)
            .Select(ToDomain)
            .ToArray();
    }

    public async Task<IReadOnlyList<MaterialSchedulingConfigAdminRecord>> ListAdminAsync(CancellationToken ct = default)
    {
        await using var ctx = _contextFactory();
        var entities = await ctx.SchedulingMaterialConfigs.AsNoTracking()
            .OrderByDescending(c => c.ActiveFromDate)
            .ThenByDescending(c => c.Id)
            .ToListAsync(ct);
        return entities
            .GroupBy(c => c.Material)
            .Select(g => g.First())
            .OrderBy(MaterialSortKey)
            .Select(ToAdminRecord)
            .ToArray();
    }

    public async Task<IReadOnlyList<MaterialSchedulingConfigAdminRecord>> ListHistoryAsync(Material material, int offset = 0, int limit = 25, CancellationToken ct = default)
    {
        await using var ctx = _contextFactory();
        offset = Math.Max(0, offset);
        limit = Math.Clamp(limit, 1, 100);
        var entities = await ctx.SchedulingMaterialConfigs.AsNoTracking()
            .Where(c => c.Material == material)
            .OrderByDescending(c => c.ActiveFromDate)
            .ThenByDescending(c => c.Id)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(ct);
        return entities.Select(ToAdminRecord).ToArray();
    }

    public async Task<MaterialSchedulingConfigAdminRecord> CreateAsync(Material material, MaterialSchedulingConfigCreate create, DateTimeOffset now, CancellationToken ct = default)
    {
        SchedulingConfigValidation.Validate(material, create);
        await using var ctx = _contextFactory();
        if (await ctx.SchedulingMaterialConfigs.AnyAsync(c => c.Material == material, ct))
            throw new MaterialSchedulingConfigAlreadyExistsException(material);

        var entity = new SchedulingMaterialConfigEntity
        {
            Material = material,
            ActiveFromDate = ToLabLocalDate(now),
            FixedLeadTimeBusinessDays = create.FixedLeadTimeBusinessDays,
            CapacityUnitsPerTooth = create.CapacityUnitsPerTooth,
            TeethPerExtraLeadDay = create.TeethPerExtraLeadDay,
            CreatedAt = now,
            UpdatedAt = now
        };
        ctx.SchedulingMaterialConfigs.Add(entity);
        await ctx.SaveChangesAsync(ct);
        return ToAdminRecord(entity);
    }

    public async Task<MaterialSchedulingConfigAdminRecord> UpdateAsync(Material material, MaterialSchedulingConfigUpdate update, DateTimeOffset now, CancellationToken ct = default)
    {
        SchedulingConfigValidation.Validate(material, update);
        await using var ctx = _contextFactory();
        var activeFromDate = ToLabLocalDate(now);
        var entity = await ctx.SchedulingMaterialConfigs.FirstOrDefaultAsync(c => c.Material == material && c.ActiveFromDate == activeFromDate, ct);
        if (entity == null)
        {
            if (!await ctx.SchedulingMaterialConfigs.AnyAsync(c => c.Material == material, ct))
                throw new KeyNotFoundException("Material scheduling config not found.");

            entity = new SchedulingMaterialConfigEntity
            {
                Material = material,
                ActiveFromDate = activeFromDate,
                CreatedAt = now
            };
            ctx.SchedulingMaterialConfigs.Add(entity);
        }

        entity.FixedLeadTimeBusinessDays = update.FixedLeadTimeBusinessDays;
        entity.CapacityUnitsPerTooth = update.CapacityUnitsPerTooth;
        entity.TeethPerExtraLeadDay = update.TeethPerExtraLeadDay;
        entity.UpdatedAt = now;
        await ctx.SaveChangesAsync(ct);
        return ToAdminRecord(entity);
    }

    private static MaterialSchedulingConfig ToDomain(SchedulingMaterialConfigEntity entity) =>
        new(
            entity.Material,
            entity.FixedLeadTimeBusinessDays,
            entity.CapacityUnitsPerTooth,
            entity.TeethPerExtraLeadDay,
            entity.ActiveFromDate);

    private static MaterialSchedulingConfigAdminRecord ToAdminRecord(SchedulingMaterialConfigEntity entity) =>
        new(
            entity.Material,
            entity.FixedLeadTimeBusinessDays,
            entity.CapacityUnitsPerTooth,
            entity.TeethPerExtraLeadDay,
            entity.ActiveFromDate,
            entity.CreatedAt,
            entity.UpdatedAt);

    private static int MaterialSortKey(SchedulingMaterialConfigEntity entity) => MaterialOptions.Get(entity.Material).SortOrder;

    private static DateOnly ToLabLocalDate(DateTimeOffset timestamp) =>
        DateOnly.FromDateTime(LabTimeZone.ToLabLocal(timestamp).DateTime);
}
