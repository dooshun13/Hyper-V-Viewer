using HvViewer.Core.Model;
using HvViewer.Core.Query;
using HvViewer.Core.Reading;
using HvViewer.Core.Search;

namespace HvViewer.Rules;

/// <summary>
/// Scans the logs the troubleshooters need, one at a time, and builds the dashboard. It reports a partial
/// result after each log so tiles can appear as the scan goes.
/// </summary>
public sealed class DashboardService(IEventSource source, RuleSet rules)
{
    private static readonly TimeSpan PreviousSpan = TimeSpan.FromDays(7);
    private static readonly EventLevel[] WarningAndWorse = [EventLevel.Critical, EventLevel.Error, EventLevel.Warning];

    // Windows event log XPath allows only about 20 clauses, so long ID lists are not added to the previous-period filter.
    private const int MaxIdsInPreviousFilter = 15;

    public Task<DashboardResult> RunAsync(TimeRange range, IProgress<DashboardResult>? partial, CancellationToken ct) =>
        Task.Run(() => Run(range, partial, ct), ct);

    private DashboardResult Run(TimeRange range, IProgress<DashboardResult>? partial, CancellationToken ct)
    {
        var analyzer = new DashboardAnalyzer(rules);
        var current = new List<EventRecordLite>();
        var previous = new List<EventRecordLite>();
        var skipped = new List<SkippedLog>();
        var previousRange = new TimeRange(range.FromUtc - PreviousSpan, range.FromUtc);

        foreach (var log in rules.AllLogs)
        {
            ct.ThrowIfCancellationRequested();
            var target = LogTarget.Channel(log);
            var ruleIds = rules.EventsForLog(log).Select(r => r.Id).Distinct().ToList();

            try
            {
                // Security is large and sensitive: read only the events the rules know about.
                var onlyRuleIds = string.Equals(log, "Security", StringComparison.OrdinalIgnoreCase);
                var currentQuery = XPathBuilder.Build(new EventQuery { Range = range, Logs = [log], IncludeIds = onlyRuleIds ? ruleIds : [] });
                var allowed = onlyRuleIds && currentQuery.IdsInProcess ? ruleIds.ToHashSet() : null;

                foreach (var e in source.Query(target, currentQuery.XPath, ct))
                    if (allowed is null || allowed.Contains(e.EventId)) current.Add(e);

                var previousXPath = XPathBuilder.LevelsOrIds(WarningAndWorse, ruleIds.Count <= MaxIdsInPreviousFilter ? ruleIds : [], previousRange);
                previous.AddRange(source.Query(target, previousXPath, ct));
            }
            catch (EventSourceException ex)
            {
                skipped.Add(SkippedLog.From(log, ex));
            }

            partial?.Report(analyzer.Analyze(current, previous, range, skipped.ToList()));
        }

        return analyzer.Analyze(current, previous, range, skipped.ToList());
    }
}
