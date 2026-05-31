namespace Orders;

public sealed record AuthenticatedActor(
    string ClinicCode,
    string ClinicDisplayName,
    string CredentialId,
    string CredentialLabel,
    string CredentialPinHashFingerprint,
    string SessionId);
