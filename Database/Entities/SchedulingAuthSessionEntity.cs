namespace Database.Entities;

public class SchedulingAuthSessionEntity
{
    public string Id { get; set; } = string.Empty;
    public string ClinicCode { get; set; } = string.Empty;
    public string CredentialId { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? AbsoluteExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string CreatedIp { get; set; } = string.Empty;
    public string CreatedUserAgent { get; set; } = string.Empty;
}
