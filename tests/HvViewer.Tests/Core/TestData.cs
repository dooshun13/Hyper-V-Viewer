using System.Globalization;
using HvViewer.Core.Model;

namespace HvViewer.Tests.Core;

/// <summary>Shared helpers for query tests.</summary>
public static class TestData
{
    public static readonly TimeZoneInfo Mauritius =
        TimeZoneInfo.CreateCustomTimeZone("Mauritius", TimeSpan.FromHours(4), "Mauritius", "Mauritius");

    public static readonly TimeZoneInfo Paris = FindZone("Europe/Paris", "Romance Standard Time");

    public static DateTime Utc(string iso) =>
        DateTime.SpecifyKind(DateTime.Parse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal), DateTimeKind.Utc);

    /// <summary>"yyyy-MM-dd HH:mm" taken as a UTC instant.</summary>
    public static DateTime T(string s) => Utc(s.Replace(' ', 'T') + (s.Length == 16 ? ":00Z" : "Z"));

    public static TimeRange R(string fromIso, string toIso) => new(Utc(fromIso), Utc(toIso));

    public static EventQuery Q(
        string? text = null, bool regex = false, TimeRange? range = null,
        IReadOnlyList<string>? logs = null, IReadOnlyList<int>? include = null, IReadOnlyList<int>? exclude = null,
        IReadOnlyList<EventLevel>? levels = null, IReadOnlyList<string>? sources = null, IReadOnlyList<TaskFilter>? tasks = null) =>
        new()
        {
            Text = text,
            TextOptions = new TextOptions(Regex: regex),
            Range = range ?? R("2026-11-10T00:00:00Z", "2026-11-11T00:00:00Z"),
            Logs = logs ?? ["System"],
            IncludeIds = include ?? [],
            ExcludeIds = exclude ?? [],
            Levels = levels ?? [],
            Sources = sources ?? [],
            Tasks = tasks ?? [],
        };

    private static TimeZoneInfo FindZone(string iana, string windows)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(iana); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById(windows); }
    }
}
