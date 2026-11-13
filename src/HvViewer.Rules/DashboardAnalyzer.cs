using HvViewer.Core.Model;
using HvViewer.Core.Search;

namespace HvViewer.Rules;

/// <summary>Turns scanned events into the numbers, tiles, chart and restart story of the Home dashboard. Pure: no I/O.</summary>
public sealed class DashboardAnalyzer(RuleSet rules)
{
    private const int MaxUnusual = 10;
    private static readonly TimeSpan HourlyUpTo = TimeSpan.FromHours(48);
    private static readonly TimeSpan BeforeRestartWindow = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan SameIncident = TimeSpan.FromMinutes(2);

    private const string SystemLog = "System";

    public DashboardResult Analyze(
        IReadOnlyList<EventRecordLite> current,
        IReadOnlyList<EventRecordLite> previous7d,
        TimeRange range,
        IReadOnlyList<SkippedLog> skipped)
    {
        var inRange = current.Where(e => e.TimeCreatedUtc >= range.FromUtc && e.TimeCreatedUtc <= range.ToUtc).ToList();
        var matched = inRange.Select(e => (Event: e, Match: rules.Match(e))).ToList();

        return new DashboardResult(
            Current: Count(inRange, includeInformation: true),
            Previous: Count(PreviousWindow(previous7d, range), includeInformation: false),
            Buckets: BuildBuckets(inRange, range),
            Tiles: BuildTiles(matched, skipped),
            NewAndUnusual: BuildUnusual(inRange, previous7d),
            Restart: FindRestart(inRange),
            Skipped: skipped);
    }

    // ---- counts ----

    private static List<EventRecordLite> PreviousWindow(IReadOnlyList<EventRecordLite> previous7d, TimeRange range)
    {
        var from = range.FromUtc - range.Length;
        return previous7d.Where(e => e.TimeCreatedUtc >= from && e.TimeCreatedUtc < range.FromUtc).ToList();
    }

    private static SeverityCounts Count(IEnumerable<EventRecordLite> events, bool includeInformation)
    {
        int critical = 0, error = 0, warning = 0, information = 0;
        foreach (var e in events)
        {
            switch (e.Level)
            {
                case EventLevel.Critical: critical++; break;
                case EventLevel.Error: error++; break;
                case EventLevel.Warning: warning++; break;
                case EventLevel.LogAlways or EventLevel.Information: if (includeInformation) information++; break;
            }
        }
        return new SeverityCounts(critical, error, warning, information);
    }

    // ---- chart ----

    private static List<Bucket> BuildBuckets(List<EventRecordLite> events, TimeRange range)
    {
        var step = range.Length <= HourlyUpTo ? TimeSpan.FromHours(1) : TimeSpan.FromDays(1);
        var counts = new Dictionary<long, (int Errors, int Warnings)>();
        foreach (var e in events)
        {
            var key = Floor(e.TimeCreatedUtc, step).Ticks;
            counts.TryGetValue(key, out var c);
            counts[key] = e.Level switch
            {
                EventLevel.Critical or EventLevel.Error => (c.Errors + 1, c.Warnings),
                EventLevel.Warning => (c.Errors, c.Warnings + 1),
                _ => c,
            };
        }

        var buckets = new List<Bucket>();
        for (var start = Floor(range.FromUtc, step); start < range.ToUtc; start += step)
        {
            counts.TryGetValue(start.Ticks, out var c);
            buckets.Add(new Bucket(start, c.Errors, c.Warnings));
        }
        return buckets;
    }

    private static DateTime Floor(DateTime utc, TimeSpan step) =>
        new(utc.Ticks - utc.Ticks % step.Ticks, DateTimeKind.Utc);

    // ---- tiles ----

