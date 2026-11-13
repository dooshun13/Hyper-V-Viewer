namespace HvViewer.Core.Model;

/// <summary>Windows event level, read as a number (never from translated text).</summary>
public enum EventLevel : byte
{
    LogAlways = 0,
    Critical = 1,
    Error = 2,
    Warning = 3,
    Information = 4,
    Verbose = 5,
}

public static class EventLevelExtensions
{
    /// <summary>English label for the UI. Level 0 (LogAlways) is shown as Information.</summary>
    public static string Label(this EventLevel level) => level switch
    {
        EventLevel.Critical => "Critical",
        EventLevel.Error => "Error",
        EventLevel.Warning => "Warning",
        EventLevel.Verbose => "Verbose",
        _ => "Information",
    };
}
