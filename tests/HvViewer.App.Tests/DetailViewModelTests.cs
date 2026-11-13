using HvViewer.App.Detail;
using HvViewer.App.Services;
using HvViewer.Core.Model;
using HvViewer.Core.Search;
using HvViewer.Core.Settings;
using HvViewer.Rules;
using HvViewer.Tests.Fakes;
using Xunit;

namespace HvViewer.App.Tests;

public sealed class DetailViewModelTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime EventTime = new(2026, 11, 10, 23, 41, 10, DateTimeKind.Utc);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "evt-detail-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private static EventRecordLite Disk(long recordId = 42) =>
        new("System", recordId, EventTime, 153, EventLevel.Warning, "disk", null, "PC-01", 0, 0);

    private static EventRecordLite Unknown() =>
        new("Application", 7, EventTime, 5, EventLevel.Error, "Foo", null, "PC-01", 0, 0);

    private static RuleSet Rules() => new(
    [
        new Troubleshooter(1, "disk-storage", "Disk & storage", "s",
        [
            new RuleEvent("disk", 153, "System", RuleSeverity.Warning, "A disk read or write had to be retried",
                "Windows asked a disk to repeat an operation.", "A failing disk or cable.", "Check disk health.",
                "https://learn.microsoft.com/search/?terms=disk%20event%20153"),
        ]),
    ], []);

    private (DetailViewModel Vm, FakeEventSource Source, List<string> Clipboard, List<string> Opened) Create(
        EventRecordLite e, FakeEventSource? source = null, bool admin = false, MessageCache? cache = null)
    {
        source ??= new FakeEventSource().AddLog("System", Disk()).AddLog("Application", Unknown()).AddLog("Security");
        var state = new AppState(new SettingsStore(_dir), admin, () => Now);
        state.Update(s => s with { Time = TimeMode.Utc });
        var clipboard = new List<string>();
        var opened = new List<string>();
        var vm = new DetailViewModel(e, source, cache ?? new MessageCache(), Rules(), new SearchService(source, cache ?? new MessageCache()), state,
            url => { opened.Add(url); return true; }, clipboard.Add);
        return (vm, source, clipboard, opened);
    }

    private static FakeEventSource WithDiskMessage() => new FakeEventSource()
        .AddLog("System", Disk()).AddLog("Application", Unknown()).AddLog("Security")
        .WithMessage("System", 42, "The IO operation at logical block address 0x1 for Disk 3 was retried.")
        .WithData("System", 42, ("DeviceName", "\\Device\\Harddisk3\\DR3"), ("Error", "timeout"));

    // ---- loading ----

    [Fact] public async Task Load_FillsMessageXmlAndData()
    {
        var (vm, _, _, _) = Create(Disk(), WithDiskMessage());

        await vm.LoadAsync();

        Assert.Equal("The IO operation at logical block address 0x1 for Disk 3 was retried.", vm.Message);
        Assert.Contains("<EventRecordID>42</EventRecordID>", vm.Xml);
        Assert.Equal([("DeviceName", "\\Device\\Harddisk3\\DR3"), ("Error", "timeout")], vm.Data.Select(p => (p.Key, p.Value)));
        Assert.False(vm.IsLoading);
    }

    [Fact] public async Task Load_Xml_IsIndented()
    {
        var (vm, _, _, _) = Create(Disk(), WithDiskMessage());
        await vm.LoadAsync();
        Assert.Contains(Environment.NewLine, vm.Xml);
    }

    [Fact] public async Task WithAMessage_StartsOnTheExplainedTab()
    {
        var (vm, _, _, _) = Create(Disk(), WithDiskMessage());
        await vm.LoadAsync();
        Assert.Equal("explained", vm.SelectedTab);
    }

    [Fact] public async Task NullMessage_SelectsRawTab_AndSaysSo()
    {
        var (vm, _, _, _) = Create(Disk(), new FakeEventSource().AddLog("System", Disk()).WithMessage("System", 42, null)
            .WithData("System", 42, ("DeviceName", "x")));

        await vm.LoadAsync();

        Assert.Equal("raw", vm.SelectedTab);
        Assert.Equal("Message not available on this PC", vm.MessageText);
        Assert.False(vm.HasMessage);
        Assert.Single(vm.Data);
    }

    [Fact] public async Task MessageTab_ShowsTheMessage()
    {
        var (vm, _, _, _) = Create(Disk(), WithDiskMessage());
        await vm.LoadAsync();
        Assert.StartsWith("The IO operation", vm.MessageText);
    }

    [Fact] public async Task Load_UsesTheMessageCache()
    {
        var cache = new MessageCache();
        cache.GetOrAdd("System", 42, () => "from the cache");
        var (vm, _, _, _) = Create(Disk(), WithDiskMessage(), cache: cache);

        await vm.LoadAsync();

        Assert.Equal("from the cache", vm.Message);
    }

    [Fact] public async Task Load_StoresTheMessageInTheCache()
    {
        var cache = new MessageCache();
        var (vm, _, _, _) = Create(Disk(), WithDiskMessage(), cache: cache);
        await vm.LoadAsync();
        Assert.Equal(1, cache.Count);
    }

    [Fact] public void Tabs_CanBeSelected()
    {
        var (vm, _, _, _) = Create(Disk());
        vm.SelectTabCommand.Execute("xml");
        Assert.Equal("xml", vm.SelectedTab);
        vm.SelectTabCommand.Execute("bogus");
        Assert.Equal("xml", vm.SelectedTab);
    }

    // ---- explanation ----

    [Fact] public void WithARule_ShowsTitleMeaningCausesNextAndLink()
    {
        var (vm, _, _, _) = Create(Disk());
        Assert.True(vm.HasRule);
        Assert.Equal("A disk read or write had to be retried", vm.Title);
        Assert.Equal("Windows asked a disk to repeat an operation.", vm.Meaning);
        Assert.Equal("A failing disk or cable.", vm.Causes);
        Assert.Equal("Check disk health.", vm.Next);
    }

    [Fact] public void NoRule_ShowsNoExplanationText()
    {
        var (vm, _, _, _) = Create(Unknown());
        Assert.False(vm.HasRule);
        Assert.Equal("No explanation yet for Foo 5", vm.NoExplanationText);
        Assert.Equal("Foo event 5", vm.Title);
    }

    [Fact] public void Header_DescribesTheEvent()
    {
        var (vm, _, _, _) = Create(Disk());
        Assert.Equal("Warning · disk · Event 153", vm.Subtitle);
        Assert.Equal("2026-11-10 23:41:10 (UTC)", vm.TimeText);
        Assert.Equal("System", vm.LogName);
        Assert.Equal("PC-01", vm.Computer);
    }

    [Fact] public void OpenDocs_UsesTheRuleLink()
    {
        var (vm, _, _, opened) = Create(Disk());
        vm.OpenDocsCommand.Execute(null);
        Assert.Equal(["https://learn.microsoft.com/search/?terms=disk%20event%20153"], opened);
    }

    [Fact] public void OpenDocs_NoRule_DoesNothing()
    {
        var (vm, _, _, opened) = Create(Unknown());
        Assert.False(vm.OpenDocsCommand.CanExecute(null));
        vm.OpenDocsCommand.Execute(null);
        Assert.Empty(opened);
    }

    // ---- copy for ticket ----

    [Fact] public async Task CopyForTicket_MatchesTemplate()
    {
        var (vm, _, clipboard, _) = Create(Disk(), WithDiskMessage());
        await vm.LoadAsync();

        vm.CopyForTicketCommand.Execute(null);

        var nl = Environment.NewLine;
        Assert.Equal(
            "[Warning] disk 153 — A disk read or write had to be retried" + nl +
            "Computer: PC-01 | Log: System | Time: 2026-11-10 23:41:10 (UTC)" + nl +
            "Message:" + nl +
            "The IO operation at logical block address 0x1 for Disk 3 was retried." + nl +
            "Explanation: Windows asked a disk to repeat an operation. Next: Check disk health.",
            Assert.Single(clipboard));
    }

    [Fact] public async Task CopyForTicket_NoMessage_UsesRawDataLines()
    {
        var source = new FakeEventSource().AddLog("Application", Unknown()).WithMessage("Application", 7, null)
            .WithData("Application", 7, ("Name", "svc.exe"), ("Code", "0xC0000005"));
        var (vm, _, clipboard, _) = Create(Unknown(), source);
        await vm.LoadAsync();

        vm.CopyForTicketCommand.Execute(null);

        var nl = Environment.NewLine;
        Assert.Equal(
            "[Error] Foo 5 — Foo event 5" + nl +
            "Computer: PC-01 | Log: Application | Time: 2026-11-10 23:41:10 (UTC)" + nl +
            "Message:" + nl +
            "Name: svc.exe" + nl +
            "Code: 0xC0000005",
            Assert.Single(clipboard));
    }

    [Fact] public async Task CopyForTicket_NoRule_UsesTheFirstLineOfTheMessageAsTitle()
    {
        var source = new FakeEventSource().AddLog("Application", Unknown()).WithMessage("Application", 7, "Faulting application svc.exe\r\nVersion 1.0");
        var (vm, _, clipboard, _) = Create(Unknown(), source);
        await vm.LoadAsync();

        vm.CopyForTicketCommand.Execute(null);

        Assert.StartsWith("[Error] Foo 5 — Faulting application svc.exe" + Environment.NewLine, Assert.Single(clipboard));
    }

    // ---- around this event ----

    [Fact] public async Task Around_QueriesDashboardLogsFirst()
    {
        var source = new FakeEventSource()
            .AddLog("Microsoft-Windows-Zeta/Operational").AddLog("Application").AddLog("System").AddLog("Microsoft-Windows-Alpha/Operational");
        var (vm, _, _, _) = Create(Disk(), source);

        await vm.LoadAroundAsync();

        // The rules read System (and only System) in this test, so it goes first; the rest follow alphabetically.
        Assert.Equal(["System", "Application", "Microsoft-Windows-Alpha/Operational", "Microsoft-Windows-Zeta/Operational"], source.XPathHistory.Keys.Take(4).Select(k => k));
    }

    [Fact] public async Task Around_UsesPlusMinus60SecondsAndInformationAndWorse()
    {
        var source = new FakeEventSource().AddLog("System");
        var (vm, _, _, _) = Create(Disk(), source);

        await vm.LoadAroundAsync();

        var xpath = source.XPathHistory["System"][0];
        Assert.Contains("@SystemTime>='2026-11-10T23:40:10.000Z'", xpath);
        Assert.Contains("@SystemTime<='2026-11-10T23:42:10.000Z'", xpath);
        Assert.Contains("Level=3", xpath);
        Assert.Contains("Level=4", xpath);
        Assert.DoesNotContain("Level=5", xpath);
    }

    [Fact] public async Task Around_ListsNeighbours_ExcludesTheEventItself_NewestLast()
    {
        var neighbour = new EventRecordLite("Application", 9, EventTime.AddSeconds(20), 1000, EventLevel.Error, "Application Error", null, "PC-01", 0, 0);
        var before = new EventRecordLite("System", 41, EventTime.AddSeconds(-30), 7031, EventLevel.Error, "Service Control Manager", null, "PC-01", 0, 0);
        var farAway = new EventRecordLite("System", 5, EventTime.AddMinutes(10), 1, EventLevel.Error, "Far", null, "PC-01", 0, 0);
        var source = new FakeEventSource().AddLog("System", Disk(), before, farAway).AddLog("Application", neighbour);
        var (vm, _, _, _) = Create(Disk(), source);

        await vm.LoadAroundAsync();

        Assert.Equal([41L, 9L], vm.Around.Select(a => a.Event.RecordId));
        Assert.Equal("−30 s", vm.Around[0].OffsetText);
        Assert.Equal("+20 s", vm.Around[1].OffsetText);
        Assert.False(vm.IsLoadingAround);
        Assert.Contains("2 events", vm.AroundStatus);
    }

    [Fact] public async Task Around_StandardUser_SkipsSecurity()
    {
        var source = new FakeEventSource().AddLog("System").AddLog("Security");
        var (vm, _, _, _) = Create(Disk(), source, admin: false);
        await vm.LoadAroundAsync();
        Assert.DoesNotContain("Security", source.XPathHistory.Keys);
    }

    [Fact] public async Task Around_Admin_IncludesSecurity()
    {
        var source = new FakeEventSource().AddLog("System").AddLog("Security");
        var (vm, _, _, _) = Create(Disk(), source, admin: true);
        await vm.LoadAroundAsync();
        Assert.Contains("Security", source.XPathHistory.Keys);
    }

    [Fact] public async Task Around_NothingFound_SaysSo()
    {
        var (vm, _, _, _) = Create(Disk(), new FakeEventSource().AddLog("System", Disk()));
        await vm.LoadAroundAsync();
        Assert.Empty(vm.Around);
        Assert.Contains("No other events", vm.AroundStatus);
    }

    [Fact] public async Task Around_Cancel_StopsAndKeepsWhatWasFound()
    {
        var many = Enumerable.Range(1, 1_000).Select(i => new EventRecordLite("System", 100 + i, EventTime.AddSeconds(1), i, EventLevel.Warning, "P" + i, null, "PC", 0, 0)).ToArray();
        var source = new FakeEventSource().AddLog("System", many);
        var (vm, _, _, _) = Create(Disk(), source);
        source.OnEvent = n =>
        {
            if (n != 600) return;
            SpinWait.SpinUntil(() => vm.Around.Count > 0, TimeSpan.FromSeconds(10));
            vm.CancelAroundCommand.Execute(null);
        };

        await vm.LoadAroundAsync();

        Assert.NotEmpty(vm.Around);
        Assert.StartsWith("Cancelled", vm.AroundStatus);
        Assert.False(vm.IsLoadingAround);
    }
}
