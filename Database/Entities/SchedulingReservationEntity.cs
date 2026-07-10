using Orders;

namespace Database.Entities;

public class SchedulingReservationEntity
{
    public long Id { get; set; }
    public string ClinicCode { get; set; } = string.Empty;
    public string ClinicDisplayName { get; set; } = string.Empty;
    public string MemberId { get; set; } = string.Empty;
    public string MemberLabel { get; set; } = string.Empty;
    public string MemberPinHashFingerprint { get; set; } = string.Empty;
    public string CaseName { get; set; } = string.Empty;
    public DateOnly ImpressionDate { get; set; }
    public string ProductCategory { get; set; } = string.Empty;
    public string Material { get; set; } = string.Empty;
    public string WorkItemsJson { get; set; } = string.Empty;
    public DateOnly RequestedDeliveryDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public Shade Shade { get; set; }
    public string? Notes { get; set; }
    public string? ColorNote { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public long CreatedAtUnixTimeMilliseconds { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public decimal? CalculatedCapacityUnits { get; set; }
    public string CreatedIp { get; set; } = string.Empty;
    public string CreatedUserAgent { get; set; } = string.Empty;
    public long? PromotedOrderId { get; set; }
    public string? PromotedOrderCode { get; set; }
    public DateTimeOffset? PromotedAt { get; set; }
}
