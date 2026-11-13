using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HvViewer.App.Services;
using HvViewer.Core.Export;
using HvViewer.Core.Model;
using HvViewer.Core.Query;
using HvViewer.Core.Reading;
using HvViewer.Core.Search;
using HvViewer.Core.Settings;
using HvViewer.Rules;

namespace HvViewer.App.Events;

/// <summary>The Events screen: search fields, the one-line query, streaming grouped results, saved views and export.</summary>
public sealed partial class EventsViewModel : ObservableObject, IQueryTarget
{
    private const int HistogramBuckets = 36;
    private const int RebuildEveryHits = 5_000;
    private const int MaxTaskOptions = 40;
    private static readonly string[] DateFormats = ["yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-dd"];

    private readonly SearchService _search;
    private readonly IEventSource _source;
    private readonly RuleSet _rules;
    private readonly AppState _state;
    private readonly DateTime _bootUtc;
    private readonly IExportService _exporter;
    private readonly Func<EventRecordLite, ObservableObject?>? _detailFactory;

    private readonly List<SearchHit> _hits = [];
    private Dictionary<EventRecordLite, SearchHit> _hitByEvent = new(ReferenceEqualityComparer.Instance);
    private IReadOnlyList<SkippedLog> _skipped = [];
    private IReadOnlyList<string> _fieldErrors = [];
    private readonly List<string> _transientErrors = [];
    private CancellationTokenSource? _cts;
    private int _version;
    private int _scanned;
    private int _updatesSinceRebuild;
    private TimeRange _searchedRange;
    private bool _applying;
    private bool _started;

    public EventsViewModel(
        SearchService search,
        IEventSource source,
        RuleSet rules,
        AppState state,
        DateTime bootTimeUtc,
        IExportService exporter,
        Func<EventRecordLite, ObservableObject?>? detailFactory = null)
    {
        _search = search;
        _source = source;
        _rules = rules;
        _state = state;
        _bootUtc = bootTimeUtc;
        _exporter = exporter;
        _detailFactory = detailFactory;

        Logs = new LogPickerViewModel(() => source.GetChannels(), rules, IsAdmin(state), state.DefaultLogs);
        Logs.SelectionChanged += Refresh;

        LevelOptions =
        [
            new LevelOption("Critical", EventLevel.Critical, Refresh),
            new LevelOption("Error", EventLevel.Error, Refresh),
            new LevelOption("Warning", EventLevel.Warning, Refresh),
            new LevelOption("Information", EventLevel.Information, Refresh),
            new LevelOption("Verbose", EventLevel.Verbose, Refresh),
        ];
        _applying = true;
        foreach (var option in LevelOptions.Where(o => o.Level <= EventLevel.Warning)) option.IsSelected = true;
        _presetKey = "24h";
        _applying = false;
        SetRangeTexts(RangeFor("24h"));

        SearchCommand = new AsyncRelayCommand(SearchAsync, AsyncRelayCommandOptions.AllowConcurrentExecutions);
        CancelCommand = new RelayCommand(() => _cts?.Cancel());
        ApplyQueryLineCommand = new RelayCommand(ApplyQueryLine);
        SelectPresetCommand = new RelayCommand<string>(SelectPreset);
        SaveViewCommand = new RelayCommand(SaveView);
        ExportCommand = new RelayCommand(Export);
        ClearFiltersCommand = new RelayCommand(ClearFilters);
        ClearTaskCommand = new RelayCommand(() => SelectedTask = null);
        CloseDetailCommand = new RelayCommand(() => SelectedRow = null);
        WidenRangeCommand = new AsyncRelayCommand(async () =>
        {
            SelectPreset("7d");
            await SearchAsync();
        }, AsyncRelayCommandOptions.AllowConcurrentExecutions);

        _state.Changed += OnSettingsChanged;
        Refresh();
    }

    private static bool IsAdmin(AppState state) => state.DefaultLogs.Contains("Security");

    // ---- search fields ----

