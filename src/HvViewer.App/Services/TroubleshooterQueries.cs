using HvViewer.Core.Model;
using HvViewer.Rules;

namespace HvViewer.App.Services;

/// <summary>Builds the Events query that "opens" a troubleshooter: its logs and all of its event IDs.</summary>
public static class TroubleshooterQueries
{
    public static EventQuery For(Troubleshooter troubleshooter, TimeRange range, TimeSpan? lastSpan) => new()
    {
        Logs = troubleshooter.Logs,
        IncludeIds = troubleshooter.Events.Select(e => e.Id).Distinct().ToList(),
        Range = range,
        LastSpan = lastSpan,
    };
}
