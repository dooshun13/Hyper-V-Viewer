namespace HvViewer.Core.Reading;

/// <summary>A log could not be read.</summary>
public class EventSourceException(string log, string reason, Exception? inner = null)
    : Exception($"{log}: {reason}", inner)
{
    public string Log { get; } = log;

    public string Reason { get; } = reason;
}

/// <summary>Windows denied access to the log (for example Security without administrator rights).</summary>
public sealed class LogAccessDeniedException(string log, Exception? inner = null)
    : EventSourceException(log, "Access denied", inner);

/// <summary>The log or file does not exist.</summary>
public sealed class LogNotFoundException(string log, Exception? inner = null)
    : EventSourceException(log, "Log not found", inner);
