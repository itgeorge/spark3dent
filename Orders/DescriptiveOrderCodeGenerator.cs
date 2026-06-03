using System.Security.Cryptography;

namespace Orders;

public sealed class DescriptiveOrderCodeGenerator : IOrderCodeGenerator
{
    private const string SuffixAlphabet = "23456789ACDEFGHJKMNPQRSTWXYZ";

    public string Generate(OrderDraft draft) => $"{BuildStem(draft)}-{GenerateSuffix()}";

    private static string BuildStem(OrderDraft draft)
    {
        var delivery = draft.RequestedDeliveryDate;
        var yearPrefix = (delivery.Year % 100).ToString("00");
        var datePart = delivery.ToString("ddMM");
        var materialCode = MaterialCode(draft.Material);
        var toothCount = draft.TeethRange.Teeth.Length;
        return $"{yearPrefix}-{datePart}-{materialCode}{toothCount}";
    }

    private static char MaterialCode(Material material) => material switch
    {
        Material.Pfm => 'M',
        Material.FullContourZirconia => 'Z',
        Material.PfzLayeredZrCrown => 'L',
        Material.GlassCeramics => 'G',
        Material.Pmma => 'P',
        _ => throw new ArgumentOutOfRangeException(nameof(material), material, "Unsupported material for order code generation.")
    };

    private static string GenerateSuffix()
    {
        Span<byte> bytes = stackalloc byte[2];
        RandomNumberGenerator.Fill(bytes);
        var chars = new char[2];
        for (var i = 0; i < chars.Length; i++)
            chars[i] = SuffixAlphabet[bytes[i] % SuffixAlphabet.Length];
        return new string(chars);
    }
}
