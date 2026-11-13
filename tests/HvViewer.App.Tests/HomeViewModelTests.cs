using HvViewer.App.Home;
using HvViewer.App.Services;
using HvViewer.Core.Model;
using HvViewer.Core.Search;
using HvViewer.Core.Settings;
using HvViewer.Machine;
using HvViewer.Machine.Health;
using HvViewer.Rules;
using HvViewer.Tests.Fakes;
using Xunit;

namespace HvViewer.App.Tests;

public sealed class FakeNavigator : INavigator
{
    public List<string> Shown { get; } = [];

    public List<EventQuery> Queries { get; } = [];

    public void Show(string key) => Shown.Add(key);

    public void ShowEvents(EventQuery query) => Queries.Add(query);
}

public sealed class FakeHardware(HardwareInfo? info = null, long? available = 18_900_000_000, bool none = false) : IHardwareProvider
{
    private const long Gb = 1024L * 1024 * 1024;

    public HardwareInfo? Read() => none ? null : info ?? new HardwareInfo("AMD Ryzen 5 5500", 6, 12, 3.6, 34_100_000_000,
        [new MemoryModule(16 * Gb, 2133, "DDR4"), new MemoryModule(16 * Gb, 2133, "DDR4")]);

    public long? AvailableRamBytes() => available;
}

