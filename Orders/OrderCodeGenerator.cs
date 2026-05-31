using System.Security.Cryptography;

namespace Orders;

public interface IOrderCodeGenerator
{
    string Generate();
}

public sealed class SafeOrderCodeGenerator : IOrderCodeGenerator
{
    private const string Alphabet = "23456789ACDEFGHJKMNPQRSTWXYZ";

    public string Generate()
    {
        Span<byte> bytes = stackalloc byte[6];
        RandomNumberGenerator.Fill(bytes);
        var chars = new char[6];
        for (var i = 0; i < chars.Length; i++)
            chars[i] = Alphabet[bytes[i] % Alphabet.Length];
        return $"{new string(chars, 0, 3)}-{new string(chars, 3, 3)}";
    }
}
