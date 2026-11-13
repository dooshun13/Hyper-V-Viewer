using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HvViewer.App.Services;
using HvViewer.Core.Model;
using HvViewer.Core.Settings;
using HvViewer.Machine;
using HvViewer.Machine.Health;
using HvViewer.Rules;

namespace HvViewer.App.Home;

/// <summary>The Home dashboard: system card, severity counts, health, chart, tiles, restart story and unusual events.</summary>
public sealed partial class HomeViewModel : ObservableObject
{
    private static readonly string[] DateFormats = ["yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-dd"];

    private readonly DashboardService _dashboard;
    private readonly Func<IReadOnlyList<HealthResult>> _health;
    private readonly RuleSet _rules;
    private const int MaxListRows = 3; // the Home page fits one screen, so lists show only the first few rows

    private readonly IHardwareProvider _hardware;
    private HardwareInfo? _hardwareInfo;
    private readonly INavigator _navigator;
    private readonly AppState _state;
    private readonly Func<Action<DashboardResult>, IProgress<DashboardResult>> _progressFactory;
    private readonly DashboardResultStore? _store;

    private CancellationTokenSource? _cts;
    private int _version;
    private TimeRange _range;
    private TimeSpan? _lastSpan;
    private IReadOnlyList<HealthResult> _healthResults = [];
    private bool _started;

    /// <param name="progressFactory">Makes the progress reporter for partial results. The default posts back to the UI thread.</param>
    public HomeViewModel(
        DashboardService dashboard,
        Func<IReadOnlyList<HealthResult>> health,
        SystemInfo system,
        IHardwareProvider hardware,
        RuleSet rules,
        INavigator navigator,
        AppState state,
        Func<Action<DashboardResult>, IProgress<DashboardResult>>? progressFactory = null,
        DashboardResultStore? store = null)
    {
        _dashboard = dashboard;
        _health = health;
        _rules = rules;
        _navigator = navigator;
        _state = state;
        _progressFactory = progressFactory ?? (callback => new Progress<DashboardResult>(callback));
        _store = store;

        System = system;
        _hardware = hardware;
        SystemTitle = system.ProductName;
        SystemSubtitle = $"Build {system.Build} · " + (system.IsVirtual ? $"virtual machine ({system.VirtualPlatform})" : "physical machine");
        Domain = system.Domain ?? "None";
        Kind = KindText(system);
        Uptime = FormatUptime(system.Uptime);

        _rangeKey = state.Settings.DefaultRange is "1h" or "7d" ? state.Settings.DefaultRange : "24h";
        _range = state.RangeFor(_rangeKey);
        _lastSpan = AppState.SpanFor(_rangeKey);
        _customFrom = _state.FormatTime(_range.FromUtc);
        _customTo = _state.FormatTime(_range.ToUtc);
        UpdateTexts();

        RefreshCommand = new AsyncRelayCommand(RefreshAsync, AsyncRelayCommandOptions.AllowConcurrentExecutions);
        SetRangeCommand = new RelayCommand<string>(SetRange);
        SelectRangeCommand = new AsyncRelayCommand<string>(async key =>
        {
            SetRange(key);
            if (RangeKey != "custom") await RefreshAsync();
        }, AsyncRelayCommandOptions.AllowConcurrentExecutions);
        ApplyCustomRangeCommand = new AsyncRelayCommand(ApplyCustomRangeAsync, AsyncRelayCommandOptions.AllowConcurrentExecutions);
        OpenTileCommand = new RelayCommand<TileVm>(OpenTile);
        OpenSeverityCommand = new RelayCommand<SeverityRow>(OpenSeverity);
        OpenUnusualCommand = new RelayCommand<UnusualRow>(OpenUnusual);

        _state.Changed += () => { RaiseThemeChanged(); if (Result is not null) Apply(Result); UpdateTexts(); };
    }

    // ---- system card ----

    public SystemInfo System { get; }


    public string SystemTitle { get; }

    public string SystemSubtitle { get; }

    public string Domain { get; }

    public string Kind { get; }

    public string Uptime { get; }

    // ---- processor and memory (shown in the system card) ----

