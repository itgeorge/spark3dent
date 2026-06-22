using Orders;

namespace Orders.Tests;

[Parallelizable(ParallelScope.All)]
public class MaterialOptionsTest
{
    [Test]
    public void All_CoversEveryMaterialEnumValueExactlyOnce()
    {
        var expected = Enum.GetValues<Material>().OrderBy(x => x).ToArray();
        var actual = MaterialOptions.All.Select(x => x.Material).OrderBy(x => x).ToArray();

        Assert.That(actual, Is.EqualTo(expected));
        Assert.That(MaterialOptions.All.Select(x => x.Material).Distinct().Count(), Is.EqualTo(expected.Length));
    }
}
