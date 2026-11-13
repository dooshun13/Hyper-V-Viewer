using System.Text;

namespace HvViewer.App.States;

/// <summary>
/// The text shown (and copied) when something unexpected happens. It is built in memory only:
/// nothing is written to disk. It adds the HvViewer version, the time and the Windows version, but not the user or machine name.
/// </summary>
public sealed class ErrorReport
{
    public ErrorReport(Exception exception, string version, DateTime utcNow)
    {
        Summary = string.IsNullOrWhiteSpace(exception.Message) ? exception.GetType().Name : exception.Message.Trim();

        var sb = new StringBuilder();
        sb.AppendLine($"Hyper-V Viewer {version}");
        sb.AppendLine($"Time: {utcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"Windows: {Environment.OSVersion.VersionString}");
        sb.AppendLine("This report was not saved anywhere: nothing was written to disk.");
        sb.AppendLine();
        Append(sb, exception, "");
        Details = sb.ToString().TrimEnd();
    }

    /// <summary>One line in plain words.</summary>
    public string Summary { get; }

    /// <summary>The full text for a ticket: exception types, messages and stack traces.</summary>
    public string Details { get; }

    private static void Append(StringBuilder sb, Exception ex, string prefix)
    {
        sb.AppendLine($"{prefix}{ex.GetType().FullName}: {ex.Message}");
        if (!string.IsNullOrWhiteSpace(ex.StackTrace)) sb.AppendLine(ex.StackTrace);

        if (ex is AggregateException aggregate)
        {
            var inner = aggregate.Flatten().InnerExceptions;
            for (var i = 0; i < inner.Count; i++)
            {
                sb.AppendLine();
                Append(sb, inner[i], $"({i + 1} of {inner.Count}) ");
            }
        }
        else if (ex.InnerException is { } cause)
        {
            sb.AppendLine();
            Append(sb, cause, "Caused by: ");
        }
    }
}