    [ObservableProperty] private bool _hasHardware;
    [ObservableProperty] private string _processorName = "";
    [ObservableProperty] private string _processorDetail = "";
    [ObservableProperty] private string _memoryTotalText = "";
    [ObservableProperty] private string _memoryUsedText = "";
    [ObservableProperty] private int _memoryPercent;
    [ObservableProperty] private string _memoryDetail = "";
    [ObservableProperty] private string _unusualMoreText = "";

    /// <summary>The processor and memory layout do not change, so they are read once; the memory in use is read every time.</summary>
    private (HardwareInfo? Info, long? Available) ReadHardware()
    {
        _hardwareInfo ??= _hardware.Read();
        return (_hardwareInfo, _hardware.AvailableRamBytes());
    }

    private void ApplyHardware(HardwareInfo? info, long? available)
    {
        HasHardware = info is not null;
        if (info is null) return;

        ProcessorName = info.CpuName;
        ProcessorDetail = HardwareText.CpuDetail(info);
        MemoryTotalText = HardwareText.Size(info.TotalRamBytes);
        MemoryDetail = HardwareText.Modules(info.Modules);
        MemoryUsedText = available is { } free ? HardwareText.Size(info.TotalRamBytes - free) + " used" : "";
        MemoryPercent = available is { } left ? HardwareText.UsedPercent(info.TotalRamBytes, left) : 0;
    }

    // ---- state ----

    [ObservableProperty] private string _rangeKey;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _errorMessage = "";
    [ObservableProperty] private DashboardResult? _result;
    [ObservableProperty] private string _headline = "Looking at your logs…";
    [ObservableProperty] private string _dateLine = "";
    [ObservableProperty] private string _rangeDescription = "last 24 hours";
    [ObservableProperty] private string _skippedText = "";
    [ObservableProperty] private string _lastBoot = "";
    [ObservableProperty] private string _lastBootKey = "TextPrimary";
    [ObservableProperty] private string _restartHeading = "";
    [ObservableProperty] private string _customFrom;
    [ObservableProperty] private string _customTo;
    [ObservableProperty] private string _customError = "";

    [ObservableProperty] private IReadOnlyList<SeverityRow> _severityRows = [];
    [ObservableProperty] private IReadOnlyList<BarVm> _bars = [];
    [ObservableProperty] private IReadOnlyList<string> _axisLabels = [];
    [ObservableProperty] private IReadOnlyList<TileVm> _tiles = [];
    [ObservableProperty] private IReadOnlyList<HealthRow> _healthRows = [];
    [ObservableProperty] private IReadOnlyList<RestartRow> _restartRows = [];
    [ObservableProperty] private IReadOnlyList<UnusualRow> _unusualRows = [];

    public IReadOnlyList<RangeOption> RangeOptions { get; } =
    [
        new RangeOption("1h", "1 h"), new RangeOption("24h", "24 h"), new RangeOption("7d", "7 d"), new RangeOption("custom", "Custom…"),
    ];

    // ---- Night / Day / Auto switch (the same setting as the one in Settings) ----

    public bool IsThemeAuto { get => _state.Settings.Theme == ThemeMode.System; set { if (value) SetTheme(ThemeMode.System); } }

    public bool IsThemeNight { get => _state.Settings.Theme == ThemeMode.Dark; set { if (value) SetTheme(ThemeMode.Dark); } }

    public bool IsThemeDay { get => _state.Settings.Theme == ThemeMode.Light; set { if (value) SetTheme(ThemeMode.Light); } }

    private void SetTheme(ThemeMode mode)
    {
        if (_state.Settings.Theme != mode) _state.Update(s => s with { Theme = mode });
    }

    private void RaiseThemeChanged()
    {
        OnPropertyChanged(nameof(IsThemeAuto));
        OnPropertyChanged(nameof(IsThemeNight));
        OnPropertyChanged(nameof(IsThemeDay));
    }

    public bool IsCustomRange => RangeKey == "custom";

    public bool HasRestart => RestartRows.Count > 0;

    partial void OnRangeKeyChanged(string value) => OnPropertyChanged(nameof(IsCustomRange));

    partial void OnRestartRowsChanged(IReadOnlyList<RestartRow> value) => OnPropertyChanged(nameof(HasRestart));