    [ObservableProperty] private string _text = "";
    [ObservableProperty] private bool _matchCase;
    [ObservableProperty] private bool _wholeWord;
    [ObservableProperty] private bool _useRegex;
    [ObservableProperty] private bool _ignoreAccents;
    [ObservableProperty] private TextTarget _target = TextTarget.Message;
    [ObservableProperty] private string _presetKey;
    [ObservableProperty] private string _fromText = "";
    [ObservableProperty] private string _toText = "";
    [ObservableProperty] private string _ids = "";
    [ObservableProperty] private string _sources = "";
    [ObservableProperty] private TaskOption? _selectedTask;
    [ObservableProperty] private string _saveViewName = "";

    /// <summary>Whether the search panel is open. It collapses when another screen sends a query here.</summary>
    [ObservableProperty] private bool _isSearchOpen = true;

    partial void OnTextChanged(string value) => Edited();
    partial void OnMatchCaseChanged(bool value) => Edited();
    partial void OnWholeWordChanged(bool value) => Edited();
    partial void OnUseRegexChanged(bool value) => Edited();
    partial void OnIgnoreAccentsChanged(bool value) => Edited();
    partial void OnTargetChanged(TextTarget value)
    {
        OnPropertyChanged(nameof(IsTargetMessage));
        OnPropertyChanged(nameof(IsTargetAllFields));
        OnPropertyChanged(nameof(IsTargetXml));
        Edited();
    }
    partial void OnIdsChanged(string value) => Edited();
    partial void OnSourcesChanged(string value) => Edited();
    partial void OnSelectedTaskChanged(TaskOption? value) => Edited();

    partial void OnFromTextChanged(string value) => DateEdited();
    partial void OnToTextChanged(string value) => DateEdited();

    private void Edited()
    {
        if (!_applying) Refresh();
    }

    private void DateEdited()
    {
        if (_applying) return;
        PresetKey = "custom"; // typing a date means "this exact range"
        Refresh();
    }

    public TextOptions Options => new(MatchCase, WholeWord, UseRegex, IgnoreAccents);

    public bool IsTargetMessage { get => Target == TextTarget.Message; set { if (value) Target = TextTarget.Message; } }

    public bool IsTargetAllFields { get => Target == TextTarget.AllFields; set { if (value) Target = TextTarget.AllFields; } }

    public bool IsTargetXml { get => Target == TextTarget.Xml; set { if (value) Target = TextTarget.Xml; } }

    public LogPickerViewModel Logs { get; }

    public IReadOnlyList<LevelOption> LevelOptions { get; }

    public IReadOnlyList<EventLevel> SelectedLevels =>
        LevelOptions.Where(o => o.IsSelected).Select(o => o.Level).ToList();

    public IReadOnlyList<PresetOption> Presets { get; } =
    [
        new("15m", "Last 15 min"), new("1h", "Last hour"), new("24h", "Last 24 h"), new("7d", "Last 7 days"), new("today", "Today"),
        new("yesterday", "Yesterday"), new("boot", "Since last boot"), new("custom", "Custom"),
    ];

    // ---- query line ----

    [ObservableProperty] private string _queryLine = "";
    [ObservableProperty] private string _queryLineError = "";
    [ObservableProperty] private IReadOnlyList<string> _errors = [];

    // ---- results ----

    [ObservableProperty] private IReadOnlyList<ResultRow> _rows = [];
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _isSearching;
    [ObservableProperty] private bool _isEmpty;
    [ObservableProperty] private string _skippedText = "";
    [ObservableProperty] private string _skippedDetail = "";
    [ObservableProperty] private IReadOnlyList<HistogramBar> _histogram = [];
    [ObservableProperty] private IReadOnlyList<TaskOption> _taskOptions = [];
    [ObservableProperty] private ResultRow? _selectedRow;
    [ObservableProperty] private ObservableObject? _detail;

    partial void OnSelectedRowChanged(ResultRow? value) =>
        Detail = value is null ? null : _detailFactory?.Invoke(value.Group.Latest);

