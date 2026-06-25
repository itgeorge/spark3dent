namespace Database.Entities;

public class SchedulingCapacityConfigEntity
{
    public long Id { get; set; }
    public DateOnly ActiveFromDate { get; set; }
    public decimal DailyCapacityUnits { get; set; }
    public decimal WeeklyCapacityUnits { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
