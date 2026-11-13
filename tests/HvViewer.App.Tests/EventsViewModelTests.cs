using CommunityToolkit.Mvvm.ComponentModel;
using HvViewer.App.Events;
using HvViewer.App.Services;
using HvViewer.Core.Export;
using HvViewer.Core.Model;
using HvViewer.Core.Search;
using HvViewer.Core.Settings;
using HvViewer.Rules;
using HvViewer.Tests.Fakes;
using Xunit;

namespace HvViewer.App.Tests;

public sealed class FakeExporter : IExportService
{
    public List<(string Title, string Subtitle, IReadOnlyList<ExportRow> Rows)> Calls { get; } = [];

    public string? Error { get; set; }

    public string? Export(string title, string subtitle, IReadOnlyList<ExportRow> rows)
    {
        Calls.Add((title, subtitle, rows));
        return Error;
    }
}

public sealed class EventsViewModelTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Boot = new(2026, 11, 10, 22, 30, 0, DateTimeKind.Utc);
    private const string MockupLine = "\"Disk 3\" word from:2026-11-10 22:00:00 to:2026-11-11 04:00:00 id:153,-7036 level:>=warning";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "evt-events-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private static EventRecordLite E(string log, long id, string time, int eventId = 153, string provider = "disk",
        EventLevel level = EventLevel.Warning, int task = 0) =>
        new(log, id, DateTime.SpecifyKind(DateTime.Parse("2026-11-11 " + time, System.Globalization.CultureInfo.InvariantCulture), DateTimeKind.Utc),
            eventId, level, provider, null, "PC", task, 0);

    private static RuleSet Rules() => new(
    [
        new Troubleshooter(1, "disk-storage", "Disk & storage", "s",
            [new RuleEvent("disk", 153, "System", RuleSeverity.Warning, "A disk read or write had to be retried", "m", "c", "n", "https://learn.microsoft.com/x")]),
    ], []);

    private static FakeEventSource Source(params EventRecordLite[] system) =>
        new FakeEventSource().AddLog("System", system).AddLog("Application").AddLog("Security");

    private (EventsViewModel Vm, AppState State, FakeExporter Exporter) Create(FakeEventSource source, bool admin = false, bool utc = true,
        Func<EventRecordLite, ObservableObject?>? detail = null)
    {
        var state = new AppState(new SettingsStore(_dir), admin, () => Now);
        if (utc) state.Update(s => s with { Time = TimeMode.Utc });
        var exporter = new FakeExporter();
        var vm = new EventsViewModel(new SearchService(source, new MessageCache()), source, Rules(), state, Boot, exporter, detail);
        return (vm, state, exporter);
    }

    private static void FillMockup(EventsViewModel vm)
    {
        vm.Text = "Disk 3";
        vm.WholeWord = true;
        vm.FromText = "2026-11-10 22:00:00";
        vm.ToText = "2026-11-11 04:00:00";
        vm.Ids = "153, -7036";
    }

    // ---- query line ----

    [Fact] public void EditingFields_UpdatesQueryLine()
    {
        var (vm, _, _) = Create(Source());
        FillMockup(vm);
        Assert.Equal(MockupLine, vm.QueryLine);
    }

    [Fact] public void FreshScreen_QueryLineIsLast24HoursWithWarningAndUp()
    {
        var (vm, _, _) = Create(Source());
        Assert.Equal("last:24h level:>=warning", vm.QueryLine);
        Assert.Equal("24h", vm.PresetKey);
    }

    [Fact] public void QueryLineEnter_UpdatesFields()
    {
        var (vm, _, _) = Create(Source());

        vm.QueryLine = MockupLine;
        vm.ApplyQueryLineCommand.Execute(null);

        Assert.Equal("Disk 3", vm.Text);
        Assert.True(vm.WholeWord);
        Assert.False(vm.MatchCase);
        Assert.Equal("2026-11-10 22:00:00", vm.FromText);
        Assert.Equal("2026-11-11 04:00:00", vm.ToText);
        Assert.Equal("153, -7036", vm.Ids);
        Assert.Equal("custom", vm.PresetKey);
        Assert.Equal([EventLevel.Critical, EventLevel.Error, EventLevel.Warning], vm.SelectedLevels);
        Assert.Equal("", vm.QueryLineError);
        Assert.Equal(MockupLine, vm.QueryLine);
    }

    [Fact] public void QueryLine_Last7d_SelectsThePreset()
    {
        var (vm, _, _) = Create(Source());
        vm.QueryLine = "disk last:7d";
        vm.ApplyQueryLineCommand.Execute(null);
        Assert.Equal("7d", vm.PresetKey);
        Assert.Equal("disk", vm.Text);
    }

    [Fact] public void QueryLine_Invalid_ShowsErrorAndKeepsFields()
    {
        var (vm, _, _) = Create(Source());
        vm.Text = "keep me";

        vm.QueryLine = "foo:1";
        vm.ApplyQueryLineCommand.Execute(null);

        Assert.Equal("Unknown filter 'foo'", vm.QueryLineError);
        Assert.Equal("keep me", vm.Text);
    }

    [Fact] public void QueryLine_SelectsLogsFromTheLine()
    {
        var (vm, _, _) = Create(Source());
        vm.QueryLine = "log:System,Setup";
        vm.ApplyQueryLineCommand.Execute(null);
        Assert.Equal(["System", "Setup"], vm.Logs.Selected);
    }

    [Fact] public void ChangingTheLogs_UpdatesTheQueryLine()
    {
        var (vm, _, _) = Create(Source());
        vm.Logs.SetSelected(["System"]);
        Assert.Contains("log:System", vm.QueryLine);
    }

    // ---- validation ----

    [Fact] public async Task InvalidRegex_ShowsErrorAndDoesNotSearch()
    {
        var source = Source(E("System", 1, "10:00:00"));
        var (vm, _, _) = Create(source);
        vm.Text = "([";
        vm.UseRegex = true;

        await vm.SearchCommand.ExecuteAsync(null);

        Assert.Contains(vm.Errors, e => e.StartsWith("Invalid regular expression"));
        Assert.Empty(vm.Rows);
        Assert.Empty(source.XPathHistory);
    }

    [Fact] public void FromAfterTo_ShowsError()
    {
        var (vm, _, _) = Create(Source());
        vm.FromText = "2026-11-11 04:00:00";
        vm.ToText = "2026-11-10 22:00:00";
        Assert.Contains("From must be before To.", vm.Errors);
    }

    [Fact] public void BadDate_ShowsError()
    {
        var (vm, _, _) = Create(Source());
        vm.FromText = "yesterday-ish";
        Assert.Contains(vm.Errors, e => e.Contains("From"));
    }

    [Fact] public void BadIds_ShowsError()
    {
        var (vm, _, _) = Create(Source());
        vm.Ids = "153, abc";
        Assert.Contains(vm.Errors, e => e.Contains("abc"));
    }

    [Fact] public void ValidFields_HaveNoErrors()
    {
        var (vm, _, _) = Create(Source());
        FillMockup(vm);
        Assert.Empty(vm.Errors);
    }

    // ---- presets ----

    [Fact] public void PresetToday_SetsFromToLocalMidnight()
    {
        var (vm, state, _) = Create(Source(), utc: false);

        vm.SelectPresetCommand.Execute("today");

        var midnight = TimeZoneInfo.ConvertTimeFromUtc(Now, state.Zone).Date;
        Assert.Equal(midnight.ToString("yyyy-MM-dd 00:00:00"), vm.FromText);
        Assert.Equal(state.FormatTime(Now), vm.ToText);
        Assert.Equal("today", vm.PresetKey);
    }

    [Fact] public void PresetBoot_StartsAtTheLastBoot()
    {
        var (vm, _, _) = Create(Source());
        vm.SelectPresetCommand.Execute("boot");
        Assert.Equal("2026-11-10 22:30:00", vm.FromText);
    }

    [Fact] public void PresetYesterday_IsTheWholePreviousDay()
    {
        var (vm, _, _) = Create(Source());
        vm.SelectPresetCommand.Execute("yesterday");
        Assert.Equal("2026-11-10 00:00:00", vm.FromText);
        Assert.Equal("2026-11-11 00:00:00", vm.ToText);
    }

    [Theory]
    [InlineData("15m", "2026-11-11 11:45:00")]
    [InlineData("1h", "2026-11-11 11:00:00")]
    [InlineData("24h", "2026-11-10 12:00:00")]
    public void PresetLast_SetsFromTo(string key, string from)
    {
        var (vm, _, _) = Create(Source());
        vm.SelectPresetCommand.Execute(key);
        Assert.Equal(from, vm.FromText);
        Assert.Equal("2026-11-11 12:00:00", vm.ToText);
    }

    [Fact] public void EditingTheDates_SwitchesToCustom()
    {
        var (vm, _, _) = Create(Source());
        vm.FromText = "2026-11-10 22:00:00";
        Assert.Equal("custom", vm.PresetKey);
    }

    // ---- searching ----

    [Fact] public async Task Search_StreamsRowsAndStatus()
    {
        var (vm, _, _) = Create(Source(
            E("System", 1, "10:00:00"), E("System", 2, "10:00:30"), E("System", 3, "10:01:00"),   // one group of 3
            E("System", 4, "11:00:00", 7031, "Service Control Manager")));

        await vm.SearchCommand.ExecuteAsync(null);

        Assert.Equal("Searched 4 events · 4 matches · 2 groups", vm.Status);
        Assert.Equal(2, vm.Rows.Count);
        Assert.Equal("7031", vm.Rows[0].EventId.ToString());       // newest first
        Assert.Equal("×3", vm.Rows[1].CountText);
        Assert.Equal("", vm.Rows[0].CountText);
        Assert.False(vm.IsSearching);
        Assert.False(vm.IsEmpty);
    }

    [Fact] public async Task Search_RowsShowTimeAndRuleTitle()
    {
        var (vm, _, _) = Create(Source(E("System", 1, "10:15:20")));
        await vm.SearchCommand.ExecuteAsync(null);

        var row = Assert.Single(vm.Rows);
        Assert.Equal("10:15:20", row.TimeText);
        Assert.Equal("2026-11-11", row.DateText);
        Assert.Equal("A disk read or write had to be retried", row.Title);
        Assert.Equal("Warning", row.LevelText);
    }

    [Fact] public async Task Search_TextFiltersAndKeepsHighlightSpans()
    {
        var source = Source(E("System", 1, "10:00:00"), E("System", 2, "11:00:00", 7031, "Service Control Manager"))
            .WithMessage("System", 1, "The IO operation at Disk 3 was retried.")
            .WithMessage("System", 2, "Service stopped.");
        var (vm, _, _) = Create(source);
        vm.Text = "disk 3";

        await vm.SearchCommand.ExecuteAsync(null);

        var row = Assert.Single(vm.Rows);
        Assert.Equal("The IO operation at Disk 3 was retried.", row.Message);
        Assert.Equal([(20, 6)], row.Matches);
        Assert.Equal("Searched 2 events · 1 match · 1 group", vm.Status);
    }

    [Fact] public async Task Search_MessageNewlines_AreFlattenedWithoutMovingTheMatches()
    {
        var source = Source(E("System", 1, "10:00:00")).WithMessage("System", 1, "line one\r\nDisk 3 here");
        var (vm, _, _) = Create(source);
        vm.Text = "Disk 3";
        await vm.SearchCommand.ExecuteAsync(null);

        var row = Assert.Single(vm.Rows);
        Assert.DoesNotContain('\n', row.Message!);
        Assert.Equal("Disk 3", row.Message!.Substring(row.Matches[0].Start, row.Matches[0].Length));
    }

    [Fact] public async Task Search_NoMatches_IsEmpty()
    {
        var (vm, _, _) = Create(Source());
        await vm.SearchCommand.ExecuteAsync(null);
        Assert.True(vm.IsEmpty);
        Assert.Equal("Searched 0 events · 0 matches · 0 groups", vm.Status);
    }

    [Fact] public async Task Search_SingularStatusWording()
    {
        var (vm, _, _) = Create(Source(E("System", 1, "10:00:00")));
        await vm.SearchCommand.ExecuteAsync(null);
        Assert.Equal("Searched 1 event · 1 match · 1 group", vm.Status);
    }

    [Fact] public async Task Search_SkippedLogs_AreListed()
    {
        var source = new FakeEventSource().AddLog("System", E("System", 1, "10:00:00")).AddLog("Application").Deny("Security");
        var (vm, _, _) = Create(source, admin: true);

        await vm.SearchCommand.ExecuteAsync(null);

        Assert.Contains("Security", vm.SkippedText);
        Assert.Contains("Access denied", vm.SkippedText);
        Assert.Single(vm.Rows);
    }

    [Fact] public async Task Search_BuildsA36BarHistogram()
    {
        var (vm, _, _) = Create(Source(E("System", 1, "10:00:00"), E("System", 2, "10:05:00", 5, "x")));
        await vm.SearchCommand.ExecuteAsync(null);
        Assert.Equal(36, vm.Histogram.Count);
        Assert.Contains(vm.Histogram, b => b.Height > 0);
    }

    [Fact] public async Task Search_FillsLogCounts()
    {
        var (vm, _, _) = Create(Source(E("System", 1, "10:00:00"), E("System", 2, "10:05:00", 5, "x")));
        vm.Logs.EnsureLoaded();
        await vm.SearchCommand.ExecuteAsync(null);
        Assert.Contains(vm.Logs.Roots.SelectMany(Flatten), n => n.ChannelName == "System" && n.CountText == "2");
    }

    private static IEnumerable<LogNodeVm> Flatten(LogNodeVm node) => new[] { node }.Concat(node.Children.SelectMany(Flatten));

    [Fact] public async Task Cancel_KeepsPartialRows()
    {
        var many = Enumerable.Range(1, 2_000).Select(i => E("System", i, "10:00:00", i, "P" + i)).ToArray();
        var source = Source(many);
        var (vm, _, _) = Create(source);
        source.OnEvent = n =>
        {
            if (n != 400) return;
            SpinWait.SpinUntil(() => vm.Rows.Count > 0, TimeSpan.FromSeconds(10)); // first batch has reached the screen
            vm.CancelCommand.Execute(null);
        };

        await vm.SearchCommand.ExecuteAsync(null);

        Assert.NotEmpty(vm.Rows);
        Assert.StartsWith("Cancelled", vm.Status);
        Assert.False(vm.IsSearching);
    }

    [Fact] public async Task SecondSearch_ReplacesTheFirstResults()
    {
        var (vm, _, _) = Create(Source(E("System", 1, "10:00:00")));
        await vm.SearchCommand.ExecuteAsync(null);
        vm.Text = "nothing matches this";
        await vm.SearchCommand.ExecuteAsync(null);
        Assert.Empty(vm.Rows);
        Assert.True(vm.IsEmpty);
    }

    // ---- defaults, saved views, export ----

    [Fact] public void StandardUser_DefaultLogsExcludeSecurity() =>
        Assert.Equal(["System", "Application"], Create(Source(), admin: false).Vm.Logs.Selected);

    [Fact] public void Admin_DefaultLogsIncludeSecurity() =>
        Assert.Equal(["System", "Application", "Security"], Create(Source(), admin: true).Vm.Logs.Selected);

    [Fact] public void SaveView_AddsToSettings()
    {
        var (vm, state, _) = Create(Source());
        FillMockup(vm);
        vm.SaveViewName = "Disk problems";

        vm.SaveViewCommand.Execute(null);

        Assert.Equal([new SavedView("Disk problems", MockupLine)], state.Settings.SavedViews);
        Assert.Equal("", vm.SaveViewName);
        Assert.Equal([new SavedView("Disk problems", MockupLine)], new SettingsStore(_dir).Load().SavedViews);
    }

    [Fact] public void SaveView_SameNameReplaces()
    {
        var (vm, state, _) = Create(Source());
        vm.SaveViewName = "A";
        vm.Text = "one";
        vm.SaveViewCommand.Execute(null);
        vm.SaveViewName = "A";
        vm.Text = "two";
        vm.SaveViewCommand.Execute(null);
        Assert.Equal("two last:24h level:>=warning", Assert.Single(state.Settings.SavedViews).QueryText);
    }

    [Fact] public void SaveView_BlankName_UsesTheText()
    {
        var (vm, state, _) = Create(Source());
        vm.Text = "disk 3";
        vm.SaveViewCommand.Execute(null);
        Assert.Equal("disk 3", Assert.Single(state.Settings.SavedViews).Name);
    }

    [Fact] public async Task Export_SendsGroupedRowsToTheExporter()
    {
        var (vm, _, exporter) = Create(Source(E("System", 1, "10:00:00"), E("System", 2, "10:00:30"), E("System", 4, "11:00:00", 7031, "Service Control Manager")));
        await vm.SearchCommand.ExecuteAsync(null);

        vm.ExportCommand.Execute(null);

        var call = Assert.Single(exporter.Calls);
        Assert.Equal(2, call.Rows.Count);
        Assert.Contains(call.Rows, r => r.EventId == 153 && r.Count == 2 && r.Source == "disk" && r.Log == "System");
        Assert.Contains("last:24h", call.Subtitle);
    }

    [Fact] public void Export_WithNoResults_DoesNothing()
    {
        var (vm, _, exporter) = Create(Source());
        vm.ExportCommand.Execute(null);
        Assert.Empty(exporter.Calls);
    }

    [Fact] public async Task Export_Failure_IsShown()
    {
        var (vm, _, exporter) = Create(Source(E("System", 1, "10:00:00")));
        exporter.Error = "The disk is full.";
        await vm.SearchCommand.ExecuteAsync(null);
        vm.ExportCommand.Execute(null);
        Assert.Contains("The disk is full.", vm.Errors);
    }

    // ---- coming from other screens ----

    [Fact] public async Task LoadQuery_FillsFieldsAndRunsTheSearch()
    {
        var source = Source(E("System", 1, "10:00:00"), E("System", 2, "10:05:00", 7031, "Service Control Manager"));
        var (vm, _, _) = Create(source);
        var query = new EventQuery
        {
            Logs = ["System"], IncludeIds = [153], Levels = [EventLevel.Error],
            Range = new TimeRange(new DateTime(2026, 11, 11, 0, 0, 0, DateTimeKind.Utc), Now),
        };

        await vm.LoadQueryAsync(query);

        Assert.Equal("153", vm.Ids);
        Assert.Equal(["System"], vm.Logs.Selected);
        Assert.Equal([EventLevel.Error], vm.SelectedLevels);
        Assert.Equal("2026-11-11 00:00:00", vm.FromText);
        Assert.Equal("custom", vm.PresetKey);
        Assert.NotEmpty(vm.Rows);
    }

    [Fact] public async Task LoadQuery_CollapsesTheSearchPanelSoResultsGetTheRoom()
    {
        var (vm, _, _) = Create(Source());
        Assert.True(vm.IsSearchOpen);

        await vm.LoadQueryAsync(new EventQuery { Logs = ["System"], Range = TimeRange.Last(TimeSpan.FromHours(1), Now), LastSpan = TimeSpan.FromHours(1) });

        Assert.False(vm.IsSearchOpen);
    }

    [Fact] public async Task LoadQuery_SourcesAndTextArePutInTheirFields()
    {
        var (vm, _, _) = Create(Source());
        await vm.LoadQueryAsync(new EventQuery
        {
            Text = "disk", Sources = ["disk", "ntfs"], Logs = ["System"], Range = TimeRange.Last(TimeSpan.FromHours(1), Now), LastSpan = TimeSpan.FromHours(1),
        });
        Assert.Equal("disk", vm.Text);
        Assert.Equal("disk, ntfs", vm.Sources);
        Assert.Equal("1h", vm.PresetKey);
    }

    [Fact] public async Task ClearFilters_ResetsTextIdsSourcesAndLevels()
    {
        var (vm, _, _) = Create(Source());
        FillMockup(vm);
        vm.Sources = "disk";

        vm.ClearFiltersCommand.Execute(null);

        Assert.Equal("", vm.Text);
        Assert.Equal("", vm.Ids);
        Assert.Equal("", vm.Sources);
        Assert.Empty(vm.SelectedLevels);
        await Task.CompletedTask;
    }

    [Fact] public async Task WidenRange_Uses7DaysAndSearches()
    {
        var (vm, _, _) = Create(Source(E("System", 1, "10:00:00")));
        await vm.WidenRangeCommand.ExecuteAsync(null);
        Assert.Equal("7d", vm.PresetKey);
        Assert.NotEmpty(vm.Rows);
    }

    [Fact] public void Levels_AreToggledFromTheOptions()
    {
        var (vm, _, _) = Create(Source());
        vm.LevelOptions.Single(o => o.Level == EventLevel.Information).IsSelected = true;
        Assert.Contains(EventLevel.Information, vm.SelectedLevels);
        Assert.Contains("information", vm.QueryLine);
    }

    // ---- tasks and detail ----

    [Fact] public async Task TaskOptions_ComeFromTheResults_AndFilterTheNextSearch()
    {
        var (vm, _, _) = Create(Source(E("System", 1, "10:00:00", 1, "P", task: 7), E("System", 2, "10:01:00", 2, "P", task: 8)));
        await vm.SearchCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.TaskOptions.Count);
        vm.SelectedTask = vm.TaskOptions.First(t => t.Filter.Task == 7);

        Assert.Contains("task:P/7", vm.QueryLine);
    }

    [Fact] public async Task SelectingARow_ShowsItsDetail()
    {
        var shown = new List<EventRecordLite>();
        var (vm, _, _) = Create(Source(E("System", 1, "10:00:00")), detail: e => { shown.Add(e); return new Page(); });
        await vm.SearchCommand.ExecuteAsync(null);

        vm.SelectedRow = vm.Rows[0];

        Assert.Single(shown);
        Assert.IsType<Page>(vm.Detail);
    }

    private sealed class Page : ObservableObject;
}
