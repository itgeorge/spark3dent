namespace Orders;

public sealed record MaterialSchedulingConfig(
    Material Material,
    string? DisplayName,
    int FixedLeadTimeBusinessDays,
    decimal CapacityUnitsPerTooth,
    int? TeethPerExtraLeadDay,
    bool IsActive,
    int SortOrder,
    DateOnly ActiveFromDate = default);

public interface IMaterialSchedulingConfigProvider
{
    Task<MaterialSchedulingConfig> GetAsync(Material material, CancellationToken ct = default);
    Task<MaterialSchedulingConfig> GetForDateAsync(Material material, DateOnly deadlineDate, CancellationToken ct = default);
    Task<IReadOnlyList<MaterialSchedulingConfig>> ListAsync(CancellationToken ct = default);
}
