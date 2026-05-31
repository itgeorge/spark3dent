namespace Orders;

public sealed record AuthSession(
    string Id,
    string ClinicCode,
    string CredentialId,
    string TokenHash,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? AbsoluteExpiresAt,
    DateTimeOffset? RevokedAt,
    string CreatedIp,
    string CreatedUserAgent);
