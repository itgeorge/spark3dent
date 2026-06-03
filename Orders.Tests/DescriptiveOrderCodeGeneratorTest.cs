using Orders;

namespace Orders.Tests;

[Parallelizable(ParallelScope.All)]
public class DescriptiveOrderCodeGeneratorTest
{
    private readonly DescriptiveOrderCodeGenerator _generator = new();

    [Test]
    public void Generate_GivenMetalCeramicCrownFor29May_ProducesExpectedFormat()
    {
        var draft = CreateDraft(
            Material.Pfm,
            ConstructionType.Crown,
            new ToothRange(11, 11),
            new DateOnly(2026, 5, 29));

        var code = _generator.Generate(draft);

        AssertDescriptiveCode(code, "26-2905-M1");
    }

    [Test]
    public void Generate_GivenZirconiaBridgeFor13November_ProducesExpectedFormat()
    {
        var draft = CreateDraft(
            Material.FullContourZirconia,
            ConstructionType.Bridge,
            new ToothRange(12, 22),
            new DateOnly(2026, 11, 13));

        var code = _generator.Generate(draft);

        AssertDescriptiveCode(code, "26-1311-Z4");
    }

    [Test]
    public void Generate_GivenZirconiaFacetFor13November_UsesToothCountNotConstructionType()
    {
        var draft = CreateDraft(
            Material.FullContourZirconia,
            ConstructionType.Facet,
            new ToothRange(12, 22),
            new DateOnly(2026, 11, 13));

        var code = _generator.Generate(draft);

        AssertDescriptiveCode(code, "26-1311-Z4");
    }

    [TestCase(Material.Pfm, 'M')]
    [TestCase(Material.FullContourZirconia, 'Z')]
    [TestCase(Material.PfzLayeredZrCrown, 'L')]
    [TestCase(Material.GlassCeramics, 'G')]
    [TestCase(Material.Pmma, 'P')]
    public void Generate_GivenKnownMaterial_IncludesExpectedMaterialLetter(Material material, char expected)
    {
        var draft = CreateDraft(
            material,
            ConstructionType.Crown,
            new ToothRange(11, 11),
            new DateOnly(2026, 5, 29));

        var code = _generator.Generate(draft);

        AssertDescriptiveCode(code, $"26-2905-{expected}1");
    }

    [Test]
    public void Generate_GivenBridgeWithMoreThanNineTeeth_UsesTwoDigitToothCount()
    {
        var draft = CreateDraft(
            Material.FullContourZirconia,
            ConstructionType.Bridge,
            new ToothRange(18, 22),
            new DateOnly(2026, 11, 13));

        var code = _generator.Generate(draft);

        AssertDescriptiveCode(code, "26-1311-Z10");
    }

    [Test]
    public void ToShortenedCode_GivenDescriptiveOrderCode_DropsYearPrefix()
    {
        var draft = CreateDraft(
            Material.Pfm,
            ConstructionType.Crown,
            new ToothRange(11, 11),
            new DateOnly(2026, 5, 29));

        var code = _generator.Generate(draft);

        Assert.That(DescriptiveOrderCodeGenerator.ToShortenedCode(code), Is.EqualTo(code[3..]));
        AssertDescriptiveCode(code, "26-2905-M1");
        Assert.That(DescriptiveOrderCodeGenerator.ToShortenedCode("26-2905-M1K7"), Is.EqualTo("2905-M1K7"));
    }

    [Test]
    public void Generate_WhenCalledRepeatedly_ProducesDistinctSuffixes()
    {
        var draft = CreateDraft(
            Material.Pfm,
            ConstructionType.Crown,
            new ToothRange(11, 11),
            new DateOnly(2026, 5, 29));

        var codes = Enumerable.Range(0, 42).Select(_ => _generator.Generate(draft)).ToArray();
        var suffixes = codes.Select(code => code[^2..]).ToHashSet(StringComparer.Ordinal);

        Assert.That(suffixes, Has.Count.GreaterThan(1));
        Assert.That(codes, Has.All.StartWith("26-2905-M1"));
        Assert.That(codes, Has.All.Length.EqualTo("26-2905-M1".Length + 2));
    }

    private static void AssertDescriptiveCode(string code, string expectedStem)
    {
        Assert.Multiple(() =>
        {
            Assert.That(code, Does.StartWith(expectedStem));
            Assert.That(code, Has.Length.EqualTo(expectedStem.Length + 2));
        });
    }

    private static OrderDraft CreateDraft(
        Material material,
        ConstructionType constructionType,
        ToothRange teethRange,
        DateOnly requestedDeliveryDate) =>
        new(
            "Case 42",
            new DateOnly(2026, 6, 2),
            material == Material.Pmma ? ProductCategory.Temporary : ProductCategory.Permanent,
            material == Material.Pmma ? WorkType.TemporaryCrownBridge : WorkType.Crown,
            material,
            constructionType,
            teethRange,
            requestedDeliveryDate,
            Shade.A3,
            null);
}
