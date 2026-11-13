using HvViewer.Core.Model;
using HvViewer.Core.Search;
using HvViewer.Rules;
using HvViewer.Tests.Fakes;
using Xunit;
using static HvViewer.Core.Model.EventLevel;
using static HvViewer.Tests.Core.TestData;

namespace HvViewer.Tests.Rules;

public class DashboardAnalyzerTests
{
    private static readonly TimeRange Day = R("2026-11-10T00:00:00Z", "2026-11-11T00:00:00Z");
    private const string DenyReason = "Access denied: run as administrator to include";

    private static RuleEvent Rule(string provider, int id, string log, RuleSeverity severity) =>
        new(provider, id, log, severity, "t", "m", "c", "n", "https://learn.microsoft.com/x");

    internal static RuleSet Rules() => new(
    [
        new Troubleshooter(1, "disk-storage", "Disk & storage", "s", [Rule("disk", 153, "System", RuleSeverity.Warning)]),
        new Troubleshooter(1, "logons-accounts", "Logons & accounts", "s", [Rule("Microsoft-Windows-Security-Auditing", 4625, "Security", RuleSeverity.Warning)]),
        new Troubleshooter(1, "crashes-hangs", "Crashes & hangs", "s", [Rule("Application Error", 1000, "Application", RuleSeverity.Error)]),
        new Troubleshooter(1, "boot-shutdown", "Boot & shutdown", "s",
        [
            Rule("Microsoft-Windows-Kernel-Power", 41, "System", RuleSeverity.Critical),
            Rule("EventLog", 6008, "System", RuleSeverity.Error),
            Rule("EventLog", 6005, "System", RuleSeverity.Info),
        ]),
    ], []);

    private static EventRecordLite E(string time, int id = 1, string provider = "Foo", EventLevel level = Warning, string log = "System", string day = "2026-11-10") =>
        Ev.Make(log, 1, $"{day} {time}", id, provider, level);

    private static DashboardResult Analyze(IReadOnlyList<EventRecordLite> current, IReadOnlyList<EventRecordLite>? previous = null,
        IReadOnlyList<SkippedLog>? skipped = null, TimeRange? range = null) =>
        new DashboardAnalyzer(Rules()).Analyze(current, previous ?? [], range ?? Day, skipped ?? []);

    [Fact] public void Counts_Level0IsInformation()
    {
        var result = Analyze([E("01:00:00", level: LogAlways), E("02:00:00", level: Information), E("03:00:00", level: Verbose),
                              E("04:00:00", level: Critical), E("05:00:00", level: Error), E("06:00:00", level: Warning)]);
        Assert.Equal(new SeverityCounts(1, 1, 1, 2), result.Current);
    }

    [Fact] public void Previous_UsesSameLengthWindow()
    {
        var previous = new[]
        {
            E("23:00:00", level: Error, day: "2026-11-09"),                  // inside [From-24h, From)
            E("20:00:00", level: Warning, day: "2026-11-08"),                // older than the window
            E("22:00:00", level: Information, day: "2026-11-09"),            // information is not counted
        };
        Assert.Equal(new SeverityCounts(0, 1, 0, 0), Analyze([], previous).Previous);
    }

    [Fact] public void Buckets_24h_Has24HourlyIncludingEmpty()
    {
        var result = Analyze([E("10:15:00", level: Error), E("10:45:00", level: Critical), E("10:50:00", level: Warning)]);

        Assert.Equal(24, result.Buckets.Count);
        Assert.Equal(Day.FromUtc, result.Buckets[0].StartUtc);
        Assert.Equal(new Bucket(Day.FromUtc.AddHours(10), 2, 1), result.Buckets[10]);
        Assert.Equal(0, result.Buckets[11].Errors);
    }

    [Fact] public void Buckets_7d_Has7Daily()
    {
        var week = R("2026-11-04T00:00:00Z", "2026-11-11T00:00:00Z");
        var result = Analyze([E("12:00:00", level: Error, day: "2026-11-06")], range: week);

        Assert.Equal(7, result.Buckets.Count);
        Assert.Equal(TimeSpan.FromDays(1), result.Buckets[1].StartUtc - result.Buckets[0].StartUtc);
        Assert.Equal(1, result.Buckets[2].Errors);
    }

    [Fact] public void Tile_CountsOnlyWarningOrAbove()
    {
        var result = Analyze([E("01:00:00", 153, "disk"), E("02:00:00", 153, "disk"), E("03:00:00", 6005, "EventLog", Information)]);

        var disk = result.Tiles.Single(t => t.TroubleshooterId == "disk-storage");
        var boot = result.Tiles.Single(t => t.TroubleshooterId == "boot-shutdown");
        Assert.Equal(2, disk.Count);
        Assert.Equal(RuleSeverity.Warning, disk.Highest);
        Assert.Equal(0, boot.Count);
    }

    [Fact] public void Tiles_AreInRuleOrder() => Assert.Equal(
        ["disk-storage", "logons-accounts", "crashes-hangs", "boot-shutdown"], Analyze([]).Tiles.Select(t => t.TroubleshooterId));

