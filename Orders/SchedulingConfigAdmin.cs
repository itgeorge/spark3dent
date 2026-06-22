namespace Orders;

public sealed record MaterialSchedulingConfigCreate(
    int FixedLeadTimeBusinessDays,
    decimal CapacityUnitsPerTooth,
    int? TeethPerExtraLeadDay);

public sealed record MaterialSchedulingConfigUpdate(
    int FixedLeadTimeBusinessDays,
    decimal CapacityUnitsPerTooth,
    int? TeethPerExtraLeadDay);

public sealed record MaterialSchedulingConfigAdminRecord(
    Material Material,
    int FixedLeadTimeBusinessDays,
    decimal CapacityUnitsPerTooth,
    int? TeethPerExtraLeadDay,
    DateOnly ActiveFromDate,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public interface IMaterialSchedulingConfigAdminRepository : IMaterialSchedulingConfigProvider
{
    Task<IReadOnlyList<MaterialSchedulingConfigAdminRecord>> ListAdminAsync(CancellationToken ct = default);
    Task<IReadOnlyList<MaterialSchedulingConfigAdminRecord>> ListHistoryAsync(Material material, int offset = 0, int limit = 25, CancellationToken ct = default);
    Task<MaterialSchedulingConfigAdminRecord> CreateAsync(Material material, MaterialSchedulingConfigCreate create, DateTimeOffset now, CancellationToken ct = default);
    Task<MaterialSchedulingConfigAdminRecord> UpdateAsync(Material material, MaterialSchedulingConfigUpdate update, DateTimeOffset now, CancellationToken ct = default);
}

public sealed class MaterialSchedulingConfigAlreadyExistsException : InvalidOperationException
{
    public MaterialSchedulingConfigAlreadyExistsException(Material material)
        : base($"Material scheduling config already exists for {material}.")
    {
        Material = material;
    }

    public Material Material { get; }
}

public sealed record SchedulingCapacityConfigCreate(
    DateOnly ActiveFromDate,
    decimal DailyCapacityUnits,
    decimal WeeklyCapacityUnits);

public sealed record SchedulingCapacityConfigAdminRecord(
    long Id,
    DateOnly ActiveFromDate,
    decimal DailyCapacityUnits,
    decimal WeeklyCapacityUnits,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public interface ISchedulingCapacityConfigAdminRepository : ISchedulingCapacityConfigProvider
{
    Task<IReadOnlyList<SchedulingCapacityConfigAdminRecord>> ListAdminAsync(CancellationToken ct = default);
    Task<SchedulingCapacityConfigAdminRecord> CreateAsync(SchedulingCapacityConfigCreate create, DateTimeOffset now, CancellationToken ct = default);
}

public sealed class DuplicateSchedulingCapacityConfigDateException : InvalidOperationException
{
    public DuplicateSchedulingCapacityConfigDateException(DateOnly activeFromDate)
        : base($"Scheduling capacity config already exists for {activeFromDate:yyyy-MM-dd}.")
    {
        ActiveFromDate = activeFromDate;
    }

    public DateOnly ActiveFromDate { get; }
}

public static class SchedulingConfigValidation
{
    public static void Validate(Material material, MaterialSchedulingConfigCreate create)
    {
        if (create.FixedLeadTimeBusinessDays <= 0)
            throw new InvalidOperationException("Fixed lead-time business days must be positive.");
        if (create.CapacityUnitsPerTooth <= 0)
            throw new InvalidOperationException("Capacity units per tooth must be positive.");
        if (UsesToothCountExtraLeadTime(material) && (create.TeethPerExtraLeadDay is null or <= 0))
            throw new InvalidOperationException($"{material} requires positive teeth per extra lead day.");
    }

    public static void Validate(Material material, MaterialSchedulingConfigUpdate update)
    {
        if (update.FixedLeadTimeBusinessDays <= 0)
            throw new InvalidOperationException("Fixed lead-time business days must be positive.");
        if (update.CapacityUnitsPerTooth <= 0)
            throw new InvalidOperationException("Capacity units per tooth must be positive.");
        if (UsesToothCountExtraLeadTime(material) && (update.TeethPerExtraLeadDay is null or <= 0))
            throw new InvalidOperationException($"{material} requires positive teeth per extra lead day.");
    }

    public static void Validate(SchedulingCapacityConfigCreate create)
    {
        if (create.DailyCapacityUnits <= 0)
            throw new InvalidOperationException("Daily capacity units must be positive.");
        if (create.WeeklyCapacityUnits <= 0)
            throw new InvalidOperationException("Weekly capacity units must be positive.");
    }

    public static bool UsesToothCountExtraLeadTime(Material material) =>
        material is Material.Pfm or Material.PfzLayeredZrCrown;
}