    // ---- commands ----

    public IAsyncRelayCommand SearchCommand { get; }

    public IRelayCommand CancelCommand { get; }

    public IRelayCommand ApplyQueryLineCommand { get; }

    public IRelayCommand<string> SelectPresetCommand { get; }

    public IRelayCommand SaveViewCommand { get; }

    public IRelayCommand ExportCommand { get; }

    public IRelayCommand ClearFiltersCommand { get; }

    public IRelayCommand ClearTaskCommand { get; }

    public IRelayCommand CloseDetailCommand { get; }

    public IAsyncRelayCommand WidenRangeCommand { get; }

    // ---- building the query from the fields ----

    private EventQuery? TryBuildQuery(out List<string> errors)
    {
        errors = [];

        var (include, exclude) = ParseIds(Ids, errors);
        var range = TryRange(errors, out var lastSpan);
        if (range is null) return null;

        var query = new EventQuery
        {
            Text = string.IsNullOrWhiteSpace(Text) ? null : Text,
            TextOptions = Options,
            Target = Target,
            Range = range.Value,
            LastSpan = lastSpan,
            IncludeIds = include,
            ExcludeIds = exclude,
            Sources = Sources.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
            Logs = Logs.Selected,
            Levels = SelectedLevels,
            Tasks = SelectedTask is null ? [] : [SelectedTask.Filter],
        };

        if (errors.Count == 0) errors.AddRange(query.Validate());
        return query;
    }

