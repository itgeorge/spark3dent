namespace Orders;

public sealed record MaterialOption(
    Material Material,
    string Title,
    string Description,
    int SortOrder);

public static class MaterialOptions
{
    public static readonly IReadOnlyList<MaterialOption> All = ValidateCoverage(
    [
        new(Material.FullContourZirconia, "Zirconia", "Full contour zirconia crown/bridge", 10),
        new(Material.PfzLayeredZrCrown, "Layered zirconia", "PFZ / ceramic layered on ZR", 20),
        new(Material.Pfm, "Metal-ceramic", "PFM crown/bridge", 30),
        new(Material.GlassCeramics, "Glass ceramics", "High-esthetic ceramic case", 40),
        new(Material.Pmma, "Standard PMMA", "Temporary PMMA crown/bridge", 50),
        new(Material.PmmaTelio, "PMMA Telio", "Stronger cross-linked PMMA temporary", 60)
    ]);

    public static MaterialOption Get(Material material) =>
        All.FirstOrDefault(x => x.Material == material)
        ?? throw new InvalidOperationException($"Unknown material option '{material}'.");

    private static IReadOnlyList<MaterialOption> ValidateCoverage(IReadOnlyList<MaterialOption> all)
    {
        var expected = Enum.GetValues<Material>().OrderBy(x => x).ToArray();
        var actual = all.Select(x => x.Material).OrderBy(x => x).ToArray();
        var duplicates = all.GroupBy(x => x.Material).Where(g => g.Count() > 1).Select(g => g.Key).ToArray();
        if (duplicates.Length > 0)
            throw new InvalidOperationException($"MaterialOptions.All contains duplicate entries for: {string.Join(", ", duplicates)}.");
        if (!actual.SequenceEqual(expected))
        {
            var missing = expected.Except(actual).ToArray();
            var unexpected = actual.Except(expected).ToArray();
            throw new InvalidOperationException($"MaterialOptions.All must cover every Material enum value exactly once. Missing: {string.Join(", ", missing)}. Unexpected: {string.Join(", ", unexpected)}.");
        }

        return all;
    }
}
