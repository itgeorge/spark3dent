namespace Orders;

public sealed record AuthSession(
    string Id,
    OrganizationType OrganizationType,
    string OrganizationCode,
    string MemberId,
    string TokenHash,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? AbsoluteExpiresAt,
    DateTimeOffset? RevokedAt,
    string CreatedIp,
    string CreatedUserAgent);
