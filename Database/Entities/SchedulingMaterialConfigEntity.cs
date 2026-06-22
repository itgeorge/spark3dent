namespace Database.Entities;

public class SchedulingMaterialConfigEntity
{
    public long Id { get; set; }
    public string Material { get; set; } = string.Empty;
    public DateOnly ActiveFromDate { get; set; }
    public string? DisplayName { get; set; }
    public int FixedLeadTimeBusinessDays { get; set; }
    public decimal CapacityUnitsPerTooth { get; set; }
    public int? TeethPerExtraLeadDay { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
