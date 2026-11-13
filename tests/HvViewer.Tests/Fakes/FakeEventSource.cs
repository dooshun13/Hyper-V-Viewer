using System.Globalization;
using HvViewer.Core.Model;
using HvViewer.Core.Reading;

namespace HvViewer.Tests.Fakes;

/// <summary>An in-memory <see cref="IEventSource"/> for unit tests. It ignores XPath but records it.</summary>
public sealed class FakeEventSource : IEventSource
{
    private readonly Dictionary<string, List<EventRecordLite>> _logs = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _denied = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string, long), string?> _messages = new();
    private readonly Dictionary<(string, long), List<KeyValuePair<string, string>>> _data = new();

    /// <summary>The XPath each log was last queried with.</summary>
    public Dictionary<string, string> XPaths { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every XPath each log was queried with, in order.</summary>
    public Dictionary<string, List<string>> XPathHistory { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Called with the running index of every event the source hands out (lets a test act mid-scan).</summary>
    public Action<int>? OnEvent { get; set; }

    private int _handedOut;

    /// <summary>Every log target the source was asked to read.</summary>
    public List<LogTarget> Targets { get; } = [];

    public FakeEventSource AddLog(string log, params EventRecordLite[] events)
    {
        _logs[log] = events.OrderByDescending(e => e.TimeCreatedUtc).ToList();
        return this;
    }

    public FakeEventSource Deny(string log)
    {
        _denied.Add(log);
        return this;
    }

    public FakeEventSource WithMessage(string log, long recordId, string? message)
    {
        _messages[(log, recordId)] = message;
        return this;
    }

    public FakeEventSource WithData(string log, long recordId, params (string Name, string Value)[] pairs)
    {
        _data[(log, recordId)] = pairs.Select(p => new KeyValuePair<string, string>(p.Name, p.Value)).ToList();
        return this;
    }

    public IEnumerable<EventRecordLite> Query(LogTarget log, string xpath, CancellationToken ct)
    {
        Targets.Add(log);
        XPaths[log.Name] = xpath;
        if (!XPathHistory.TryGetValue(log.Name, out var history)) XPathHistory[log.Name] = history = [];
        history.Add(xpath);
        if (_denied.Contains(log.Name)) throw new LogAccessDeniedException(log.Name);
        if (!_logs.TryGetValue(log.Name, out var events)) throw new LogNotFoundException(log.Name);
        var range = TimeRangeIn(xpath);
        foreach (var e in events)
        {
            ct.ThrowIfCancellationRequested();
            if (range is { } r && (e.TimeCreatedUtc < r.FromUtc || e.TimeCreatedUtc > r.ToUtc)) continue;
            OnEvent?.Invoke(++_handedOut);
            yield return e;
        }
    }

    /// <summary>The only part of the XPath the fake honours: the TimeCreated range.</summary>
    private static TimeRange? TimeRangeIn(string xpath)
    {
        var m = System.Text.RegularExpressions.Regex.Match(xpath, "@SystemTime>='(?<from>[^']+)' and @SystemTime<='(?<to>[^']+)'");
        if (!m.Success) return null;
        var style = DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal;
        return new TimeRange(
            DateTime.Parse(m.Groups["from"].Value, CultureInfo.InvariantCulture, style),
            DateTime.Parse(m.Groups["to"].Value, CultureInfo.InvariantCulture, style));
    }

    public IEnumerable<(EventRecordLite Event, string? Text)> QueryWithText(LogTarget log, string xpath, TextTarget target, CancellationToken ct)
    {
        foreach (var e in Query(log, xpath, ct))
        {
            var message = _messages.GetValueOrDefault((log.Name, e.RecordId));
            yield return target switch
            {
                TextTarget.Xml => (e, $"<Event><EventID>{e.EventId}</EventID></Event>"),
                TextTarget.AllFields => (e, $"{message} {e.ProviderName}"),
                _ => (e, message),
            };
        }
    }

    public string? FormatMessage(LogTarget log, long recordId) => _messages.GetValueOrDefault((log.Name, recordId));

    public string? GetXml(LogTarget log, long recordId) => $"<Event><EventRecordID>{recordId}</EventRecordID></Event>";

    public IReadOnlyList<KeyValuePair<string, string>> GetEventData(LogTarget log, long recordId) =>
        _data.GetValueOrDefault((log.Name, recordId)) ?? [];

    public string? GetTaskName(string provider, int task) => null;

    public IReadOnlyList<ChannelInfo> GetChannels() =>
        _logs.Keys.Select(k => new ChannelInfo(k, ChannelType.Admin, true)).ToList();
}

/// <summary>Short builder for test events.</summary>
public static class Ev
{
    public static EventRecordLite Make(string log, long recordId, string time, int id = 1, string provider = "TestProvider",
        EventLevel level = EventLevel.Warning, int task = 0) =>
        new(log, recordId,
            DateTime.SpecifyKind(DateTime.Parse(time, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal), DateTimeKind.Utc),
            id, level, provider, null, "PC", task, 0);
}
