using Orders;

namespace Orders.Tests;

public class OrdersDomainTests
{
    [Test]
    public void Crown_must_have_single_tooth()
    {
        Assert.DoesNotThrow(() => new ToothRange(11, 11).Validate(ConstructionType.Crown));
        Assert.Throws<InvalidOperationException>(() => new ToothRange(11, 13).Validate(ConstructionType.Crown));
    }

    [Test]
    public void Bridge_uses_range_edges_as_default_abutments()
    {
        var range = new ToothRange(14, 16);
        range.Validate(ConstructionType.Bridge);
        Assert.That(range.DefaultAbutments(ConstructionType.Bridge), Is.EqualTo(new[] { 14, 16 }));
    }

    [Test]
    public async Task Weekend_provider_makes_monday_first_after_closure_and_tuesday_selectable()
    {
        var availability = new DateAvailabilityService(new WeekendOnlyNonWorkingDayProvider());
        var monday = new DateOnly(2026, 6, 1);
        var tuesday = new DateOnly(2026, 6, 2);

        var monStatus = await availability.GetStatusAsync(monday, monday);
        var tueStatus = await availability.GetStatusAsync(tuesday, monday);

        Assert.That(monStatus.IsSelectable, Is.False);
        Assert.That(monStatus.IsFirstBusinessDayAfterClosure, Is.True);
        Assert.That(tueStatus.IsSelectable, Is.True);
    }

    [Test]
    public void Pin_hash_verifies_and_rejects_wrong_pin()
    {
        var hasher = new PinHasher("test-pepper");
        var hash = hasher.Hash("123456", iterations: 10_000);
        Assert.That(hasher.Verify("123456", hash), Is.True);
        Assert.That(hasher.Verify("654321", hash), Is.False);
    }
}
