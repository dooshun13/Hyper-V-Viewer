using HvViewer.Core.Model;

namespace HvViewer.Core.Search;

/// <summary>
/// Groups events that are consecutive in time order, share the same (log, provider, event ID),
/// and are at most <see cref="MaxGap"/> apart.
/// </summary>
public static class Grouping
{
    public static readonly TimeSpan MaxGap = TimeSpan.FromMinutes(2);

    /// <param name="newestFirst">Events sorted newest first.</param>
    public static IReadOnlyList<EventGroup> Group(IReadOnlyList<EventRecordLite> newestFirst)
    {
        var groups = new List<EventGroup>();
        if (newestFirst.Count == 0) return groups;

        var latest = newestFirst[0];
        var first = latest;
        var count = 1;

        for (var i = 1; i < newestFirst.Count; i++)
        {
            var e = newestFirst[i];
            if (SameKey(first, e) && first.TimeCreatedUtc - e.TimeCreatedUtc <= MaxGap)
            {
                first = e;
                count++;
            }
            else
            {
                groups.Add(new EventGroup(latest, first, count));
                latest = first = e;
                count = 1;
            }
        }
        groups.Add(new EventGroup(latest, first, count));
        return groups;
    }

    private static bool SameKey(EventRecordLite a, EventRecordLite b) =>
        a.EventId == b.EventId
        && string.Equals(a.LogName, b.LogName, StringComparison.OrdinalIgnoreCase)
        && string.Equals(a.ProviderName, b.ProviderName, StringComparison.OrdinalIgnoreCase);
}
