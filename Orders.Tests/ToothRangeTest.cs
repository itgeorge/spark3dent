using Orders;

namespace Orders.Tests;

public class ToothRangeTest
{
    [Test]
    public void Validate_GivenCrownWithSingleTooth_DoesNotThrow()
    {
        var range = new ToothRange(11, 11);

        Assert.DoesNotThrow(() => range.Validate(ConstructionType.Crown));
        Assert.That(range.Teeth, Is.EqualTo(new[] { 11 }));
    }

    [Test]
    public void Validate_GivenCrownWithToothRange_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => new ToothRange(11, 13).Validate(ConstructionType.Crown));
    }

    [Test]
    public void Validate_GivenBridgeWithTwoAdjacentTeeth_DoesNotThrow()
    {
        var range = new ToothRange(11, 21);

        Assert.DoesNotThrow(() => range.Validate(ConstructionType.Bridge));
        Assert.That(range.Teeth, Is.EqualTo(new[] { 11, 21 }));
    }

    [Test]
    public void Validate_GivenBridgeWithThreeTeethInSingleSector_DoesNotThrow()
    {
        var range = new ToothRange(16, 14);

        Assert.DoesNotThrow(() => range.Validate(ConstructionType.Bridge));
        Assert.That(range.Teeth, Is.EqualTo(new[] { 16, 15, 14 }));
    }

    [Test]
    public void Validate_GivenInlayOverlayWithSingleTooth_DoesNotThrow()
    {
        var range = new ToothRange(12, 12);

        Assert.DoesNotThrow(() => range.Validate(ConstructionType.InlayOverlay));
        Assert.That(range.Teeth, Is.EqualTo(new[] { 12 }));
    }

    [TestCase(18, 28, new[] { 18, 17, 16, 15, 14, 13, 12, 11, 21, 22, 23, 24, 25, 26, 27, 28 })]
    [TestCase(48, 38, new[] { 48, 47, 46, 45, 44, 43, 42, 41, 31, 32, 33, 34, 35, 36, 37, 38 })]
    public void Validate_GivenAllTeethInJaw_DoesNotThrow(int start, int end, int[] expectedTeeth)
    {
        var range = new ToothRange(start, end);

        Assert.DoesNotThrow(() => range.Validate(ConstructionType.Bridge));
        Assert.That(range.Teeth, Is.EqualTo(expectedTeeth));
    }

    [Test]
    public void Teeth_GivenNumericallyConfusingRange_UsesFdiJawOrder()
    {
        var range = new ToothRange(12, 22);

        Assert.That(range.Teeth, Is.EqualTo(new[] { 12, 11, 21, 22 }));
    }

    [Test]
    public void Constructor_GivenReversedRange_ReordersToFdiJawOrder()
    {
        var range = new ToothRange(22, 12);

        Assert.Multiple(() =>
        {
            Assert.That(range.Start, Is.EqualTo(12));
            Assert.That(range.End, Is.EqualTo(22));
            Assert.That(range.Teeth, Is.EqualTo(new[] { 12, 11, 21, 22 }));
        });
    }

    [Test]
    public void Validate_GivenRangeAcrossBothJaws_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => new ToothRange(28, 31).Validate(ConstructionType.Bridge));

        Assert.That(ex!.Message, Does.Contain("same jaw"));
    }

    [Test]
    public void Validate_GivenBridgeWithOneTooth_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => new ToothRange(11, 11).Validate(ConstructionType.Bridge));

        Assert.That(ex!.Message, Does.Contain("at least two teeth"));
    }

    [TestCase(10)]
    [TestCase(19)]
    [TestCase(51)]
    public void Validate_GivenInvalidFdiTooth_Throws(int tooth)
    {
        Assert.Throws<InvalidOperationException>(() => new ToothRange(tooth, tooth).Validate(ConstructionType.Crown));
    }
}
