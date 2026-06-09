namespace Orders;

public sealed record SchedulingOrganization(
    OrganizationType OrganizationType,
    string Code,
    string DisplayName,
    bool IsActive);

public sealed record SchedulingLab(
    int Id,
    string Code,
    string DisplayName,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record SchedulingClinic(
    string Code,
    string DisplayName,
    string? LinkedClientNickname,
    string? DisplayColor,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record SchedulingMember(
    OrganizationType OrganizationType,
    string OrganizationCode,
    string Id,
    string Label,
    string PinHash,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public string PinFingerprint => PinHasher.Fingerprint(PinHash);
}
