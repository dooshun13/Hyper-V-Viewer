using System.Globalization;
using System.Xml.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HvViewer.App.Services;
using HvViewer.Core.Model;
using HvViewer.Core.Reading;
using HvViewer.Core.Search;
using HvViewer.Core.Settings;
using HvViewer.Rules;

namespace HvViewer.App.Detail;

/// <summary>An event near the selected one, with how many seconds apart.</summary>
public sealed record AroundRow(EventRecordLite Event, string TimeText, string OffsetText, string Title, string LevelKey);

/// <summary>The panel for one event: explanation, message, XML, raw data, neighbours in time, and copy for a ticket.</summary>
public sealed partial class DetailViewModel : ObservableObject
{
    private const string MessageMissing = "Message not available on this PC";
    private static readonly string[] Tabs = ["explained", "message", "xml", "raw"];
    private static readonly TimeSpan AroundWindow = TimeSpan.FromSeconds(60);
    private static readonly EventLevel[] AroundLevels = [EventLevel.Critical, EventLevel.Error, EventLevel.Warning, EventLevel.Information];

    private readonly IEventSource _source;
    private readonly MessageCache _cache;
    private readonly RuleSet _rules;
    private readonly SearchService _search;
    private readonly AppState _state;
    private readonly Func<string, bool> _openUrl;
    private readonly Action<string> _setClipboard;
    private readonly RuleEvent? _rule;
    private CancellationTokenSource? _aroundCts;

    public DetailViewModel(
        EventRecordLite e, IEventSource source, MessageCache cache, RuleSet rules, SearchService search, AppState state,
        Func<string, bool>? openUrl = null, Action<string>? setClipboard = null)
    {
        Event = e;
        _source = source;
        _cache = cache;
        _rules = rules;
        _search = search;
        _state = state;
        _openUrl = openUrl ?? UrlLauncher.TryOpen;
        _setClipboard = setClipboard ?? SetClipboard;
        _rule = rules.Match(e)?.Rule;

        SelectTabCommand = new RelayCommand<string>(SelectTab);
        CopyForTicketCommand = new RelayCommand(CopyForTicket);
        OpenDocsCommand = new RelayCommand(() => { if (_rule is not null) _openUrl(_rule.Docs); }, () => _rule is not null);
        LoadAroundCommand = new AsyncRelayCommand(LoadAroundAsync, AsyncRelayCommandOptions.AllowConcurrentExecutions);
        CancelAroundCommand = new RelayCommand(() => _aroundCts?.Cancel());
    }

    public EventRecordLite Event { get; }

    // ---- header ----

    public string Title => _rule?.Title ?? $"{Event.Source} event {Event.EventId}";

    public string Subtitle => $"{Event.Level.Label()} · {Event.Source} · Event {Event.EventId}";

    public string LevelKey => Event.Level switch
    {
        EventLevel.Critical => "Critical",
        EventLevel.Error => "Error",
        EventLevel.Warning => "Warning",
        _ => "Info",
    };

    public string TimeText => $"{_state.FormatTime(Event.TimeCreatedUtc)} ({(_state.Settings.Time == TimeMode.Utc ? "UTC" : "Local")})";

    public string LogName => Event.LogName;

    public string Computer => Event.Computer;

    public long RecordId => Event.RecordId;

    // ---- explanation ----

    public bool HasRule => _rule is not null;

    public string Meaning => _rule?.Meaning ?? "";

    public string Causes => _rule?.Causes ?? "";

    public string Next => _rule?.Next ?? "";

    public string NoExplanationText => $"No explanation yet for {Event.Source} {Event.EventId}";

    // ---- content ----

    [ObservableProperty] private string _selectedTab = "explained";
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _message;
    [ObservableProperty] private string _xml = "";
    [ObservableProperty] private IReadOnlyList<KeyValuePair<string, string>> _data = [];
    [ObservableProperty] private string _loadError = "";

    public bool HasMessage => !string.IsNullOrWhiteSpace(Message);

    public string MessageText => HasMessage ? Message! : MessageMissing;

    partial void OnMessageChanged(string? value)
    {
        OnPropertyChanged(nameof(HasMessage));
        OnPropertyChanged(nameof(MessageText));
    }

    public IRelayCommand<string> SelectTabCommand { get; }

    public IRelayCommand CopyForTicketCommand { get; }

    public IRelayCommand OpenDocsCommand { get; }

    private void SelectTab(string? tab)
    {
        if (tab is not null && Tabs.Contains(tab)) SelectedTab = tab;
    }

    /// <summary>Reads the message, XML and data fields. The message is built only now, and kept in the cache.</summary>
    public async Task LoadAsync()
    {
        IsLoading = true;
        var target = LogTarget.Channel(Event.LogName);
        try
        {
            var (message, xml, data) = await Task.Run(() => (
                _cache.GetOrAdd(Event.LogName, Event.RecordId, () => _source.FormatMessage(target, Event.RecordId)),
                _source.GetXml(target, Event.RecordId),
                _source.GetEventData(target, Event.RecordId)));

            Message = message;
            Xml = Indent(xml);
            Data = data;
        }
        catch (EventSourceException ex)
        {
            LoadError = ex.Reason;
            Message = null;
        }
        finally
        {
            IsLoading = false;
        }

        // If the message cannot be built, the raw data is the useful part.
        SelectedTab = HasMessage ? "explained" : "raw";
    }

