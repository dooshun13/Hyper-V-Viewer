using System.Diagnostics;
using System.Runtime.Versioning;
using HvViewer.Core.Model;
using HvViewer.Core.Reading;
using HvViewer.Core.Search;
using Xunit;
using Xunit.Abstractions;

namespace HvViewer.Tests.Performance;

/// <summary>
/// Spec section 7 targets, measured on a large .evtx (tests/samples/perf.evtx).
/// Without that file the tests do nothing, so they never fail on a machine that has no sample.
/// On CI hardware the limits are 1.5x.
/// </summary>
[SupportedOSPlatform("windows")]
public class PerformanceTests(ITestOutputHelper output)
{
    private const int Events = 60_000;

    private static double Allowance => Environment.GetEnvironmentVariable("CI") is null ? 1.0 : 1.5;

    private static string? PerfLog()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "samples", "perf.evtx");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    private static readonly TimeRange Everything = new(new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc), DateTime.UtcNow.AddDays(1));

    private static List<EventRecordLite> Load(string path, int count) =>
        new NativeEventSource().Query(LogTarget.File(path), "*", CancellationToken.None).Take(count).ToList();

    [WindowsFact]
    public void Scan60k_BasicFields_Under3s()
    {
        if (PerfLog() is not { } path) return;

        var timer = Stopwatch.StartNew();
        var events = Load(path, Events);
        timer.Stop();

        output.WriteLine($"Scanned {events.Count:N0} events in {timer.Elapsed.TotalSeconds:0.00} s");
        Assert.True(events.Count > 10_000, "The perf log is too small to measure anything.");
        Assert.True(timer.Elapsed.TotalSeconds <= 3 * Allowance, $"{events.Count} events took {timer.Elapsed.TotalSeconds:0.00} s");
    }

    [WindowsFact]
    public async Task TextSearch_About50kEvents_Under5s()
    {
        if (PerfLog() is not { } path) return;

        // Limit the search to the newest ~50,000 events: start the range at the time of the 50,000th newest.
        var newest = Load(path, 50_000);
        var from = newest[^1].TimeCreatedUtc;
        var query = new EventQuery
        {
            Text = "service",
            Logs = [path],
            Range = new TimeRange(from, Everything.ToUtc),
        };
        var search = new SearchService(new NativeEventSource(), new MessageCache());

        var timer = Stopwatch.StartNew();
        SearchUpdate? last = null;
        await foreach (var update in search.RunAsync(query, CancellationToken.None)) last = update;
        timer.Stop();

        output.WriteLine($"Searched {last!.Scanned:N0} events in {timer.Elapsed.TotalSeconds:0.00} s");
        Assert.True(last.Scanned > 10_000);
        Assert.True(timer.Elapsed.TotalSeconds <= 5 * Allowance, $"{last.Scanned} events took {timer.Elapsed.TotalSeconds:0.00} s");
    }

    [WindowsFact]
    public void Filter_LoadedData_Under200ms()
    {
        if (PerfLog() is not { } path) return;
        var events = Load(path, Events);

        var timer = Stopwatch.StartNew();
        var matches = events.Where(e => e.EventId is 7036 or 7040 or 1 && e.Level <= EventLevel.Warning).ToList();
        timer.Stop();

        output.WriteLine($"Filtered {events.Count:N0} events in {timer.Elapsed.TotalMilliseconds:0.0} ms ({matches.Count} kept)");
        Assert.True(timer.Elapsed.TotalMilliseconds <= 200 * Allowance, $"took {timer.Elapsed.TotalMilliseconds:0} ms");
    }

    [WindowsFact]
    public void Memory_After60kScan_Under300MB()
    {
        if (PerfLog() is not { } path) return;

        var before = GC.GetTotalMemory(forceFullCollection: true);
        var events = Load(path, Events);
        var after = GC.GetTotalMemory(forceFullCollection: true);
        GC.KeepAlive(events);

        var megabytes = (after - before) / 1024.0 / 1024.0;
        output.WriteLine($"{events.Count:N0} events held in {megabytes:0.0} MB");
        Assert.True(megabytes <= 300, $"{megabytes:0} MB");
    }
}