    private List<Tile> BuildTiles(List<(EventRecordLite Event, (Troubleshooter Troubleshooter, RuleEvent Rule)? Match)> matched, IReadOnlyList<SkippedLog> skipped)
    {
        var skippedByLog = skipped.GroupBy(s => s.Log, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Reason, StringComparer.OrdinalIgnoreCase);
        var tiles = new List<Tile>();

        foreach (var t in rules.Troubleshooters)
        {
            var counted = matched
                .Where(m => m.Match is { } hit && hit.Troubleshooter.Id == t.Id && hit.Rule.Severity >= RuleSeverity.Warning)
                .Select(m => m.Match!.Value.Rule.Severity)
                .ToList();

            var unavailable = t.Logs.Count > 0 && t.Logs.All(skippedByLog.ContainsKey)
                ? skippedByLog[t.Logs[0]]
                : null;

            tiles.Add(new Tile(t.Id, t.Title, unavailable is null ? counted.Count : 0,
                counted.Count == 0 ? RuleSeverity.Info : counted.Max(), unavailable));
        }
        return tiles;
    }

    // ---- new & unusual ----

    private List<UnusualItem> BuildUnusual(List<EventRecordLite> current, IReadOnlyList<EventRecordLite> previous7d)
    {
        var seen = previous7d.Select(Key).ToHashSet();
        return current
            .Where(e => e.Level is >= EventLevel.Critical and <= EventLevel.Warning)
            .GroupBy(Key)
            .Where(g => !seen.Contains(g.Key))
            .Select(g => (Latest: g.OrderByDescending(e => e.TimeCreatedUtc).First(), Count: g.Count()))
            .OrderByDescending(x => x.Latest.TimeCreatedUtc)
            .Take(MaxUnusual)
            .Select(x => new UnusualItem(x.Latest, rules.Match(x.Latest)?.Rule, x.Count))
            .ToList();
    }

    private static (string Provider, int Id) Key(EventRecordLite e) => (e.ProviderName.ToLowerInvariant(), e.EventId);

    // ---- what happened before the restart ----

    private static bool Is(EventRecordLite e, string provider, int id) =>
        e.EventId == id
        && string.Equals(e.LogName, SystemLog, StringComparison.OrdinalIgnoreCase)
        && (string.Equals(e.ProviderName, provider, StringComparison.OrdinalIgnoreCase)
            || string.Equals(e.EventSourceName, provider, StringComparison.OrdinalIgnoreCase));

    private static RestartStory? FindRestart(List<EventRecordLite> events)
    {
        var restarts = events.Where(e => Is(e, "Microsoft-Windows-Kernel-Power", 41) || Is(e, "EventLog", 6008)).ToList();
        if (restarts.Count == 0) return null;

        // The newest incident; within it prefer Kernel-Power 41 over 6008 because it is the more specific record.
        var newest = restarts.Max(e => e.TimeCreatedUtc);
        var incident = restarts.Where(e => newest - e.TimeCreatedUtc <= SameIncident).ToList();
        var restart = incident.FirstOrDefault(e => e.EventId == 41) ?? incident.OrderByDescending(e => e.TimeCreatedUtc).First();

        // The boot record (EventLog 6005) at or before the restart event.
        var boot = events.Where(e => Is(e, "EventLog", 6005) && e.TimeCreatedUtc <= restart.TimeCreatedUtc)
            .OrderByDescending(e => e.TimeCreatedUtc).FirstOrDefault();
        var bootTime = boot?.TimeCreatedUtc ?? restart.TimeCreatedUtc;

        // The anchor is the last event logged before the boot: that is what Windows was doing just before it went down.
        var beforeBoot = events.Where(e => e.TimeCreatedUtc < bootTime).ToList();
        var anchor = beforeBoot.Count == 0 ? bootTime : beforeBoot.Max(e => e.TimeCreatedUtc);

        var before = events
            .Where(e => e.TimeCreatedUtc <= anchor && e.TimeCreatedUtc >= anchor - BeforeRestartWindow)
            .Where(e => e.Level is >= EventLevel.Critical and <= EventLevel.Warning)
            .OrderByDescending(e => e.TimeCreatedUtc)
            .ToList();

        return new RestartStory(anchor, restart, before);
    }
}
