namespace HvViewer.Core.Model;

/// <summary>A UTC time range. Every query carries one.</summary>
public readonly record struct TimeRange(DateTime FromUtc, DateTime ToUtc)
{
    public TimeSpan Length => ToUtc - FromUtc;

    public static TimeRange Last(TimeSpan span, DateTime nowUtc) => new(nowUtc - span, nowUtc);

    public static TimeRange Today(TimeZoneInfo zone, DateTime nowUtc) =>
        new(MidnightUtc(zone, nowUtc), nowUtc);

    public static TimeRange Yesterday(TimeZoneInfo zone, DateTime nowUtc)
    {
        var todayLocal = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, zone).Date;
        return new(ZoneTime.LocalToUtc(todayLocal.AddDays(-1), zone), ZoneTime.LocalToUtc(todayLocal, zone));
    }

    public static TimeRange SinceBoot(DateTime bootUtc, DateTime nowUtc) => new(bootUtc, nowUtc);

    private static DateTime MidnightUtc(TimeZoneInfo zone, DateTime nowUtc) =>
        ZoneTime.LocalToUtc(TimeZoneInfo.ConvertTimeFromUtc(nowUtc, zone).Date, zone);
}
