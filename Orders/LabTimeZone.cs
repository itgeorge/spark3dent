namespace Orders;

public static class LabTimeZone
{
    private static readonly Lazy<TimeZoneInfo> Sofia = new(ResolveSofiaTimeZone);

    public static TimeZoneInfo BulgariaSofia => Sofia.Value;

    public static DateTimeOffset ToLabLocal(DateTimeOffset utcTimestamp) =>
        TimeZoneInfo.ConvertTime(utcTimestamp.ToUniversalTime(), BulgariaSofia);

    private static TimeZoneInfo ResolveSofiaTimeZone()
    {
        foreach (var id in new[] { "Europe/Sofia", "FLE Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }

        throw new InvalidOperationException("Could not resolve Bulgaria/Sofia lab time zone.");
    }
}