    private static (List<int> Include, List<int> Exclude) ParseIds(string text, List<string> errors)
    {
        List<int> include = [], exclude = [];
        foreach (var raw in text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var negative = raw.StartsWith('-');
            if (!int.TryParse(negative ? raw[1..] : raw, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            {
                errors.Add($"'{raw}' is not a valid event ID. Use numbers separated by commas; a leading - leaves an ID out.");
                continue;
            }
            (negative ? exclude : include).Add(id);
        }
        return (include, exclude);
    }

    private TimeRange? TryRange(List<string> errors, out TimeSpan? lastSpan)
    {
        lastSpan = null;
        if (PresetKey != "custom")
        {
            if (PresetKey is "15m" or "1h" or "24h" or "7d") lastSpan = SpanFor(PresetKey);
            return RangeFor(PresetKey);
        }

        var from = ParseLocal(FromText);
        var to = ParseLocal(ToText);
        if (from is null) errors.Add("From is not a valid date and time. Use 2026-11-10 22:00:00.");
        if (to is null) errors.Add("To is not a valid date and time. Use 2026-11-10 22:00:00.");
        return from is null || to is null ? null : new TimeRange(from.Value, to.Value);
    }

    /// <summary>The current moment in the time zone chosen in Settings (for the date picker's Today button).</summary>
    public DateTime NowInZone() => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(_state.UtcNow, DateTimeKind.Utc), _state.Zone);

    private DateTime? ParseLocal(string text) =>
        DateTime.TryParseExact(text.Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local)
            ? ZoneTime.LocalToUtc(local, _state.Zone)
            : null;

    private static TimeSpan SpanFor(string key) => key switch
    {
        "15m" => TimeSpan.FromMinutes(15),
        "1h" => TimeSpan.FromHours(1),
        "7d" => TimeSpan.FromDays(7),
        _ => TimeSpan.FromHours(24),
    };

    private TimeRange RangeFor(string key)
    {
        var now = _state.UtcNow;
        return key switch
        {
            "today" => TimeRange.Today(_state.Zone, now),
            "yesterday" => TimeRange.Yesterday(_state.Zone, now),
            "boot" => TimeRange.SinceBoot(_bootUtc, now),
            _ => TimeRange.Last(SpanFor(key), now),
        };
    }

    private void SetRangeTexts(TimeRange range)
    {
        var was = _applying;
        _applying = true;
        FromText = _state.FormatTime(range.FromUtc);
        ToText = _state.FormatTime(range.ToUtc);
        _applying = was;
    }

    private void SelectPreset(string? key)
    {
        if (key is null || Presets.All(p => p.Key != key)) return;
        if (key == "custom")
        {
            PresetKey = "custom";
            Refresh();
            return;
        }
        _applying = true;
        PresetKey = key;
        _applying = false;
        SetRangeTexts(RangeFor(key));
        Refresh();
    }

    // ---- query line and fields stay in step ----

    /// <summary>Rebuilds the one-line query and the error list from the fields.</summary>
    private void Refresh()
    {
        if (_applying) return;
        _transientErrors.Clear();

        var query = TryBuildQuery(out var errors);
        _fieldErrors = errors;
        Errors = errors;
        if (query is not null) QueryLine = QueryText.Format(query, _state.Zone, _state.DefaultLogs);
        QueryLineError = "";
    }

    private void ApplyQueryLine()
    {
        var defaults = TryBuildQuery(out _) ?? _state.NewQuery();
        try
        {
            ApplyQuery(QueryText.Parse(QueryLine, defaults, _state.Zone, _state.UtcNow));
        }
        catch (QueryTextException ex)
        {
            QueryLineError = ex.Message;
        }
    }

    private void ApplyQuery(EventQuery q)
    {
        _applying = true;
        Text = q.Text ?? "";
        MatchCase = q.TextOptions.MatchCase;
        WholeWord = q.TextOptions.WholeWord;
        UseRegex = q.TextOptions.Regex;
        IgnoreAccents = q.TextOptions.IgnoreAccents;
        Target = q.Target;
        Ids = string.Join(", ", q.IncludeIds.Select(i => i.ToString(CultureInfo.InvariantCulture))
            .Concat(q.ExcludeIds.Select(i => "-" + i.ToString(CultureInfo.InvariantCulture))));
        Sources = string.Join(", ", q.Sources);
        SelectedTask = q.Tasks.Count == 0 ? null : new TaskOption(TaskLabel(q.Tasks[0].Provider, q.Tasks[0].Task), q.Tasks[0]);
        foreach (var option in LevelOptions)
            option.IsSelected = q.Levels.Contains(option.Level) || (option.Level == EventLevel.Information && q.Levels.Contains(EventLevel.LogAlways));
        if (q.Logs.Count > 0) Logs.SetSelected(q.Logs);

        var preset = q.LastSpan is { } span ? PresetFor(span) : null;
        PresetKey = preset ?? "custom";
        _applying = false;
        SetRangeTexts(preset is null ? q.Range : RangeFor(preset));
        Refresh();
    }

    private static string? PresetFor(TimeSpan span) =>
        span == TimeSpan.FromMinutes(15) ? "15m"
        : span == TimeSpan.FromHours(1) ? "1h"
        : span == TimeSpan.FromHours(24) ? "24h"
        : span == TimeSpan.FromDays(7) ? "7d"
        : null;

    private void ClearFilters()
    {
        _applying = true;
        Text = "";
        Ids = "";
        Sources = "";
        SelectedTask = null;
        foreach (var option in LevelOptions) option.IsSelected = false;
        _applying = false;
        Refresh();
    }

    private void OnSettingsChanged()
    {
        // Time zone or default range changed: show the same moments in the new zone.
        var query = TryBuildQuery(out _);
        if (query is null) return;
        SetRangeTexts(query.Range);
        Refresh();
    }

    // ---- coming from other screens ----

    void IQueryTarget.LoadQuery(EventQuery query) => _ = LoadQueryAsync(query);

    public async Task LoadQueryAsync(EventQuery query)
    {
        ApplyQuery(query);
        IsSearchOpen = false; // the user came here for results

        await SearchAsync();
    }

    // ---- searching ----

    /// <summary>Runs the default search the first time the screen is shown (unless another screen already loaded a query).</summary>
    public void StartIfNeeded()
    {
        if (_started) return;
        _ = SearchAsync();
    }

    private async Task SearchAsync()
    {
        _started = true;
        var query = TryBuildQuery(out var errors);
        _fieldErrors = errors;
        Errors = errors;
        if (query is null || errors.Count > 0) return;

        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        var version = ++_version;

        _hits.Clear();
        _hitByEvent = new Dictionary<EventRecordLite, SearchHit>(ReferenceEqualityComparer.Instance);
        _skipped = [];
        _scanned = 0;
        _updatesSinceRebuild = 0;
        _searchedRange = query.Range;
        _transientErrors.Clear();
        SelectedRow = null;
        Rows = [];
        Histogram = [];
        SkippedText = "";
        SkippedDetail = "";
        IsEmpty = false;
        IsSearching = true;
        Status = "Searching…";

        try
        {
            await foreach (var update in _search.RunAsync(query, cts.Token))
            {
                if (version != _version) return;
                Absorb(update);
            }

            if (version != _version) return;
            Rebuild();
            CompleteSearch(cancelled: false);
        }
        catch (OperationCanceledException)
        {
            if (version != _version) return;
            Rebuild();
            CompleteSearch(cancelled: true);
        }
        catch (Exception ex) when (ex is ArgumentException or EventSourceException)
        {
            if (version != _version) return;
            Errors = [ex.Message];
            IsSearching = false;
        }
    }

    private void Absorb(SearchUpdate update)
    {
        _hits.AddRange(update.NewHits);
        _scanned = update.Scanned;
        _skipped = update.Skipped;
        _updatesSinceRebuild++;

        if (_hits.Count <= RebuildEveryHits || _updatesSinceRebuild >= 10 || update.Completed)
        {
            Rebuild();
            _updatesSinceRebuild = 0;
        }
        Status = Summary("Searching… ");
    }

    private void Rebuild()
    {
        var ordered = _hits.OrderByDescending(h => h.Event.TimeCreatedUtc).ToList();
        _hitByEvent = new Dictionary<EventRecordLite, SearchHit>(ReferenceEqualityComparer.Instance);
        foreach (var hit in ordered) _hitByEvent[hit.Event] = hit;

        var groups = Grouping.Group(ordered.Select(h => h.Event).ToList());
        Rows = groups.Select(ToRow).ToList();
        Histogram = BuildHistogram();
        SkippedText = SkippedSummary(_skipped);
        SkippedDetail = string.Join("\n", _skipped.Select(x => $"{x.Log}: {x.Reason}"));
    }

    /// <summary>A few skipped logs are named; a long list (analytic logs that cannot be read) is counted by reason so it stays one line.</summary>
    private static string SkippedSummary(IReadOnlyList<SkippedLog> skipped)
    {
        if (skipped.Count == 0) return "";
        var noun = skipped.Count == 1 ? "log" : "logs";
        if (skipped.Count <= 3) return $"{skipped.Count} {noun} skipped: " + string.Join("; ", skipped.Select(s => $"{s.Log} ({s.Reason})"));
        var reasons = skipped.GroupBy(s => s.Reason).OrderByDescending(g => g.Count()).Take(3).Select(g => $"{g.Count()} × {g.Key}");
        return $"{skipped.Count} {noun} skipped ({string.Join("; ", reasons)}). Hover for the list.";
    }

    private void CompleteSearch(bool cancelled)
    {
        IsSearching = false;
        IsEmpty = !cancelled && Rows.Count == 0;
        Status = Summary(cancelled ? "Cancelled · " : "");

        Logs.SetCounts(_hits.GroupBy(h => h.Event.LogName, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase));
        TaskOptions = BuildTaskOptions();
    }

    private string Summary(string prefix)
    {
        var events = Plural(_scanned, "event", "events");
        var matches = Plural(_hits.Count, "match", "matches");
        var groups = Plural(Rows.Count, "group", "groups");
        return prefix.StartsWith("Searching", StringComparison.Ordinal)
            ? $"Searching… {events} · {matches}"
            : $"{prefix}{(prefix.Length == 0 ? "Searched " : "Searched ")}{events} · {matches} · {groups}";
    }

    private static string Plural(int n, string one, string many) =>
        n == 1 ? $"1 {one}" : $"{n.ToString("N0", CultureInfo.InvariantCulture)} {many}";

    private ResultRow ToRow(EventGroup g)
    {
        var e = g.Latest;
        _hitByEvent.TryGetValue(e, out var hit);
        var time = _state.FormatTime(e.TimeCreatedUtc);
        return new ResultRow(
            g, time[11..], time[..10], e.Level.Label(), LevelKey(e.Level),
            Title(e), e.Source, e.EventId,
            g.Count > 1 ? "×" + g.Count.ToString(CultureInfo.InvariantCulture) : "",
            Flatten(hit?.Text), hit?.Matches ?? []);
    }

    private string Title(EventRecordLite e) => _rules.Match(e)?.Rule.Title ?? $"{e.Source} event {e.EventId}";

    /// <summary>One line for the list. Every character is replaced by one character, so highlight positions stay valid.</summary>
    private static string? Flatten(string? text) =>
        text is null ? null : text.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');

    private static string LevelKey(EventLevel l) => l switch
    {
        EventLevel.Critical => "Critical",
        EventLevel.Error => "Error",
        EventLevel.Warning => "Warning",
        _ => "Info",
    };

    private IReadOnlyList<HistogramBar> BuildHistogram()
    {
        var counts = new int[HistogramBuckets];
        var length = _searchedRange.Length;
        if (length > TimeSpan.Zero)
        {
            foreach (var hit in _hits)
            {
                var position = (hit.Event.TimeCreatedUtc - _searchedRange.FromUtc).Ticks / (double)length.Ticks;
                counts[Math.Clamp((int)(position * HistogramBuckets), 0, HistogramBuckets - 1)]++;
            }
        }

        var peak = counts.Max();
        var step = length / HistogramBuckets;
        return counts.Select((c, i) => new HistogramBar(
            c == 0 ? 0 : Math.Max(3, c * 40.0 / peak),
            $"{_state.FormatTime(_searchedRange.FromUtc + step * i)}: {Plural(c, "event", "events")}")).ToList();
    }

    private IReadOnlyList<TaskOption> BuildTaskOptions() =>
        _hits.Select(h => h.Event).Where(e => e.Task != 0)
            .Select(e => (e.ProviderName, e.Task)).Distinct().Take(MaxTaskOptions)
            .Select(t => new TaskOption(TaskLabel(t.ProviderName, t.Task), new TaskFilter(t.ProviderName, t.Task)))
            .OrderBy(o => o.Label, StringComparer.OrdinalIgnoreCase).ToList();

    private string TaskLabel(string provider, int task) => (_source.GetTaskName(provider, task) ?? $"Task {task}") + " · " + provider;

    // ---- saved views and export ----

    private void SaveView()
    {
        var name = SaveViewName.Trim();
        if (name.Length == 0) name = string.IsNullOrWhiteSpace(Text) ? "Saved search" : Text.Trim();
        if (name.Length > 40) name = name[..40];

        var view = new SavedView(name, QueryLine);
        _state.Update(s => s with
        {
            SavedViews = s.SavedViews.Where(v => !string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase)).Append(view).ToList(),
        });
        SaveViewName = "";
    }

    private void Export()
    {
        if (Rows.Count == 0) return;

        var rows = Rows.Select(r =>
        {
            var e = r.Group.Latest;
            _hitByEvent.TryGetValue(e, out var hit);
            return new ExportRow(_state.FormatTime(e.TimeCreatedUtc), e.Level.Label(), e.EventId, e.Source, e.LogName, e.Computer,
                hit?.Text ?? r.Title, r.Group.Count);
        }).ToList();

        var error = _exporter.Export("Hyper-V Viewer events", $"{QueryLine} · {Plural(_hits.Count, "match", "matches")}", rows);
        if (error is not null)
        {
            _transientErrors.Add(error);
            Errors = _fieldErrors.Concat(_transientErrors).ToList();
        }
    }
}
