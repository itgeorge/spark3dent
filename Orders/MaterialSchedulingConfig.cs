namespace Orders;

public sealed record MaterialSchedulingConfig(
    Material Material,
    int FixedLeadTimeBusinessDays,
    decimal CapacityUnitsPerTooth,
    int? TeethPerExtraLeadDay,
    DateOnly ActiveFromDate = default);

public interface IMaterialSchedulingConfigProvider
{
    Task<MaterialSchedulingConfig> GetLatestAsync(Material material, CancellationToken ct = default);
    Task<MaterialSchedulingConfig> GetForDateAsync(Material material, DateOnly deadlineDate, CancellationToken ct = default);
    Task<IReadOnlyList<MaterialSchedulingConfig>> ListAsync(CancellationToken ct = default);
}
