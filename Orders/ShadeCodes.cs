namespace Orders;

public static class ShadeCodes
{
    private static readonly IReadOnlyDictionary<string, Shade> ByCode =
        new Dictionary<string, Shade>(StringComparer.OrdinalIgnoreCase)
        {
            ["unspecified"] = Shade.Unspecified,
            ["A1"] = Shade.A1,
            ["A2"] = Shade.A2,
            ["A3"] = Shade.A3,
            ["A3.5"] = Shade.A3_5,
            ["A4"] = Shade.A4,
            ["B1"] = Shade.B1,
            ["B2"] = Shade.B2,
            ["B3"] = Shade.B3,
            ["B4"] = Shade.B4,
            ["C1"] = Shade.C1,
            ["C2"] = Shade.C2,
            ["C3"] = Shade.C3,
            ["C4"] = Shade.C4,
            ["D2"] = Shade.D2,
            ["D3"] = Shade.D3,
            ["D4"] = Shade.D4,
            ["BL1"] = Shade.BL1,
            ["BL2"] = Shade.BL2,
            ["BL3"] = Shade.BL3,
            ["BL4"] = Shade.BL4,
        };

    private static readonly IReadOnlyDictionary<Shade, string> DisplayCode = new Dictionary<Shade, string>
    {
        [Shade.Unspecified] = "unspecified",
        [Shade.A1] = "A1",
        [Shade.A2] = "A2",
        [Shade.A3] = "A3",
        [Shade.A3_5] = "A3.5",
        [Shade.A4] = "A4",
        [Shade.B1] = "B1",
        [Shade.B2] = "B2",
        [Shade.B3] = "B3",
        [Shade.B4] = "B4",
        [Shade.C1] = "C1",
        [Shade.C2] = "C2",
        [Shade.C3] = "C3",
        [Shade.C4] = "C4",
        [Shade.D2] = "D2",
        [Shade.D3] = "D3",
        [Shade.D4] = "D4",
        [Shade.BL1] = "BL1",
        [Shade.BL2] = "BL2",
        [Shade.BL3] = "BL3",
        [Shade.BL4] = "BL4",
    };

    public static string ToDisplayCode(Shade shade) =>
        DisplayCode.TryGetValue(shade, out var code)
            ? code
            : throw new ArgumentOutOfRangeException(nameof(shade), shade, "Unknown shade value.");

    public static bool TryParse(string? code, out Shade shade)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            shade = default;
            return false;
        }

        return ByCode.TryGetValue(code.Trim(), out shade);
    }
}