public sealed class HomeViewModelTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "evt-home-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private static RuleEvent Rule(string provider, int id, string log, RuleSeverity severity, string title = "Rule title") =>
        new(provider, id, log, severity, title, "m", "c", "n", "https://learn.microsoft.com/x");

    private static RuleSet Rules() => new(
    [
        new Troubleshooter(1, "disk-storage", "Disk & storage", "s", [Rule("disk", 153, "System", RuleSeverity.Warning, "A disk read or write had to be retried")]),
        new Troubleshooter(1, "crashes-hangs", "Crashes & hangs", "s", [Rule("Application Error", 1000, "Application", RuleSeverity.Error, "An application crashed")]),
        new Troubleshooter(1, "services", "Services", "s", [Rule("Service Control Manager", 7031, "System", RuleSeverity.Error, "A service crashed")]),
        new Troubleshooter(1, "logons-accounts", "Logons & accounts", "s", [Rule("Microsoft-Windows-Security-Auditing", 4625, "Security", RuleSeverity.Warning)]),
        new Troubleshooter(1, "boot-shutdown", "Boot & shutdown", "s",
        [
            Rule("Microsoft-Windows-Kernel-Power", 41, "System", RuleSeverity.Critical),
            Rule("EventLog", 6005, "System", RuleSeverity.Info),
        ]),
    ], []);

    private static SystemInfo Info() => new("PC-01", "Windows 11 Pro", "Professional", "23H2", "22631.4037", MachineKind.Workstation,
        false, true, "VMware", "corp.local", Now.AddHours(-7), TimeSpan.FromHours(7));

    private static HealthResult Health(string id, HealthStatus status, string value = "x") => new(id, id, status, value, "d");

    private static EventRecordLite E(string log, long id, string time, int eventId, string provider, EventLevel level) =>
        Ev.Make(log, id, "2026-11-11 " + time, eventId, provider, level);

    private (HomeViewModel Vm, FakeNavigator Nav, List<DashboardResult> Partials) Create(FakeEventSource source, IReadOnlyList<HealthResult>? health = null, IHardwareProvider? hardware = null)
    {
        var state = new AppState(new SettingsStore(_dir), isAdmin: true, () => Now);
        var rules = Rules();
        var nav = new FakeNavigator();
        var partials = new List<DashboardResult>();
        var vm = new HomeViewModel(
            new DashboardService(source, rules),
            () => health ?? [Health("disk", HealthStatus.Ok), Health("reboot", HealthStatus.Ok)],
            Info(), hardware ?? new FakeHardware(), rules, nav, state,
            callback => new SyncProgress<DashboardResult>(r => { partials.Add(r); callback(r); }));
        return (vm, nav, partials);
    }

    private static FakeEventSource Quiet() => new FakeEventSource().AddLog("System").AddLog("Application").AddLog("Security");

    // ---- headline ----

    [Fact] public async Task Headline_AllClear()
    {
        var (vm, _, _) = Create(Quiet());
        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.Equal("All clear in the last 24 hours", vm.Headline);
    }

    [Fact] public async Task Headline_OneThing()
    {
        var source = Quiet().AddLog("Application", E("Application", 1, "10:00:00", 1000, "Application Error", EventLevel.Error));
        var (vm, _, _) = Create(source);
        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.Equal("1 thing needs a look", vm.Headline);
    }

    [Fact] public async Task Headline_CountsErrorTilesAndBadHealth()
    {
        var source = Quiet()
            .AddLog("Application", E("Application", 1, "10:00:00", 1000, "Application Error", EventLevel.Error))
            .AddLog("System", E("System", 2, "09:00:00", 7031, "Service Control Manager", EventLevel.Error),
                              E("System", 3, "08:00:00", 153, "disk", EventLevel.Warning)); // warning tile: not counted
        var (vm, _, _) = Create(source, [Health("disk", HealthStatus.Problem), Health("reboot", HealthStatus.Ok), Health("certs", HealthStatus.Unknown)]);

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Equal("3 things need a look", vm.Headline);
    }

    [Fact] public async Task Headline_RestartStoryPresent()
    {
        var source = Quiet().AddLog("System",
            E("System", 1, "09:00:00", 153, "disk", EventLevel.Warning),
            E("System", 2, "09:10:00", 6005, "EventLog", EventLevel.Information),
            E("System", 3, "09:10:20", 41, "Microsoft-Windows-Kernel-Power", EventLevel.Critical));
        var (vm, _, _) = Create(source);

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.StartsWith("Restarted unexpectedly — ", vm.Headline);
        Assert.NotNull(vm.Result?.Restart);
        Assert.NotEmpty(vm.RestartRows);
    }

    [Fact] public async Task Headline_ChangesWithRange()
    {
        var (vm, _, _) = Create(Quiet());
        vm.SetRangeCommand.Execute("7d");
        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.Equal("All clear in the last 7 days", vm.Headline);
        Assert.Equal("7d", vm.RangeKey);
    }

    // ---- refresh ----

    [Fact] public async Task Refresh_ShowsPartialThenFinal()
    {
        var source = Quiet().AddLog("System", E("System", 1, "10:00:00", 153, "disk", EventLevel.Warning));
        var (vm, _, partials) = Create(source);
        var loadingStates = new List<bool>();
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(HomeViewModel.IsLoading)) loadingStates.Add(vm.IsLoading); };

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(3, partials.Count);          // System, Application, Security
        Assert.Equal([true, false], loadingStates);
        Assert.False(vm.IsLoading);
        Assert.Contains(vm.Tiles, t => t.TroubleshooterId == "disk-storage" && t.ValueText == "1");
    }

    [Fact] public async Task Refresh_FillsSystemAndHealth()
    {
        var (vm, _, _) = Create(Quiet(), [Health("disk", HealthStatus.Warning, "C: 12% free")]);
        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Equal("Windows 11 Pro", vm.SystemTitle);
        Assert.Contains("22631.4037", vm.SystemSubtitle);
        Assert.Contains("virtual machine (VMware)", vm.SystemSubtitle);
        Assert.Equal("7 h 0 min", vm.Uptime);
        Assert.Equal("corp.local", vm.Domain);
        var row = Assert.Single(vm.HealthRows);
        Assert.Equal(("C: 12% free", "!"), (row.Value, row.Mark));
        Assert.True(vm.HasHardware);
    }

    [Fact] public async Task Refresh_BuildsChartBarsForEveryBucket()
    {
        var (vm, _, _) = Create(Quiet().AddLog("System", E("System", 1, "10:30:00", 7031, "Service Control Manager", EventLevel.Error)));
        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(vm.Result!.Buckets.Count, vm.Bars.Count);
        Assert.Contains(vm.Bars, b => b.ErrorHeight > 0);
        Assert.Equal(5, vm.AxisLabels.Count);
    }

    [Fact] public async Task Refresh_SeverityRows_ShowCountsAndDeltas()
    {
        var source = Quiet().AddLog("System",
            E("System", 1, "10:00:00", 1, "Foo", EventLevel.Error), E("System", 2, "11:00:00", 2, "Foo", EventLevel.Error));
        var (vm, _, _) = Create(source);

        await vm.RefreshCommand.ExecuteAsync(null);

        var error = vm.SeverityRows.Single(r => r.Label == "Error");
        Assert.Equal(2, error.Value);
        Assert.Equal("+2", error.Delta);
        Assert.Equal("", vm.SeverityRows.Single(r => r.Label == "Information").Delta);
    }

    [Fact] public async Task Refresh_ErrorFromScan_IsShownNotThrown()
    {
        var failing = new ThrowingSource();
        var state = new AppState(new SettingsStore(_dir), true, () => Now);
        var rules = Rules();
        var vm = new HomeViewModel(new DashboardService(failing, rules), () => [], Info(), new FakeHardware(), rules, new FakeNavigator(), state,
            callback => new SyncProgress<DashboardResult>(callback));

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.False(vm.IsLoading);
        Assert.Contains("boom", vm.ErrorMessage);
    }

    private sealed class ThrowingSource : HvViewer.Core.Reading.IEventSource
    {
        public IEnumerable<EventRecordLite> Query(LogTarget log, string xpath, CancellationToken ct) => throw new InvalidOperationException("boom");

        public IEnumerable<(EventRecordLite Event, string? Text)> QueryWithText(LogTarget log, string xpath, TextTarget target, CancellationToken ct) => [];

        public string? FormatMessage(LogTarget log, long recordId) => null;

        public string? GetXml(LogTarget log, long recordId) => null;

        public IReadOnlyList<KeyValuePair<string, string>> GetEventData(LogTarget log, long recordId) => [];

        public string? GetTaskName(string provider, int task) => null;

        public IReadOnlyList<ChannelInfo> GetChannels() => [];
    }

    // ---- tiles ----

    [Fact] public async Task UnavailableTile_ShowsReason()
    {
        var source = new FakeEventSource().AddLog("System").AddLog("Application").Deny("Security");
        var (vm, _, _) = Create(source);

        await vm.RefreshCommand.ExecuteAsync(null);

        var tile = vm.Tiles.Single(t => t.TroubleshooterId == "logons-accounts");
        Assert.True(tile.IsUnavailable);
        Assert.Equal("—", tile.ValueText);
        Assert.Equal("Access denied: run as administrator to include", tile.Hint);
        Assert.Contains("Security", vm.SkippedText);
    }

    [Fact] public async Task OpenTile_NavigatesToEventsWithTroubleshooterQuery()
    {
        var source = Quiet().AddLog("System", E("System", 1, "10:00:00", 153, "disk", EventLevel.Warning));
        var (vm, nav, _) = Create(source);
        await vm.RefreshCommand.ExecuteAsync(null);

        vm.OpenTileCommand.Execute(vm.Tiles.Single(t => t.TroubleshooterId == "disk-storage"));

        var q = Assert.Single(nav.Queries);
        Assert.Equal(["System"], q.Logs);
        Assert.Equal([153], q.IncludeIds);
        Assert.Equal(TimeRange.Last(TimeSpan.FromHours(24), Now), q.Range);
        Assert.Empty(q.Validate());
    }

    [Fact] public async Task OpenSeverity_FiltersByThatLevel()
    {
        var (vm, nav, _) = Create(Quiet());
        await vm.RefreshCommand.ExecuteAsync(null);

        vm.OpenSeverityCommand.Execute(vm.SeverityRows.Single(r => r.Label == "Error"));

        Assert.Equal([EventLevel.Error], Assert.Single(nav.Queries).Levels);
    }

    [Fact] public async Task OpenUnusual_FiltersByProviderAndId()
    {
        var source = Quiet().AddLog("System", E("System", 1, "10:00:00", 99, "Foo", EventLevel.Error));
        var (vm, nav, _) = Create(source);
        await vm.RefreshCommand.ExecuteAsync(null);

        vm.OpenUnusualCommand.Execute(Assert.Single(vm.UnusualRows));

        var q = Assert.Single(nav.Queries);
        Assert.Equal(["Foo"], q.Sources);
        Assert.Equal([99], q.IncludeIds);
    }


    // ---- processor and memory ----

    [Fact] public async Task Refresh_ShowsProcessorAndMemory()
    {
        var (vm, _, _) = Create(Quiet());
        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.True(vm.HasHardware);
        Assert.Equal("AMD Ryzen 5 5500", vm.ProcessorName);
        Assert.Equal("6 cores · 12 threads · 3.6 GHz", vm.ProcessorDetail);
        Assert.Equal("31.8 GB", vm.MemoryTotalText);
        Assert.Equal("14.2 GB used", vm.MemoryUsedText);
        Assert.Equal(45, vm.MemoryPercent);
        Assert.Equal("2 × 16 GB · DDR4 · 2133 MHz", vm.MemoryDetail);
    }

    [Fact] public async Task Refresh_HardwareUnavailable_HidesTheCpuAndMemoryBlocks()
    {
        var (vm, _, _) = Create(Quiet(), hardware: new FakeHardware(none: true));
        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.False(vm.HasHardware);
    }

    [Fact] public async Task Refresh_MemoryInUse_IsReadAgainEachTime()
    {
        var hardware = new FakeHardware(available: 30_000_000_000);
        var (vm, _, _) = Create(Quiet(), hardware: hardware);
        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.Equal(12, vm.MemoryPercent);
    }

    // ---- the page must fit one screen, so long lists are shortened ----

    [Fact] public async Task UnusualRows_ShowOnlyTheNewestThree_AndSayHowManyMore()
    {
        var events = Enumerable.Range(1, 5).Select(i => E("System", i, $"0{i}:00:00", 100 + i, "P" + i, EventLevel.Error)).ToArray();
        var (vm, _, _) = Create(Quiet().AddLog("System", events));

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(3, vm.UnusualRows.Count);
        Assert.Equal(105, vm.UnusualRows[0].EventId);
        Assert.Equal("+2 more in Events", vm.UnusualMoreText);
    }

    [Fact] public async Task UnusualRows_ThreeOrFewer_HaveNoMoreText()
    {
        var (vm, _, _) = Create(Quiet().AddLog("System", E("System", 1, "01:00:00", 100, "P", EventLevel.Error)));
        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.Equal("", vm.UnusualMoreText);
    }

    [Fact] public async Task RestartRows_ShowOnlyTheThreeNearestTheRestart()
    {
        var before = Enumerable.Range(1, 5).Select(i => E("System", i, $"09:0{i}:00", 200 + i, "P" + i, EventLevel.Warning)).ToList();
        before.Add(E("System", 10, "09:20:00", 6005, "EventLog", EventLevel.Information));
        before.Add(E("System", 11, "09:20:20", 41, "Microsoft-Windows-Kernel-Power", EventLevel.Critical));
        var (vm, _, _) = Create(Quiet().AddLog("System", before.ToArray()));

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(3, vm.RestartRows.Count);
        // newest first; shown in this PC's time zone, which is the default
        var expected = TimeZoneInfo.ConvertTimeFromUtc(new DateTime(2026, 11, 11, 9, 5, 0, DateTimeKind.Utc), TimeZoneInfo.Local).ToString("HH:mm:ss");
        Assert.Equal(expected, vm.RestartRows[0].Time);
    }
    // ---- theme switch on the Home page ----

    [Fact] public void ThemeSwitch_StartsOnAuto_AndChangesTheSavedTheme()
    {
        var (vm, _, _) = Create(Quiet());
        Assert.True(vm.IsThemeAuto);

        vm.IsThemeNight = true;
        Assert.Equal(ThemeMode.Dark, new SettingsStore(_dir).Load().Theme);
        Assert.True(vm.IsThemeNight);
        Assert.False(vm.IsThemeAuto);

        vm.IsThemeDay = true;
        Assert.Equal(ThemeMode.Light, new SettingsStore(_dir).Load().Theme);
        Assert.True(vm.IsThemeDay);
    }

    [Fact] public void ThemeSwitch_RaisesChangeForTheRadioButtons()
    {
        var (vm, _, _) = Create(Quiet());
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.IsThemeNight = true;

        Assert.Contains(nameof(HomeViewModel.IsThemeNight), raised);
        Assert.Contains(nameof(HomeViewModel.IsThemeAuto), raised);
        Assert.Contains(nameof(HomeViewModel.IsThemeDay), raised);
    }

    // ---- range ----

    [Fact] public void SetRange_Custom_NeedsValidDates()
    {
        var (vm, _, _) = Create(Quiet());
        vm.SetRangeCommand.Execute("custom");
        vm.CustomFrom = "nonsense";
        vm.ApplyCustomRangeCommand.Execute(null);
        Assert.NotEmpty(vm.CustomError);
    }

    [Fact] public async Task CustomRange_UsesTypedDatesInTheChosenZone()
    {
        var (vm, _, partials) = Create(Quiet());
        vm.SetRangeCommand.Execute("custom");
        vm.CustomFrom = "2026-11-10 00:00:00";
        vm.CustomTo = "2026-11-11 00:00:00";

        await vm.ApplyCustomRangeCommand.ExecuteAsync(null);

        Assert.Equal("", vm.CustomError);
        Assert.Equal("custom", vm.RangeKey);
        Assert.NotEmpty(partials);
        Assert.Equal(24, vm.Result!.Buckets.Count);
    }
}
