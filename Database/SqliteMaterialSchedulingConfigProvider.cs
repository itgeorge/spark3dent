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

    public async Task<MaterialSchedulingConfig> GetAsync(Material material, CancellationToken ct = default)
    {
        await using var ctx = _contextFactory();
        var materialName = material.ToString();
        var entity = await ctx.SchedulingMaterialConfigs.AsNoTracking()
            .Where(c => c.Material == materialName)
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
        var materialName = material.ToString();
        var entity = await ctx.SchedulingMaterialConfigs.AsNoTracking()
            .Where(c => c.Material == materialName && c.ActiveFromDate <= deadlineDate)
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
            .GroupBy(c => c.Material, StringComparer.Ordinal)
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
            .GroupBy(c => c.Material, StringComparer.Ordinal)
            .Select(g => g.First())
            .OrderBy(MaterialSortKey)
            .Select(ToAdminRecord)
            .ToArray();
    }

    public async Task<IReadOnlyList<MaterialSchedulingConfigAdminRecord>> ListHistoryAsync(Material material, int offset = 0, int limit = 25, CancellationToken ct = default)
    {
        await using var ctx = _contextFactory();
        var materialName = material.ToString();
        offset = Math.Max(0, offset);
        limit = Math.Clamp(limit, 1, 100);
        var entities = await ctx.SchedulingMaterialConfigs.AsNoTracking()
            .Where(c => c.Material == materialName)
            .OrderByDescending(c => c.ActiveFromDate)
            .ThenByDescending(c => c.Id)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(ct);
        return entities.Select(ToAdminRecord).ToArray();
    }

    public async Task<MaterialSchedulingConfigAdminRecord> UpdateAsync(Material material, MaterialSchedulingConfigUpdate update, DateTimeOffset now, CancellationToken ct = default)
    {
        SchedulingConfigValidation.Validate(material, update);
        await using var ctx = _contextFactory();
        var materialName = material.ToString();
        var activeFromDate = DateOnly.FromDateTime(now.UtcDateTime.Date);
        var entity = await ctx.SchedulingMaterialConfigs.FirstOrDefaultAsync(c => c.Material == materialName && c.ActiveFromDate == activeFromDate, ct);
        if (entity == null)
        {
            var latest = await ctx.SchedulingMaterialConfigs
                .Where(c => c.Material == materialName)
                .OrderByDescending(c => c.ActiveFromDate)
                .ThenByDescending(c => c.Id)
                .FirstOrDefaultAsync(ct);
            if (latest == null)
                throw new KeyNotFoundException("Material scheduling config not found.");

            entity = new SchedulingMaterialConfigEntity
            {
                Material = materialName,
                ActiveFromDate = activeFromDate,
                IsActive = latest.IsActive,
                SortOrder = latest.SortOrder,
                CreatedAt = now
            };
            ctx.SchedulingMaterialConfigs.Add(entity);
        }

        entity.DisplayName = string.IsNullOrWhiteSpace(update.DisplayName) ? null : update.DisplayName.Trim();
        entity.FixedLeadTimeBusinessDays = update.FixedLeadTimeBusinessDays;
        entity.CapacityUnitsPerTooth = update.CapacityUnitsPerTooth;
        entity.TeethPerExtraLeadDay = update.TeethPerExtraLeadDay;
        entity.UpdatedAt = now;
        await ctx.SaveChangesAsync(ct);
        return ToAdminRecord(entity);
    }

    private static MaterialSchedulingConfig ToDomain(SchedulingMaterialConfigEntity entity)
    {
        var material = ParseMaterial(entity.Material);

        return new MaterialSchedulingConfig(
            material,
            entity.DisplayName,
            entity.FixedLeadTimeBusinessDays,
            entity.CapacityUnitsPerTooth,
            entity.TeethPerExtraLeadDay,
            entity.IsActive,
            entity.SortOrder,
            entity.ActiveFromDate);
    }

    private static MaterialSchedulingConfigAdminRecord ToAdminRecord(SchedulingMaterialConfigEntity entity)
    {
        var material = ParseMaterial(entity.Material);
        return new MaterialSchedulingConfigAdminRecord(
            material,
            entity.DisplayName,
            entity.FixedLeadTimeBusinessDays,
            entity.CapacityUnitsPerTooth,
            entity.TeethPerExtraLeadDay,
            entity.IsActive,
            entity.SortOrder,
            entity.ActiveFromDate,
            entity.CreatedAt,
            entity.UpdatedAt);
    }

    private static Material ParseMaterial(string materialName)
    {
        if (!Enum.TryParse<Material>(materialName, ignoreCase: false, out var material))
            throw new InvalidOperationException($"Material scheduling config contains unknown material '{materialName}'.");
        return material;
    }

    private static Material MaterialSortKey(SchedulingMaterialConfigEntity entity) => ParseMaterial(entity.Material);
}
