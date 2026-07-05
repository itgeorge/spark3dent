namespace Orders;

public static class OrganizationCodes
{
    public static string Normalize(string code) => code.Trim().ToUpperInvariant();
}
