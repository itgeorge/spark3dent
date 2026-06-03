using System.Security.Cryptography;
using System.Text;

namespace Orders;

public sealed class PinHasher
{
    private const int SaltBytes = 16;
    private const int HashBytes = 32;
    private const int DefaultIterations = 210_000;
    private readonly string _pepper;

    public PinHasher(string? pepper = null) => _pepper = pepper ?? "";

    public string Hash(string pin, int iterations = DefaultIterations)
    {
        ValidatePinShape(pin);
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Derive(pin, salt, iterations);
        return $"pbkdf2-sha256${iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public bool Verify(string pin, string storedHash)
    {
        if (string.IsNullOrWhiteSpace(pin) || string.IsNullOrWhiteSpace(storedHash)) return false;
        var parts = storedHash.Split('$');
        if (parts.Length != 4 || parts[0] != "pbkdf2-sha256") return false;
        if (!int.TryParse(parts[1], out var iterations) || iterations <= 0) return false;

        if (!TryReadHashParts(parts, out var salt, out var expected)) return false;

        var actual = Derive(pin, salt, iterations);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    public static string Fingerprint(string storedHash)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(storedHash));
        return Convert.ToHexString(bytes)[..16];
    }

    public static void ValidatePinShape(string pin)
    {
        if (pin.Length != 6 || pin.Any(ch => ch < '0' || ch > '9'))
            throw new InvalidOperationException("Invalid credential secret.");
    }

    private byte[] Derive(string pin, byte[] salt, int iterations)
    {
        var material = Encoding.UTF8.GetBytes(pin + ":" + _pepper);
        return Rfc2898DeriveBytes.Pbkdf2(material, salt, iterations, HashAlgorithmName.SHA256, HashBytes);
    }

    private static bool TryReadHashParts(string[] parts, out byte[] salt, out byte[] expected)
    {
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
            return true;
        }
        catch
        {
            salt = [];
            expected = [];
            return false;
        }
    }
}