    private static string Indent(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) return "";
        try { return XDocument.Parse(xml).ToString(); }
        catch (System.Xml.XmlException) { return xml; }
    }

    // ---- copy for ticket ----

    private void CopyForTicket()
    {
        var nl = Environment.NewLine;
        var firstLine = HasMessage ? Message!.Split('\n')[0].Trim() : "";
        var title = _rule is not null ? _rule.Title : firstLine.Length > 0 ? firstLine : Title;

        var lines = new List<string>
        {
            $"[{Event.Level.Label()}] {Event.Source} {Event.EventId.ToString(CultureInfo.InvariantCulture)} — {title}",
            $"Computer: {Event.Computer} | Log: {Event.LogName} | Time: {TimeText}",
            "Message:",
        };

        if (HasMessage) lines.AddRange(Message!.Replace("\r\n", "\n").TrimEnd().Split('\n'));
        else if (Data.Count > 0) lines.AddRange(Data.Select(p => $"{p.Key}: {p.Value}"));
        else lines.Add("(not available on this PC)");

        if (_rule is not null) lines.Add($"Explanation: {_rule.Meaning} Next: {_rule.Next}");

        _setClipboard(string.Join(nl, lines));
    }

    private static void SetClipboard(string text)
    {
        try { System.Windows.Clipboard.SetText(text); }
        catch (System.Runtime.InteropServices.COMException) { /* the clipboard was busy */ }
    }

    // ---- around this event ----

    [ObservableProperty] private IReadOnlyList<AroundRow> _around = [];
    [ObservableProperty] private bool _isLoadingAround;
    [ObservableProperty] private string _aroundStatus = "";

    public IAsyncRelayCommand LoadAroundCommand { get; }

    public IRelayCommand CancelAroundCommand { get; }

    /// <summary>
    /// Looks at the minute before and after this event in every readable log: the logs the troubleshooters use
    /// first, then all other enabled logs. Runs only when asked, and can be cancelled.
    /// </summary>
    public async Task LoadAroundAsync()
    {
        _aroundCts?.Cancel();
        var cts = _aroundCts = new CancellationTokenSource();

        IsLoadingAround = true;
        AroundStatus = "Looking in all logs…";
        Around = [];
        var found = new List<AroundRow>();

        try
        {
            var logs = await Task.Run(OrderedLogs, cts.Token);
            var query = new EventQuery
            {
                Logs = logs,
                Levels = AroundLevels,
                Range = new TimeRange(Event.TimeCreatedUtc - AroundWindow, Event.TimeCreatedUtc + AroundWindow),
            };

            await foreach (var update in _search.RunAsync(query, cts.Token))
            {
                found.AddRange(update.NewHits.Where(h => !IsSelf(h.Event)).Select(ToRow));
                Around = found.OrderBy(r => r.Event.TimeCreatedUtc).ToList();
            }

            AroundStatus = found.Count == 0
                ? "No other events within 60 s."
                : $"Found {Plural(found.Count)} within 60 s";
        }
        catch (OperationCanceledException)
        {
            AroundStatus = $"Cancelled · Found {Plural(found.Count)} so far";
        }
        finally
        {
            IsLoadingAround = false;
        }
    }

    private List<string> OrderedLogs()
    {
        var admin = _state.DefaultLogs.Contains("Security");
        var dashboard = _rules.AllLogs.Where(l => admin || !IsSecurity(l)).ToList();

        var others = _source.GetChannels()
            .Where(c => c.Enabled && c.Type is ChannelType.Admin or ChannelType.Operational)
            .Select(c => c.Name)
            .Where(n => admin || !IsSecurity(n))
            .Where(n => !dashboard.Contains(n, StringComparer.OrdinalIgnoreCase))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase);

        return dashboard.Concat(others).ToList();
    }

    private static bool IsSecurity(string log) => string.Equals(log, "Security", StringComparison.OrdinalIgnoreCase);

    private bool IsSelf(EventRecordLite e) =>
        e.RecordId == Event.RecordId && string.Equals(e.LogName, Event.LogName, StringComparison.OrdinalIgnoreCase);

    private AroundRow ToRow(SearchHit hit)
    {
        var e = hit.Event;
        var seconds = (int)Math.Round((e.TimeCreatedUtc - Event.TimeCreatedUtc).TotalSeconds);
        var offset = seconds > 0 ? $"+{seconds} s" : seconds < 0 ? $"−{-seconds} s" : "0 s";
        return new AroundRow(e, _state.FormatTime(e.TimeCreatedUtc)[11..], offset,
            _rules.Match(e)?.Rule.Title ?? $"{e.Source} event {e.EventId}",
            e.Level switch { EventLevel.Critical => "Critical", EventLevel.Error => "Error", EventLevel.Warning => "Warning", _ => "Info" });
    }

    private static string Plural(int n) => n == 1 ? "1 event" : $"{n} events";
}
