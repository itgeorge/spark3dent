using NUnit.Framework;
using Orders;

namespace Orders.Tests;

[TestFixture]
public class ReservationActiveRulesTest
{
    [Test]
    public void IsActiveForScheduling_ExpiresAtImpressionPlusTwoDaysMidnightLabLocal()
    {
        var reservation = BuildReservation(new DateOnly(2026, 6, 24));
        var beforeExpiry = ToUtc(new DateTime(2026, 6, 25, 23, 59, 0, DateTimeKind.Unspecified));
        var atExpiry = ToUtc(new DateTime(2026, 6, 26, 0, 0, 0, DateTimeKind.Unspecified));

        Assert.That(ReservationActiveRules.IsActiveForScheduling(reservation, beforeExpiry), Is.True);
        Assert.That(ReservationActiveRules.IsActiveForScheduling(reservation, atExpiry), Is.False);
    }

    [Test]
    public void ToAfterCutoffImpressionTimestampUtc_UsesNoonLabLocal()
    {
        var utc = ReservationActiveRules.ToAfterCutoffImpressionTimestampUtc(new DateOnly(2026, 6, 24));
        var local = LabTimeZone.ToLabLocal(utc);

        Assert.That(TimeOnly.FromDateTime(local.DateTime), Is.EqualTo(new TimeOnly(12, 0)));
    }

    private static DateTimeOffset ToUtc(DateTime labLocal)
    {
        var utc = TimeZoneInfo.ConvertTimeToUtc(labLocal, LabTimeZone.BulgariaSofia);
        return new DateTimeOffset(utc, TimeSpan.Zero);
    }

    private static ReservationRecord BuildReservation(DateOnly impressionDate) => new(
        1,
        "DEMO",
        "Demo Clinic",
        "member",
        "Member",
        "fingerprint",
        "Reservation",
        impressionDate,
        ProductCategory.Permanent,
        Material.Pmma,
        [new OrderWorkItem(ConstructionType.Crown, new ToothRange(11, 11))],
        impressionDate.AddDays(5),
        ReservationStatus.Active,
        Shade.Unspecified,
        null,
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow,
        "127.0.0.1",
        "test");
}
