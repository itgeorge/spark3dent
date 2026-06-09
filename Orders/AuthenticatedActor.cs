namespace Orders;

public enum OrganizationType
{
    Clinic,
    Lab
}

public sealed record AuthenticatedActor(
    OrganizationType OrganizationType,
    string OrganizationCode,
    string OrganizationName,
    string MemberId,
    string MemberLabel,
    string MemberPinHashFingerprint,
    string SessionId)
{
    public bool IsLab => OrganizationType == OrganizationType.Lab;
    public bool IsClinic => OrganizationType == OrganizationType.Clinic;
}
