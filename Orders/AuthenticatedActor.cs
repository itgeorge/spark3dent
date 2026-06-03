namespace Orders;

public enum ActorRole
{
    Clinic,
    Technician
}

public sealed record AuthenticatedActor(
    string ClinicCode,
    string ClinicDisplayName,
    string CredentialId,
    string CredentialLabel,
    string CredentialPinHashFingerprint,
    string SessionId,
    ActorRole Role = ActorRole.Clinic)
{
    public bool IsTechnician => Role == ActorRole.Technician;
}
