namespace Orders;

public enum ReservationStatus
{
    Active,
    Cancelled,
    Promoted
}

public sealed record ReservationRecord(
    long Id,
    string ClinicCode,
    string ClinicDisplayName,
    string MemberId,
    string MemberLabel,
    string MemberPinHashFingerprint,
    string CaseName,
    DateOnly ImpressionDate,
    ProductCategory ProductCategory,
    Material Material,
    IReadOnlyList<OrderWorkItem> WorkItems,
    DateOnly RequestedDeliveryDate,
    ReservationStatus Status,
    Shade Shade,
    string? Notes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string CreatedIp,
    string CreatedUserAgent,
    string? ColorNote = null,
    decimal? CalculatedCapacityUnits = null,
    long? PromotedOrderId = null,
    string? PromotedOrderCode = null,
    DateTimeOffset? PromotedAt = null);

public sealed record ReservationDraft(
    string CaseName,
    DateOnly ImpressionDate,
    ProductCategory ProductCategory,
    Material Material,
    IReadOnlyList<OrderWorkItem> WorkItems,
    DateOnly RequestedDeliveryDate,
    Shade Shade,
    string? Notes,
    string? ColorNote);

public static class ReservationActiveRules
{
    public static bool IsActiveForScheduling(ReservationRecord reservation, DateTimeOffset nowUtc) =>
        reservation.Status == ReservationStatus.Active && IsBeforeIgnoreAt(reservation.ImpressionDate, nowUtc);

    public static DateTimeOffset IgnoreAtUtc(DateOnly impressionDate)
    {
        var localIgnore = impressionDate.AddDays(2).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var utc = TimeZoneInfo.ConvertTimeToUtc(localIgnore, LabTimeZone.BulgariaSofia);
        return new DateTimeOffset(utc, TimeSpan.Zero);
    }

    public static DateTimeOffset ToAfterCutoffImpressionTimestampUtc(DateOnly impressionDate)
    {
        var local = impressionDate.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Unspecified);
        var utc = TimeZoneInfo.ConvertTimeToUtc(local, LabTimeZone.BulgariaSofia);
        return new DateTimeOffset(utc, TimeSpan.Zero);
    }

    private static bool IsBeforeIgnoreAt(DateOnly impressionDate, DateTimeOffset nowUtc) =>
        nowUtc.ToUniversalTime() < IgnoreAtUtc(impressionDate);
}
