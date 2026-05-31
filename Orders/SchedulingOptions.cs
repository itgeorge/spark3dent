namespace Orders;

public sealed record SchedulingOptions
{
    public int SessionSlidingDays { get; init; } = 30;
    public int? SessionAbsoluteDays { get; init; } = 180;
    public int DefaultMinBusinessDays { get; init; } = 3;
    public List<ClinicConfig> Clinics { get; init; } = [];
    public List<WorkRule> WorkRules { get; init; } = [];
}

public sealed record SchedulingConfigSnapshot(SchedulingOptions Options, DateTimeOffset LoadedAt, string SourcePath)
{
    public ClinicConfig GetClinic(string code)
    {
        var clinic = Options.Clinics.FirstOrDefault(c => string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase));
        if (clinic == null || !clinic.IsActive)
            throw new InvalidOperationException("Clinic not found or inactive.");
        return clinic;
    }

    public WorkRule FindWorkRule(ProductCategory productCategory, WorkType workType, Material material, ConstructionType constructionType)
    {
        var rule = Options.WorkRules.FirstOrDefault(r =>
            r.ProductCategory == productCategory &&
            r.WorkType == workType &&
            r.Material == material &&
            r.ConstructionType == constructionType);

        return rule ?? new WorkRule(productCategory, workType, material, constructionType, Options.DefaultMinBusinessDays);
    }
}

public interface ISchedulingConfigProvider
{
    SchedulingConfigSnapshot Current { get; }
    Task<SchedulingConfigSnapshot> ReloadAsync(CancellationToken ct = default);
}
