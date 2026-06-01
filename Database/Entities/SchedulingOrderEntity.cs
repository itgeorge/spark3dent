namespace Database.Entities;

public class SchedulingOrderEntity
{
    public long Id { get; set; }
    public string OrderCode { get; set; } = string.Empty;
    public string ClinicCode { get; set; } = string.Empty;
    public string ClinicDisplayName { get; set; } = string.Empty;
    public string CredentialId { get; set; } = string.Empty;
    public string CredentialLabel { get; set; } = string.Empty;
    public string CredentialPinHashFingerprint { get; set; } = string.Empty;
    public string CaseName { get; set; } = string.Empty;
    public DateOnly ImpressionDate { get; set; }
    public string ProductCategory { get; set; } = string.Empty;
    public string WorkType { get; set; } = string.Empty;
    public string Material { get; set; } = string.Empty;
    public string ConstructionType { get; set; } = string.Empty;
    public int ToothStart { get; set; }
    public int ToothEnd { get; set; }
    public string AbutmentTeeth { get; set; } = string.Empty;
    public DateOnly RequestedDeliveryDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Shade { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public long CreatedAtUnixTimeMilliseconds { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string CreatedIp { get; set; } = string.Empty;
    public string CreatedUserAgent { get; set; } = string.Empty;
}
