using Database.Entities;
using Microsoft.EntityFrameworkCore;
using Orders;

namespace Database;

public sealed class SqliteMaterialSchedulingConfigProvider : IMaterialSchedulingConfigProvider
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
        var entity = await ctx.SchedulingMaterialConfigs.AsNoTracking().FirstOrDefaultAsync(c => c.Material == materialName, ct);
        if (entity == null)
            throw new InvalidOperationException($"Material scheduling config is missing for {material}.");
        return ToDomain(entity);
    }

    public async Task<IReadOnlyList<MaterialSchedulingConfig>> ListAsync(CancellationToken ct = default)
    {
        await using var ctx = _contextFactory();
        var entities = await ctx.SchedulingMaterialConfigs.AsNoTracking()
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Material)
            .ToListAsync(ct);
        return entities.Select(ToDomain).ToArray();
    }

    private static MaterialSchedulingConfig ToDomain(SchedulingMaterialConfigEntity entity)
    {
        if (!Enum.TryParse<Material>(entity.Material, ignoreCase: false, out var material))
            throw new InvalidOperationException($"Material scheduling config contains unknown material '{entity.Material}'.");

        return new MaterialSchedulingConfig(
            material,
            entity.DisplayName,
            entity.FixedLeadTimeBusinessDays,
            entity.CapacityUnitsPerTooth,
            entity.TeethPerExtraLeadDay,
            entity.IsActive,
            entity.SortOrder);
    }
}
