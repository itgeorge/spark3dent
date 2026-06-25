using Orders;

namespace Orders.Tests;

public class SchedulingConfigValidationTest
{
    [Test]
    public void Validate_GivenNonPfmMaterialWithPositiveTeethPerExtraLeadDay_AllowsValue()
    {
        Assert.DoesNotThrow(() => SchedulingConfigValidation.Validate(
            Material.Pmma,
            new MaterialSchedulingConfigUpdate(2, 1.25m, 7)));
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void Validate_GivenNonPfmMaterialWithNonPositiveTeethPerExtraLeadDay_RejectsValue(int value)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => SchedulingConfigValidation.Validate(
            Material.Pmma,
            new MaterialSchedulingConfigUpdate(2, 1.25m, value)));

        Assert.That(ex!.Message, Does.Contain("positive"));
    }

    [Test]
    public void Validate_GivenPfmMaterialWithNullTeethPerExtraLeadDay_StillRejectsValue()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => SchedulingConfigValidation.Validate(
            Material.Pfm,
            new MaterialSchedulingConfigUpdate(4, 1.0m, null)));

        Assert.That(ex!.Message, Does.Contain("requires positive teeth per extra lead day"));
    }
}
