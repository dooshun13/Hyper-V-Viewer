using System.Runtime.Versioning;
using HvViewer.Core.Model;
using HvViewer.Core.Reading;
using HvViewer.Core.Search;
using HvViewer.Rules;
using Xunit;

namespace HvViewer.Tests.Samples;

/// <summary>
/// Checks the dashboard logic against real .evtx files exported from lab machines (see tests/samples/README.md).
/// A sample that has not been provided is skipped: the test does nothing for that file.
/// </summary>
[SupportedOSPlatform("windows")]
public class SampleLogTests
{
    private static readonly RuleSet Rules = RuleLoader.LoadBuiltIn();

    private static string? Find(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "samples", file);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    private static (List<EventRecordLite> Events, DashboardResult Result) Analyze(string path)
    {
        var events = new NativeEventSource().Query(LogTarget.File(path), "*", CancellationToken.None).ToList();
        Assert.NotEmpty(events);
        var range = new TimeRange(events.Min(e => e.TimeCreatedUtc).AddSeconds(-1), events.Max(e => e.TimeCreatedUtc).AddSeconds(1));
        return (events, new DashboardAnalyzer(Rules).Analyze(events, [], range, []));
    }

    [WindowsTheory]
    [InlineData("disk-retries.evtx", "disk-storage")]
    [InlineData("logons.evtx", "logons-accounts")]
    [InlineData("crashes.evtx", "crashes-hangs")]
    [InlineData("services.evtx", "services")]
    [InlineData("low-memory.evtx", "memory-performance")]
    [InlineData("dns.evtx", "network-dns")]
    [InlineData("group-policy.evtx", "group-policy")]
    [InlineData("windows-update.evtx", "windows-update")]
    [InlineData("french-system.evtx", "boot-shutdown")]
    public void Sample_ShowsUpInItsTroubleshooterTile(string file, string troubleshooter)
    {
        if (Find(file) is not { } path) return; // Sample not provided: <file>

        var (_, result) = Analyze(path);

        var tile = result.Tiles.Single(t => t.TroubleshooterId == troubleshooter);
        Assert.True(tile.Count >= 1, $"{file}: expected the '{troubleshooter}' tile to count at least one event");
    }

    [WindowsFact]
    public void Restart_SampleHasARestartStoryBeforeTheBootRecord()
    {
        if (Find("restart.evtx") is not { } path) return;

        var (events, result) = Analyze(path);

        var story = Assert.IsType<RestartStory>(result.Restart);
        Assert.Contains(events, e => e.EventId == 41);
        var boot = events.Where(e => e.EventId == 6005 && e.TimeCreatedUtc <= story.RestartEvent.TimeCreatedUtc).Max(e => e.TimeCreatedUtc);
        Assert.True(story.AnchorUtc < boot);
    }

    [WindowsFact]
    public void DiskRetries_SampleGroupsIdenticalEvents()
    {
        if (Find("disk-retries.evtx") is not { } path) return;

        var (events, _) = Analyze(path);

        var groups = Grouping.Group(events.Where(e => e.EventId == 153).OrderByDescending(e => e.TimeCreatedUtc).ToList());
        Assert.Contains(groups, g => g.Count > 1);
    }

    [WindowsFact]
    public void Logons_SampleCountsFailedLogonsAndLockouts()
    {
        if (Find("logons.evtx") is not { } path) return;
        var (events, _) = Analyze(path);
        Assert.Contains(events, e => e.EventId == 4625);
        Assert.Contains(events, e => e.EventId == 4740);
    }

    [WindowsFact]
    public void FrenchSystemLog_ExplanationsStayInEnglish()
    {
        if (Find("french-system.evtx") is not { } path) return;

        var (events, _) = Analyze(path);

        // Rules match on provider + ID + log name, so a French log matches the same rules; the explanation text is ours.
        var matched = events.Select(e => Rules.Match(e)).Where(m => m is not null).Select(m => m!.Value.Rule).ToList();
        Assert.NotEmpty(matched);
        Assert.All(matched, r => Assert.DoesNotMatch("[àâçéèêëîïôùûüœ]", r.Title + r.Meaning + r.Causes + r.Next));
    }

    [WindowsFact]
    public void NoPublisher_SampleHasNoMessageButStillHasRawData()
    {
        if (Find("no-publisher.evtx") is not { } path) return;

        var source = new NativeEventSource();
        var log = LogTarget.File(path);
        var first = source.Query(log, "*", CancellationToken.None).First();

        Assert.Null(source.FormatMessage(log, first.RecordId));
        Assert.NotNull(source.GetXml(log, first.RecordId));
    }

    // Harness check that runs on any PC that has the perf log (a copy of this PC's System log).
    [WindowsFact]
    public void AnalyzingAWholeRealLog_Works()
    {
        if (Find("perf.evtx") is not { } path) return;

        var (events, result) = Analyze(path);

        Assert.NotEmpty(result.Buckets);
        Assert.Equal(events.Count(e => e.Level == EventLevel.Error), result.Current.Error);
        Assert.Equal(events.Count(e => e.Level is EventLevel.LogAlways or EventLevel.Information), result.Current.Information);
    }
}
