using HvViewer.Core.Model;

namespace HvViewer.Core.Reading;

/// <summary>Reads events from event logs. Read-only.</summary>
public interface IEventSource
{
    /// <summary>Events matching the XPath, newest first. Throws <see cref="OperationCanceledException"/> when cancelled.</summary>
    IEnumerable<EventRecordLite> Query(LogTarget log, string xpath, CancellationToken ct);

    /// <summary>Like <see cref="Query"/>, with the text to search in for the chosen target (null when it cannot be built).</summary>
    IEnumerable<(EventRecordLite Event, string? Text)> QueryWithText(LogTarget log, string xpath, TextTarget target, CancellationToken ct);

    /// <summary>The formatted message, or null when the publisher is not installed or has no message.</summary>
    string? FormatMessage(LogTarget log, long recordId);

    string? GetXml(LogTarget log, long recordId);

    /// <summary>Event data fields as name/value pairs (name is "Data1".."DataN" when unnamed).</summary>
    IReadOnlyList<KeyValuePair<string, string>> GetEventData(LogTarget log, long recordId);

    /// <summary>The display name of a task category, or null if unknown.</summary>
    string? GetTaskName(string provider, int task);

    IReadOnlyList<ChannelInfo> GetChannels();
}
