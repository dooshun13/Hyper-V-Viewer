namespace HvViewer.Core.Model;

/// <summary>Converts local wall-clock times to UTC using the zone's rules, including daylight-saving edges.</summary>
public static class ZoneTime
{
    /// <summary>
    /// A time that does not exist (spring-forward gap) moves forward to the first valid instant.
    /// An ambiguous time (fall-back overlap) uses the earlier (daylight) offset.
    /// </summary>
    public static DateTime LocalToUtc(DateTime local, TimeZoneInfo zone)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(local)) local = local.AddMinutes(1);
        var offset = zone.IsAmbiguousTime(local) ? zone.GetAmbiguousTimeOffsets(local).Max() : zone.GetUtcOffset(local);
        return DateTime.SpecifyKind(local - offset, DateTimeKind.Utc);
    }
}
