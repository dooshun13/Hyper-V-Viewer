using HvViewer.Core.Model;
using HvViewer.Core.Search;
using HvViewer.Tests.Fakes;
using Xunit;
using static HvViewer.Tests.Core.TestData;

namespace HvViewer.Tests.Core;

public class SearchServiceTests
{
    private static SearchService Service(FakeEventSource source, MessageCache? cache = null) => new(source, cache ?? new MessageCache());

    private static async Task<List<SearchUpdate>> Collect(IAsyncEnumerable<SearchUpdate> updates)
    {
        var all = new List<SearchUpdate>();
        await foreach (var u in updates) all.Add(u);
        return all;
    }

    private static List<SearchHit> Hits(List<SearchUpdate> updates) => updates.SelectMany(u => u.NewHits).ToList();

    [Fact] public async Task Run_NoText_ReturnsAllMatchingFromEachLog()
    {
        var source = new FakeEventSource()
            .AddLog("System", Ev.Make("System", 1, "2026-11-10 10:00:00"), Ev.Make("System", 2, "2026-11-10 11:00:00"), Ev.Make("System", 3, "2026-11-10 12:00:00"))
            .AddLog("Application", Ev.Make("Application", 1, "2026-11-10 10:00:00"), Ev.Make("Application", 2, "2026-11-10 11:00:00"));

        var updates = await Collect(Service(source).RunAsync(Q(logs: ["System", "Application"]), CancellationToken.None));

        Assert.Equal(5, Hits(updates).Count);
        Assert.True(updates[^1].Completed);
        Assert.Equal(5, updates[^1].Scanned);
        Assert.All(updates.SkipLast(1), u => Assert.False(u.Completed));
    }

    [Fact] public async Task Run_TextSearch_FiltersAndReturnsSpans()
    {
        var source = new FakeEventSource()
            .AddLog("System", Ev.Make("System", 1, "2026-11-10 10:00:00"), Ev.Make("System", 2, "2026-11-10 11:00:00"))
            .WithMessage("System", 1, "Disk 3 was retried.")
            .WithMessage("System", 2, "Something else.");

        var updates = await Collect(Service(source).RunAsync(Q(text: "Disk 3"), CancellationToken.None));

        var hit = Assert.Single(Hits(updates));
        Assert.Equal(1, hit.Event.RecordId);
        Assert.Equal([(0, 6)], hit.Matches);
        Assert.Equal("Disk 3 was retried.", hit.Text);
        Assert.Equal(2, updates[^1].Scanned);
    }

    [Fact] public async Task Run_TextSearch_NullMessage_FallsBackToEventDataText()
    {
        var source = new FakeEventSource()
            .AddLog("System", Ev.Make("System", 1, "2026-11-10 10:00:00"))
            .WithMessage("System", 1, null)
            .WithData("System", 1, ("DeviceName", "Disk 3"), ("Error", "timeout"));

        var updates = await Collect(Service(source).RunAsync(Q(text: "disk 3"), CancellationToken.None));

        Assert.Single(Hits(updates));
    }

    [Fact] public async Task Run_AccessDeniedLog_IsSkippedAndScanContinues()
    {
        var source = new FakeEventSource()
            .Deny("Security")
            .AddLog("System", Ev.Make("System", 1, "2026-11-10 10:00:00"));

        var updates = await Collect(Service(source).RunAsync(Q(logs: ["Security", "System"]), CancellationToken.None));

        Assert.Equal([new SkippedLog("Security", "Access denied: run as administrator to include")], updates[^1].Skipped);
        Assert.Single(Hits(updates));
        Assert.True(updates[^1].Completed);
    }

    [Fact] public async Task Run_MissingLog_IsSkippedAsNotFound()
    {
        var updates = await Collect(Service(new FakeEventSource()).RunAsync(Q(logs: ["Nope"]), CancellationToken.None));
        Assert.Equal([new SkippedLog("Nope", "Log not found")], updates[^1].Skipped);
    }

    [Fact] public async Task Run_Cancelled_StopsQuickly()
    {
        var many = Enumerable.Range(1, 200_000).Select(i => Ev.Make("System", i, "2026-11-10 10:00:00")).ToArray();
        var source = new FakeEventSource().AddLog("System", many);
        using var cts = new CancellationTokenSource();
        var seen = 0;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var u in Service(source).RunAsync(Q(), cts.Token))
            {
                seen += u.NewHits.Count;
                cts.Cancel();
            }
        });

        Assert.InRange(seen, 1, 100_000);
    }

    [Fact] public async Task Run_MoreThan20Ids_FilteredInProcess()
    {
        var source = new FakeEventSource().AddLog("System",
            Ev.Make("System", 1, "2026-11-10 10:00:00", id: 5), Ev.Make("System", 2, "2026-11-10 11:00:00", id: 500));

        var updates = await Collect(Service(source).RunAsync(Q(include: Enumerable.Range(1, 21).ToArray()), CancellationToken.None));

        Assert.Equal(5, Assert.Single(Hits(updates)).Event.EventId);
    }

    [Fact] public async Task Run_TaskFilter_FiltersByProviderAndTask()
    {
        var source = new FakeEventSource().AddLog("System",
            Ev.Make("System", 1, "2026-11-10 10:00:00", provider: "P", task: 7),
            Ev.Make("System", 2, "2026-11-10 11:00:00", provider: "P", task: 8),
            Ev.Make("System", 3, "2026-11-10 12:00:00", provider: "Other", task: 7));

        var updates = await Collect(Service(source).RunAsync(Q(tasks: [new TaskFilter("P", 7)]), CancellationToken.None));

        Assert.Equal(1, Assert.Single(Hits(updates)).Event.RecordId);
    }

    [Fact] public async Task Run_StreamsInBatchesOf256()
    {
        var events = Enumerable.Range(1, 600).Select(i => Ev.Make("System", i, "2026-11-10 10:00:00")).ToArray();
        var updates = await Collect(Service(new FakeEventSource().AddLog("System", events)).RunAsync(Q(), CancellationToken.None));

        Assert.Equal(600, Hits(updates).Count);
        Assert.True(updates.Count >= 3);
        Assert.All(updates, u => Assert.True(u.NewHits.Count <= 256));
    }

    [Fact] public async Task Run_MessageSearch_FillsMessageCache()
    {
        var source = new FakeEventSource()
            .AddLog("System", Ev.Make("System", 1, "2026-11-10 10:00:00"))
            .WithMessage("System", 1, "Disk 3");
        var cache = new MessageCache();

        await Collect(Service(source, cache).RunAsync(Q(text: "disk"), CancellationToken.None));

        Assert.Equal(1, cache.Count);
    }

    [Fact] public async Task Run_EvtxFilePath_IsReadAsAFile_AndChannelsAsChannels()
    {
        var path = @"C:\logs\saved.evtx";
        var source = new FakeEventSource().AddLog(path, Ev.Make(path, 1, "2026-11-10 10:00:00")).AddLog("System");

        await Collect(Service(source).RunAsync(Q(logs: [path, "System"]), CancellationToken.None));

        Assert.Equal([new LogTarget(path, true), new LogTarget("System", false)], source.Targets);
    }

    [Fact] public void Run_InvalidQuery_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() => Service(new FakeEventSource()).RunAsync(Q(logs: []), CancellationToken.None));
        Assert.Contains("Select at least one log.", ex.Message);
    }
}