    // ---- commands ----

    public IAsyncRelayCommand RefreshCommand { get; }

    public IRelayCommand<string> SetRangeCommand { get; }

    public IAsyncRelayCommand<string> SelectRangeCommand { get; }

    public IAsyncRelayCommand ApplyCustomRangeCommand { get; }

    public IRelayCommand<TileVm> OpenTileCommand { get; }

    public IRelayCommand<SeverityRow> OpenSeverityCommand { get; }

    public IRelayCommand<UnusualRow> OpenUnusualCommand { get; }

    /// <summary>Starts the first scan the first time the screen is shown.</summary>
    public void StartIfNeeded()
    {
        if (_started) return;
        _started = true;
        _ = RefreshAsync();
    }

    // ---- range ----

    private void SetRange(string? key)
    {
        if (key is not ("1h" or "24h" or "7d" or "custom")) return;
        RangeKey = key;
        if (key == "custom") return;
        _range = _state.RangeFor(key);
        _lastSpan = AppState.SpanFor(key);
        UpdateTexts();
    }

    private async Task ApplyCustomRangeAsync()
    {
        if (!TryParse(CustomFrom, out var from) || !TryParse(CustomTo, out var to))
        {
            CustomError = "Use dates like 2026-11-10 22:00:00.";
            return;
        }
        if (from >= to)
        {
            CustomError = "From must be before To.";
            return;
        }

        CustomError = "";
        _range = new TimeRange(from, to);
        _lastSpan = null;
        RangeKey = "custom";
        UpdateTexts();
        await RefreshAsync();
    }

    private bool TryParse(string text, out DateTime utc)
    {
        utc = default;
        if (!DateTime.TryParseExact(text.Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local)) return false;
        utc = ZoneTime.LocalToUtc(local, _state.Zone);
        return true;
    }

    private void UpdateTexts()
    {
        RangeDescription = RangeKey switch
        {
            "1h" => "last hour",
            "7d" => "last 7 days",
            "custom" => "selected period",
            _ => "last 24 hours",
        };
        var local = TimeZoneInfo.ConvertTimeFromUtc(_state.UtcNow, _state.Zone);
        DateLine = $"{local.ToString("dddd d MMMM", CultureInfo.InvariantCulture)} · {RangeDescription}";
    }

    // ---- refresh ----

    private async Task RefreshAsync()
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        var version = ++_version;
        var range = _range;

