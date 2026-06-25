using Orders;

namespace Database.Entities;

public class SchedulingMaterialConfigEntity
{
    public long Id { get; set; }
    public Material Material { get; set; }
    public DateOnly ActiveFromDate { get; set; }
    public int FixedLeadTimeBusinessDays { get; set; }
    public decimal CapacityUnitsPerTooth { get; set; }
    public int? TeethPerExtraLeadDay { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
