namespace Orders;

public static class SchedulingConfigValidator
{
    public static void Validate(SchedulingOptions options)
    {
        if (options.SessionSlidingDays <= 0) throw new InvalidOperationException("SessionSlidingDays must be positive.");
        if (options.DefaultMinBusinessDays < 0) throw new InvalidOperationException("DefaultMinBusinessDays must be non-negative.");

        var clinicCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var clinic in options.Clinics)
            ValidateClinic(clinic, clinicCodes);

        foreach (var rule in options.WorkRules)
        {
            if (rule.MinBusinessDays < 0)
                throw new InvalidOperationException("Work-rule MinBusinessDays must be non-negative.");
        }
    }

    private static void ValidateClinic(ClinicConfig clinic, HashSet<string> clinicCodes)
    {
        if (string.IsNullOrWhiteSpace(clinic.Code))
            throw new InvalidOperationException("Clinic code is required.");
        if (!clinicCodes.Add(clinic.Code))
            throw new InvalidOperationException($"Duplicate clinic code: {clinic.Code}");

        var credentialIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var credentialLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var credential in clinic.Credentials)
            ValidateCredential(clinic.Code, credential, credentialIds, credentialLabels);
    }

    private static void ValidateCredential(
        string clinicCode,
        ClinicCredentialConfig credential,
        HashSet<string> credentialIds,
        HashSet<string> credentialLabels)
    {
        if (string.IsNullOrWhiteSpace(credential.Id))
            throw new InvalidOperationException($"Credential id is required for clinic {clinicCode}.");
        if (!credentialIds.Add(credential.Id))
            throw new InvalidOperationException($"Duplicate credential id '{credential.Id}' for clinic {clinicCode}.");
        if (!string.IsNullOrWhiteSpace(credential.Label) && !credentialLabels.Add(credential.Label))
            throw new InvalidOperationException($"Duplicate credential label '{credential.Label}' for clinic {clinicCode}.");
        if (credential.IsActive && string.IsNullOrWhiteSpace(credential.PinHash))
            throw new InvalidOperationException($"Active credential '{credential.Id}' must have a PIN hash.");
    }
}