        IsLoading = true;
        ErrorMessage = "";
        try
        {
            var healthTask = Task.Run(() => _health(), cts.Token);
            var hardwareTask = Task.Run(ReadHardware, cts.Token);
            var progress = _progressFactory(partial => { if (version == _version) Apply(partial); });
            var result = await _dashboard.RunAsync(range, progress, cts.Token);
            var health = await healthTask;
            var (hardwareInfo, availableRam) = await hardwareTask;
            if (version != _version) return;

            _healthResults = health;
            Result = result;
            Apply(result);
            HealthRows = health.Select(ToRow).ToList();
            ApplyHardware(hardwareInfo, availableRam);
            UpdateHeadline();
        }
        catch (OperationCanceledException)
        {
            // a newer refresh replaced this one
        }
        catch (Exception ex)
        {
            if (version == _version) ErrorMessage = "Could not read the logs: " + ex.Message;
        }
        finally
        {
            if (version == _version) IsLoading = false;
        }
    }

    private void Apply(DashboardResult r)
    {
        Result = r;
        _store?.Publish(r, _range, _lastSpan);
        SeverityRows = BuildSeverity(r);
        (Bars, AxisLabels) = BuildChart(r.Buckets);
        Tiles = BuildTiles(r);
        (RestartRows, RestartHeading) = BuildRestart(r.Restart);
        UnusualRows = r.NewAndUnusual.Take(MaxListRows).Select(ToRow).ToList();
        UnusualMoreText = r.NewAndUnusual.Count > MaxListRows ? $"+{r.NewAndUnusual.Count - MaxListRows} more in Events" : "";
        SkippedText = r.Skipped.Count == 0
            ? ""
            : $"{r.Skipped.Count} {(r.Skipped.Count == 1 ? "log" : "logs")} skipped: " + string.Join("; ", r.Skipped.Select(s => $"{s.Log} ({s.Reason})"));
        (LastBoot, LastBootKey) = r.Restart is { } story
            ? ($"Unexpected · {TimeOnly(story.RestartEvent.TimeCreatedUtc)}", "Critical")
            : ($"Normal · {TimeOnly(System.BootTimeUtc)}", "TextPrimary");
        UpdateHeadline();
    }

    private void UpdateHeadline()
    {
        if (Result is null) return;

        var needsLook = Result.Tiles.Count(t => t.UnavailableReason is null && t.Count > 0 && t.Highest >= RuleSeverity.Error)
                        + _healthResults.Count(h => h.Status is HealthStatus.Warning or HealthStatus.Problem);
        var phrase = needsLook == 1 ? "1 thing needs a look" : $"{needsLook} things need a look";

        Headline = Result.Restart is not null
            ? needsLook == 0 ? "Restarted unexpectedly — nothing else needs a look" : "Restarted unexpectedly — " + phrase
            : needsLook > 0 ? phrase : "All clear in the " + RangeDescription.Replace("last ", "last ");
    }

    // ---- builders ----

    private static IReadOnlyList<SeverityRow> BuildSeverity(DashboardResult r) =>
    [
        new SeverityRow("Critical", r.Current.Critical, Delta(r.Current.Critical, r.Previous.Critical), "Critical", EventLevel.Critical),
        new SeverityRow("Error", r.Current.Error, Delta(r.Current.Error, r.Previous.Error), "Error", EventLevel.Error),
        new SeverityRow("Warning", r.Current.Warning, Delta(r.Current.Warning, r.Previous.Warning), "Warning", EventLevel.Warning),
        new SeverityRow("Information", r.Current.Information, "", "Info", EventLevel.Information),
    ];

    private static string Delta(int current, int previous)
    {
        var diff = current - previous;
        return diff > 0 ? $"+{diff}" : diff < 0 ? $"−{-diff}" : "same";
    }

    private (IReadOnlyList<BarVm>, IReadOnlyList<string>) BuildChart(IReadOnlyList<Bucket> buckets)
    {
        const double MaxHeight = 150, MinVisible = 3;
        var peak = buckets.Count == 0 ? 0 : buckets.Max(b => b.Errors + b.Warnings);
        var scale = peak == 0 ? 0 : MaxHeight / peak;

        double Height(int count) => count == 0 ? 0 : Math.Max(MinVisible, count * scale);

        var bars = buckets.Select(b => new BarVm(Height(b.Errors), Height(b.Warnings),
            $"{_state.FormatTime(b.StartUtc)}: {b.Errors} errors, {b.Warnings} warnings")).ToList();

        var daily = buckets.Count > 1 && buckets[1].StartUtc - buckets[0].StartUtc >= TimeSpan.FromDays(1);
        var count = Math.Min(5, buckets.Count);
        var labels = Enumerable.Range(0, count)
            .Select(i => buckets[count == 1 ? 0 : i * (buckets.Count - 1) / (count - 1)].StartUtc)
            .Select(t => TimeZoneInfo.ConvertTimeFromUtc(t, _state.Zone).ToString(daily ? "MM-dd" : "HH:mm", CultureInfo.InvariantCulture))
            .ToList();
        return (bars, labels);
    }

    private IReadOnlyList<TileVm> BuildTiles(DashboardResult r)
    {
        var tiles = r.Tiles.Select(t =>
        {
            var logs = _rules.Troubleshooters.FirstOrDefault(x => x.Id == t.TroubleshooterId)?.Logs ?? [];
            var unavailable = t.UnavailableReason is not null;
            return (Tile: t, Vm: new TileVm(
                t.TroubleshooterId, t.Title,
                unavailable ? "—" : t.Count.ToString("N0", CultureInfo.InvariantCulture),
                unavailable ? "TextMuted" : t.Count == 0 ? "Ok" : SeverityKey(t.Highest),
                t.UnavailableReason ?? string.Join(" · ", logs),
                unavailable));
        });

        // Tiles that need attention first, worst first; then the quiet ones, then the unavailable ones.
        return tiles
            .OrderBy(x => x.Vm.IsUnavailable ? 2 : x.Tile.Count > 0 ? 0 : 1)
            .ThenByDescending(x => x.Tile.Highest)
            .ThenByDescending(x => x.Tile.Count)
            .Select(x => x.Vm)
            .ToList();
    }

    private (IReadOnlyList<RestartRow>, string) BuildRestart(RestartStory? story)
    {
        if (story is null) return ([], "");
        var rows = story.Before.Take(MaxListRows).Select(e => new RestartRow(
            TimeOnly(e.TimeCreatedUtc), LevelKey(e.Level), Describe(e), $"{e.LogName} · {e.Source} · {e.EventId}")).ToList();
        return (rows, $"last 15 min before {TimeOnly(story.AnchorUtc)}, all scanned logs");
    }

    private UnusualRow ToRow(UnusualItem item) => new(
        item.Latest.EventId, item.Latest.Source, item.Latest.LogName,
        item.Rule?.Title ?? $"{item.Latest.Source} event {item.Latest.EventId}",
        item.Count > 1 ? $"{item.Latest.Source} · {item.Count} times" : item.Latest.Source,
        When(item.Latest.TimeCreatedUtc), LevelKey(item.Latest.Level), item.Count);

    private static HealthRow ToRow(HealthResult h) => h.Status switch
    {
        HealthStatus.Ok => new HealthRow(h.Label, h.Value, "✓", "Ok", "TextPrimary", h.Detail),
        HealthStatus.Warning => new HealthRow(h.Label, h.Value, "!", "Warning", "Warning", h.Detail),
        HealthStatus.Problem => new HealthRow(h.Label, h.Value, "!", "Error", "Error", h.Detail),
        _ => new HealthRow(h.Label, h.Value, "?", "TextMuted", "TextMuted", h.Detail),
    };

    private string Describe(EventRecordLite e) => _rules.Match(e)?.Rule.Title ?? $"{e.Source} event {e.EventId}";

    private string TimeOnly(DateTime utc) => _state.FormatTime(utc)[11..];

    private string When(DateTime utc)
    {
        var text = _state.FormatTime(utc);
        var today = _state.FormatTime(_state.UtcNow)[..10];
        return text.StartsWith(today, StringComparison.Ordinal) ? text[11..16] : text[5..16];
    }

    private static string SeverityKey(RuleSeverity s) => s switch
    {
        RuleSeverity.Critical => "Critical",
        RuleSeverity.Error => "Error",
        RuleSeverity.Warning => "Warning",
        _ => "Info",
    };

    private static string LevelKey(EventLevel l) => l switch
    {
        EventLevel.Critical => "Critical",
        EventLevel.Error => "Error",
        EventLevel.Warning => "Warning",
        _ => "Info",
    };

    private static string FormatUptime(TimeSpan span) =>
        span.TotalDays >= 1 ? $"{(int)span.TotalDays} d {span.Hours} h"
        : span.TotalHours >= 1 ? $"{(int)span.TotalHours} h {span.Minutes} min"
        : $"{span.Minutes} min";

    private static string KindText(SystemInfo s) =>
        (s.Kind switch { MachineKind.DomainController => "Domain controller", MachineKind.Server => "Server", _ => "Workstation" })
        + (s.IsServerCore ? " (Server Core)" : "");

    // ---- navigation ----

    private void OpenTile(TileVm? tile)
    {
        var troubleshooter = _rules.Troubleshooters.FirstOrDefault(t => t.Id == tile?.TroubleshooterId);
        if (troubleshooter is not null) _navigator.ShowEvents(TroubleshooterQueries.For(troubleshooter, _range, _lastSpan));
    }

    private void OpenSeverity(SeverityRow? row)
    {
        if (row is null) return;
        _navigator.ShowEvents(_state.NewQuery() with { Levels = [row.Level], Range = _range, LastSpan = _lastSpan });
    }

    private void OpenUnusual(UnusualRow? row)
    {
        if (row is null) return;
        _navigator.ShowEvents(_state.NewQuery() with
        {
            Logs = [row.Log], Sources = [row.Provider], IncludeIds = [row.EventId], Range = _range, LastSpan = _lastSpan,
        });
    }
}
