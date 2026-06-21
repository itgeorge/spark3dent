namespace Orders;

public sealed record MaterialLeadTimeConfig(
    Material Material,
    int FixedLeadTimeBusinessDays,
    bool UsesToothCountExtraLeadTime);

public sealed class MaterialLeadTimeConfigProvider
{
    public const int TeethPerExtraLeadDay = 10;

    private static readonly IReadOnlyDictionary<Material, MaterialLeadTimeConfig> Configs =
        new Dictionary<Material, MaterialLeadTimeConfig>
        {
            [Material.Pmma] = new(Material.Pmma, 2, false),
            [Material.PmmaTelio] = new(Material.PmmaTelio, 2, false),
            [Material.FullContourZirconia] = new(Material.FullContourZirconia, 3, false),
            [Material.GlassCeramics] = new(Material.GlassCeramics, 4, false),
            [Material.Pfm] = new(Material.Pfm, 4, true),
            [Material.PfzLayeredZrCrown] = new(Material.PfzLayeredZrCrown, 4, true),
        };

    public MaterialLeadTimeConfig Get(Material material)
    {
        if (Configs.TryGetValue(material, out var config)) return config;
        throw new InvalidOperationException($"No lead-time configuration found for material {material}.");
    }

    public IReadOnlyList<MaterialLeadTimeConfig> ListAll() =>
        Configs.Values.OrderBy(c => c.Material.ToString(), StringComparer.Ordinal).ToArray();
}
