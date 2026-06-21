namespace Orders;

public sealed record MaterialSchedulingConfig(
    Material Material,
    string? DisplayName,
    int FixedLeadTimeBusinessDays,
    decimal CapacityUnitsPerTooth,
    int? TeethPerExtraLeadDay,
    bool IsActive,
    int SortOrder);

public interface IMaterialSchedulingConfigProvider
{
    Task<MaterialSchedulingConfig> GetAsync(Material material, CancellationToken ct = default);
    Task<IReadOnlyList<MaterialSchedulingConfig>> ListAsync(CancellationToken ct = default);
}
