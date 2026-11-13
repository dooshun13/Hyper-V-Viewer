using HvViewer.Core.Model;

namespace HvViewer.Rules;

/// <summary>The loaded troubleshooters, with fast lookup of the rule that explains an event.</summary>
public sealed class RuleSet
{
    private readonly Dictionary<int, List<(Troubleshooter Troubleshooter, RuleEvent Rule)>> _byId = new();

    public RuleSet(IReadOnlyList<Troubleshooter> troubleshooters, IReadOnlyList<RuleLoadError> errors)
    {
        Troubleshooters = troubleshooters;
        Errors = errors;
        AllLogs = troubleshooters.SelectMany(t => t.Logs).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        foreach (var t in troubleshooters)
        {
            foreach (var rule in t.Events)
            {
                if (!_byId.TryGetValue(rule.Id, out var list)) _byId[rule.Id] = list = [];
                list.Add((t, rule));
            }
        }
    }

    public IReadOnlyList<Troubleshooter> Troubleshooters { get; }

    public IReadOnlyList<RuleLoadError> Errors { get; }

    /// <summary>Every log any troubleshooter reads, in file order.</summary>
    public IReadOnlyList<string> AllLogs { get; }

    /// <summary>
    /// The rule for an event: same event ID and log (ignoring case), and a provider equal to the event's
    /// provider name or its event source name. Never matches on message text.
    /// </summary>
    public (Troubleshooter Troubleshooter, RuleEvent Rule)? Match(EventRecordLite e)
    {
        if (!_byId.TryGetValue(e.EventId, out var candidates)) return null;
        foreach (var (troubleshooter, rule) in candidates)
        {
            if (string.Equals(rule.Log, e.LogName, StringComparison.OrdinalIgnoreCase)
                && (string.Equals(rule.Provider, e.ProviderName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(rule.Provider, e.EventSourceName, StringComparison.OrdinalIgnoreCase)))
                return (troubleshooter, rule);
        }
        return null;
    }

    public IReadOnlyList<RuleEvent> EventsForLog(string log) =>
        Troubleshooters.SelectMany(t => t.Events).Where(r => string.Equals(r.Log, log, StringComparison.OrdinalIgnoreCase)).ToList();
}
