namespace Database.Entities;

public class AuditEventEntity
{
    public long Id { get; set; }
    public string ServiceName { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string? EntityDisplay { get; set; }
    public string ActorRole { get; set; } = string.Empty;
    public string? ActorClinicCode { get; set; }
    public string? ActorCredentialId { get; set; }
    public string? ActorCredentialLabel { get; set; }
    public string? ActorSessionId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public long OccurredAtUnixTimeMilliseconds { get; set; }
    public string? Ip { get; set; }
    public string? UserAgent { get; set; }
    public string? MetadataJson { get; set; }
}
