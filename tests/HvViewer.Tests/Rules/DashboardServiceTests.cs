using HvViewer.Core.Model;
using HvViewer.Core.Search;
using HvViewer.Rules;
using HvViewer.Tests.Fakes;
using Xunit;
using static HvViewer.Tests.Core.TestData;

namespace HvViewer.Tests.Rules;

public class DashboardServiceTests
{
    private static readonly TimeRange Day = R("2026-11-10T00:00:00Z", "2026-11-11T00:00:00Z");

    private sealed class SyncProgress<T>(List<T> sink) : IProgress<T>
    {
        public void Report(T value) => sink.Add(value);
    }

    private static FakeEventSource Source() => new FakeEventSource()
        .AddLog("System", Ev.Make("System", 1, "2026-11-10 10:00:00", 153, "disk"))
        .AddLog("Application", Ev.Make("Application", 1, "2026-11-10 11:00:00", 1000, "Application Error", EventLevel.Error))
        .AddLog("Security", Ev.Make("Security", 1, "2026-11-10 12:00:00", 4625, "Microsoft-Windows-Security-Auditing"));

    private static DashboardService Service(FakeEventSource source) => new(source, DashboardAnalyzerTests.Rules());

    [Fact] public async Task Service_ReturnsAnalysedResult()
    {
        var result = await Service(Source()).RunAsync(Day, null, CancellationToken.None);

        Assert.Equal(1, result.Tiles.Single(t => t.TroubleshooterId == "disk-storage").Count);
        Assert.Equal(1, result.Tiles.Single(t => t.TroubleshooterId == "crashes-hangs").Count);
        Assert.Equal(1, result.Tiles.Single(t => t.TroubleshooterId == "logons-accounts").Count);
        Assert.Empty(result.Skipped);
    }

    [Fact] public async Task Service_SecurityQueriedOnlyForRuleIds()
    {
        var source = Source();
        await Service(source).RunAsync(Day, null, CancellationToken.None);

        var current = source.XPathHistory["Security"].Single(x => x.Contains("@SystemTime>='2026-11-10T00:00:00.000Z'"));
        Assert.Contains("EventID=4625", current);
        Assert.DoesNotContain("Level=", current);

        var currentSystem = source.XPathHistory["System"].Single(x => x.Contains("@SystemTime>='2026-11-10T00:00:00.000Z'"));
        Assert.DoesNotContain("EventID", currentSystem);
    }

    [Fact] public async Task Service_PreviousPeriod_IsSevenDaysBeforeAndOnlyWarningsAndWorse()
    {
        var source = Source();
        await Service(source).RunAsync(Day, null, CancellationToken.None);

        var previous = source.XPathHistory["System"].Single(x => x.Contains("2026-11-03T00:00:00.000Z"));
        Assert.Contains("Level=1 or Level=2 or Level=3", previous);
        Assert.Contains("@SystemTime<='2026-11-10T00:00:00.000Z'", previous);
    }

    [Fact] public async Task Service_ReportsPartialAfterEachLog()
    {
        var partials = new List<DashboardResult>();
        var final = await Service(Source()).RunAsync(Day, new SyncProgress<DashboardResult>(partials), CancellationToken.None);

        Assert.Equal(3, partials.Count); // System, Security, Application (the order the rules list their logs)
        Assert.Equal(1, partials[0].Tiles.Single(t => t.TroubleshooterId == "disk-storage").Count);
        Assert.Equal(0, partials[0].Tiles.Single(t => t.TroubleshooterId == "crashes-hangs").Count);
        Assert.Equal(1, partials[1].Tiles.Single(t => t.TroubleshooterId == "logons-accounts").Count);
        Assert.Equal(0, partials[1].Tiles.Single(t => t.TroubleshooterId == "crashes-hangs").Count);
        Assert.Equal(1, partials[2].Tiles.Single(t => t.TroubleshooterId == "crashes-hangs").Count);
        Assert.Equal(final.Tiles.Select(t => t.Count), partials[^1].Tiles.Select(t => t.Count));
    }

    [Fact] public async Task Service_AccessDeniedLog_IsSkippedAndScanContinues()
    {
        var source = new FakeEventSource()
            .AddLog("System", Ev.Make("System", 1, "2026-11-10 10:00:00", 153, "disk"))
            .AddLog("Application")
            .Deny("Security");

        var result = await Service(source).RunAsync(Day, null, CancellationToken.None);

        Assert.Equal([new SkippedLog("Security", "Access denied: run as administrator to include")], result.Skipped);
        var logons = result.Tiles.Single(t => t.TroubleshooterId == "logons-accounts");
        Assert.Equal("Access denied: run as administrator to include", logons.UnavailableReason);
        Assert.Equal(1, result.Tiles.Single(t => t.TroubleshooterId == "disk-storage").Count);
    }

    [Fact] public async Task Service_MissingLog_IsSkipped()
    {
        var source = new FakeEventSource().AddLog("System").AddLog("Application");
        var result = await Service(source).RunAsync(Day, null, CancellationToken.None);
        Assert.Equal([new SkippedLog("Security", "Log not found")], result.Skipped);
    }

    [Fact] public async Task Service_Cancelled_Throws()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service(Source()).RunAsync(Day, null, cts.Token));
    }
}
