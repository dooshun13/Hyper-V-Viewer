using System.Runtime.Versioning;
using HvViewer.Core.Model;
using Reader = System.Diagnostics.Eventing.Reader;

namespace HvViewer.Core.Reading;

/// <summary>
/// Reads events with .NET's built-in event log classes. Simpler and slower than <see cref="NativeEventSource"/>;
/// meant for single events and occasional queries. It does not fill <see cref="EventRecordLite.EventSourceName"/>.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ManagedEventSource : IEventSource
{
    private readonly TaskNameCache _taskNames = new();

    public IEnumerable<EventRecordLite> Query(LogTarget log, string xpath, CancellationToken ct)
    {
        foreach (var (record, _) in Read(log, xpath, null, ct)) yield return record;
    }

    public IEnumerable<(EventRecordLite Event, string? Text)> QueryWithText(LogTarget log, string xpath, TextTarget target, CancellationToken ct) =>
        Read(log, xpath, target, ct);

    private IEnumerable<(EventRecordLite, string?)> Read(LogTarget log, string xpath, TextTarget? target, CancellationToken ct)
    {
        using var reader = OpenReader(log, xpath);
        while (true)
        {
            ct.ThrowIfCancellationRequested();

            Reader.EventRecord? raw;
            try { raw = reader.ReadEvent(); }
            catch (Exception ex) when (ex is Reader.EventLogException or UnauthorizedAccessException) { throw Map(log.Name, ex); }

            if (raw is null) yield break;
            using (raw)
            {
                var record = ToRecord(raw, log);
                yield return (record, target is null ? null : BuildText(raw, record, target.Value));
            }
        }
    }

    private static Reader.EventLogReader OpenReader(LogTarget log, string xpath)
    {
        try
        {
            var query = new Reader.EventLogQuery(log.Name, log.IsFile ? Reader.PathType.FilePath : Reader.PathType.LogName, xpath)
            {
                ReverseDirection = true,
            };
            return new Reader.EventLogReader(query);
        }
        catch (Exception ex) when (ex is Reader.EventLogException or UnauthorizedAccessException)
        {
            throw Map(log.Name, ex);
        }
    }

    private static EventRecordLite ToRecord(Reader.EventRecord e, LogTarget log) => new(
        LogName: e.LogName ?? log.Name,
        RecordId: e.RecordId ?? 0,
        TimeCreatedUtc: (e.TimeCreated ?? DateTime.MinValue).ToUniversalTime(),
        EventId: e.Id,
        Level: (EventLevel)(e.Level ?? 0),
        ProviderName: e.ProviderName ?? string.Empty,
        EventSourceName: null,
        Computer: e.MachineName ?? string.Empty,
        Task: e.Task ?? 0,
        Keywords: e.Keywords ?? 0);

    private static string? BuildText(Reader.EventRecord e, EventRecordLite record, TextTarget target)
    {
        switch (target)
        {
            case TextTarget.Message:
                // No message: search the event's data instead (the reader already has the event, so no second lookup).
                return Describe(e) ?? DataText(e);
            case TextTarget.Xml:
                return e.ToXml();
            default:
                var values = NativeEventSource.DataPairs(e.ToXml()).Select(p => p.Value);
                return string.Join(" ", new[] { Describe(e), record.ProviderName }.Concat(values).Where(s => !string.IsNullOrEmpty(s)));
        }
    }

    private static string? DataText(Reader.EventRecord e)
    {
        var joined = string.Join(" ", NativeEventSource.DataPairs(e.ToXml()).Select(p => p.Value));
        return joined.Length == 0 ? null : joined;
    }

    private static string? Describe(Reader.EventRecord e)
    {
        try { return e.FormatDescription()?.TrimEnd(); }
        catch (Reader.EventLogException) { return null; }
    }

    public string? FormatMessage(LogTarget log, long recordId) => WithRecord(log, recordId, Describe);

    public string? GetXml(LogTarget log, long recordId) => WithRecord(log, recordId, e => e.ToXml());

    public IReadOnlyList<KeyValuePair<string, string>> GetEventData(LogTarget log, long recordId)
    {
        var xml = GetXml(log, recordId);
        return xml is null ? [] : NativeEventSource.DataPairs(xml);
    }

    private static T? WithRecord<T>(LogTarget log, long recordId, Func<Reader.EventRecord, T?> action)
    {
        using var reader = OpenReader(log, $"*[System[EventRecordID={recordId}]]");
        Reader.EventRecord? raw;
        try { raw = reader.ReadEvent(); }
        catch (Exception ex) when (ex is Reader.EventLogException or UnauthorizedAccessException) { throw Map(log.Name, ex); }
        if (raw is null) return default;
        using (raw) return action(raw);
    }

    public string? GetTaskName(string provider, int task) => _taskNames.Get(provider, task);

    public IReadOnlyList<ChannelInfo> GetChannels()
    {
        var channels = new List<ChannelInfo>();
        using var session = new Reader.EventLogSession();
        foreach (var name in session.GetLogNames())
        {
            try
            {
                using var config = new Reader.EventLogConfiguration(name, session);
                var type = config.LogType switch
                {
                    Reader.EventLogType.Administrative => ChannelType.Admin,
                    Reader.EventLogType.Analytical => ChannelType.Analytic,
                    Reader.EventLogType.Debug => ChannelType.Debug,
                    _ => ChannelType.Operational,
                };
                channels.Add(new ChannelInfo(name, type, config.IsEnabled));
            }
            catch (Exception ex) when (ex is Reader.EventLogException or UnauthorizedAccessException)
            {
                channels.Add(new ChannelInfo(name, ChannelType.Operational, false));
            }
        }
        return channels;
    }

    private static EventSourceException Map(string log, Exception ex) => ex switch
    {
        UnauthorizedAccessException => new LogAccessDeniedException(log, ex),
        Reader.EventLogNotFoundException => new LogNotFoundException(log, ex),
        _ => new EventSourceException(log, ex.Message, ex),
    };
}