    [Fact] public void Tile_Highest_IsTheWorstCountedSeverity()
    {
        var result = Analyze([E("01:00:00", 41, "Microsoft-Windows-Kernel-Power", Critical), E("02:00:00", 6008, "EventLog", Error)]);
        var boot = result.Tiles.Single(t => t.TroubleshooterId == "boot-shutdown");
        Assert.Equal((2, RuleSeverity.Critical), (boot.Count, boot.Highest));
    }

    [Fact] public void Tile_AllLogsSkipped_IsUnavailableNotZero()
    {
        var result = Analyze([], skipped: [new SkippedLog("Security", DenyReason)]);

        var logons = result.Tiles.Single(t => t.TroubleshooterId == "logons-accounts");
        Assert.Equal(DenyReason, logons.UnavailableReason);
        Assert.All(result.Tiles.Where(t => t != logons), t => Assert.Null(t.UnavailableReason));
    }

    [Fact] public void Tile_SomeLogsSkipped_StaysAvailable()
    {
        var rules = new RuleSet([new Troubleshooter(1, "mixed", "Mixed", "s",
            [Rule("a", 1, "System", RuleSeverity.Error), Rule("b", 2, "Security", RuleSeverity.Error)])], []);
        var result = new DashboardAnalyzer(rules).Analyze([], [], Day, [new SkippedLog("Security", DenyReason)]);
        Assert.Null(result.Tiles[0].UnavailableReason);
    }

    [Fact] public void NewAndUnusual_ExcludesSeenInPrevious7d()
    {
        var current = new[]
        {
            E("01:00:00", 153, "disk"),            // seen before
            E("02:00:00", 99, "Foo", Error),       // new
            E("03:00:00", 99, "Foo", Error),       // same item again
            E("04:00:00", 5, "Bar", Information),  // not warning or worse
        };
        var previous = new[] { E("12:00:00", 153, "disk", day: "2026-11-06") };

        var item = Assert.Single(Analyze(current, previous).NewAndUnusual);
        Assert.Equal(99, item.Latest.EventId);
        Assert.Equal(2, item.Count);
        Assert.Equal(DateTime.SpecifyKind(DateTime.Parse("2026-11-10 03:00:00"), DateTimeKind.Utc), item.Latest.TimeCreatedUtc);
    }

    [Fact] public void NewAndUnusual_NewestFirst_AtMost10()
    {
        var current = Enumerable.Range(1, 15).Select(i => E($"{i:00}:00:00", 100 + i, "P")).ToList();
        var items = Analyze(current).NewAndUnusual;

        Assert.Equal(10, items.Count);
        Assert.Equal(115, items[0].Latest.EventId);
    }

    [Fact] public void NewAndUnusual_IncludesRuleWhenOneMatches()
    {
        var item = Assert.Single(Analyze([E("01:00:00", 1000, "Application Error", Error, "Application")]).NewAndUnusual);
        Assert.Equal(1000, item.Rule?.Id);
    }

    [Fact] public void Restart_AnchorIsLastEventBeforeBoot()
    {
        var current = new[]
        {
            E("23:41:00", 153, "disk", Warning),
            E("23:44:00", 2004, "Microsoft-Windows-Resource-Exhaustion-Detector", Error),
            E("23:46:30", 1000, "Application Error", Error, "Application"),
            E("23:52:00", 6005, "EventLog", Information),
            E("23:52:18", 41, "Microsoft-Windows-Kernel-Power", Critical),
            E("23:52:19", 6008, "EventLog", Error),
        };

        var story = Analyze(current).Restart;

        Assert.NotNull(story);
        Assert.Equal(DateTime.SpecifyKind(DateTime.Parse("2026-11-10 23:46:30"), DateTimeKind.Utc), story.AnchorUtc);
        Assert.Equal(41, story.RestartEvent.EventId);
        Assert.Equal([1000, 2004, 153], story.Before.Select(e => e.EventId));
    }

    [Fact] public void Restart_Only6008_IsStillFound()
    {
        var story = Analyze([E("23:40:00", 153, "disk"), E("23:50:00", 6005, "EventLog", Information), E("23:50:10", 6008, "EventLog", Error)]).Restart;
        Assert.Equal(6008, story!.RestartEvent.EventId);
        Assert.Equal([153], story.Before.Select(e => e.EventId));
    }

    [Fact] public void Restart_EventsOlderThan15MinutesBeforeAnchor_AreLeftOut()
    {
        var story = Analyze([E("22:00:00", 153, "disk"), E("23:46:30", 1000, "Application Error", Error, "Application"),
                             E("23:52:00", 6005, "EventLog", Information), E("23:52:18", 41, "Microsoft-Windows-Kernel-Power", Critical)]).Restart;
        Assert.Equal([1000], story!.Before.Select(e => e.EventId));
    }

    [Fact] public void Restart_InformationEventsAreNotListed()
    {
        var story = Analyze([E("23:46:00", 7, "Foo", Information), E("23:46:30", 1000, "Application Error", Error, "Application"),
                             E("23:52:00", 6005, "EventLog", Information), E("23:52:18", 41, "Microsoft-Windows-Kernel-Power", Critical)]).Restart;
        Assert.Equal([1000], story!.Before.Select(e => e.EventId));
    }

    [Fact] public void Restart_NoneInRange_IsNull() =>
        Assert.Null(Analyze([E("10:00:00", 153, "disk")]).Restart);
}
