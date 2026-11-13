namespace HvViewer.Core.Model;

/// <summary>The basic fields every event carries. Messages are built later, on demand.</summary>
public sealed record EventRecordLite(
    string LogName,
    long RecordId,
    DateTime TimeCreatedUtc,
    int EventId,
    EventLevel Level,
    string ProviderName,
    string? EventSourceName,
    string Computer,
    int Task,
    long Keywords)
{
    /// <summary>The name users know: the event source name when it differs, otherwise the provider.</summary>
    public string Source => EventSourceName ?? ProviderName;
}
