using Orders;

namespace Orders.Tests;

public class ToothRangeTest
{
    [Test]
    public void Validate_GivenCrownWithSingleTooth_DoesNotThrow()
    {
        Assert.DoesNotThrow(() => new ToothRange(11, 11).Validate(ConstructionType.Crown));
    }

    [Test]
    public void Validate_GivenCrownWithToothRange_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => new ToothRange(11, 13).Validate(ConstructionType.Crown));
    }

    [Test]
    public void DefaultAbutments_GivenBridge_ReturnsRangeEdges()
    {
        var range = new ToothRange(14, 16);
        range.Validate(ConstructionType.Bridge);

        var abutments = range.DefaultAbutments(ConstructionType.Bridge);

        Assert.That(abutments, Is.EqualTo(new[] { 14, 16 }));
    }
}
