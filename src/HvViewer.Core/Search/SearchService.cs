using System.Runtime.CompilerServices;
using System.Threading.Channels;
using HvViewer.Core.Model;
using HvViewer.Core.Query;
using HvViewer.Core.Reading;

namespace HvViewer.Core.Search;

public sealed record SearchHit(EventRecordLite Event, string? Text, IReadOnlyList<(int Start, int Length)> Matches);

public sealed record SkippedLog(string Log, string Reason)
{
    /// <summary>The skip entry shown to the user for a log that could not be read.</summary>
    public static SkippedLog From(string log, EventSourceException ex) => new(log, ex switch
    {
        LogAccessDeniedException => "Access denied: run as administrator to include",
        LogNotFoundException => "Log not found",
        _ => ex.Reason,
    });
}

/// <summary>One step of a running search. <see cref="Skipped"/> and <see cref="Scanned"/> are totals so far.</summary>
public sealed record SearchUpdate(IReadOnlyList<SearchHit> NewHits, int Scanned, IReadOnlyList<SkippedLog> Skipped, bool Completed, int RegexTimeouts);

/// <summary>
/// Runs an <see cref="EventQuery"/> over its logs, one log at a time, and streams results in batches of 256.
/// The XPath part is filtered by Windows; the text part (and long ID lists and task filters) here.
/// </summary>
public sealed class SearchService(IEventSource source, MessageCache cache)
{
    private const int BatchSize = 256;

    /// <exception cref="ArgumentException">The query is invalid.</exception>
    public IAsyncEnumerable<SearchUpdate> RunAsync(EventQuery query, CancellationToken ct)
    {
        var errors = query.Validate();
        if (errors.Count > 0) throw new ArgumentException(string.Join(" ", errors), nameof(query));
        return Stream(query, ct);
    }

    private async IAsyncEnumerable<SearchUpdate> Stream(EventQuery query, [EnumeratorCancellation] CancellationToken ct)
    {
        var channel = Channel.CreateUnbounded<SearchUpdate>();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var producer = Task.Run(() => Produce(query, channel.Writer, linked.Token), CancellationToken.None);
        try
        {
            await foreach (var update in channel.Reader.ReadAllAsync(ct))
            {
                ct.ThrowIfCancellationRequested();
                yield return update;
            }
        }
        finally
        {
            linked.Cancel(); // stop reading if the caller stopped early
            try { await producer; }
            catch (OperationCanceledException) { }
        }
    }

    /// <summary>Reads the logs and writes updates. Never throws: failures complete the channel.</summary>
    private void Produce(EventQuery query, ChannelWriter<SearchUpdate> writer, CancellationToken ct)
    {
        try
        {
            var xpath = XPathBuilder.Build(query);
            var matcher = string.IsNullOrEmpty(query.Text) ? null : new TextMatcher(query.Text, query.TextOptions);
            var include = xpath.IdsInProcess ? query.IncludeIds.ToHashSet() : null;
            var exclude = xpath.IdsInProcess ? query.ExcludeIds.ToHashSet() : null;

            var skipped = new List<SkippedLog>();
            var batch = new List<SearchHit>(BatchSize);
            var scanned = 0;
            var inBatch = 0;

            void Flush(bool completed)
            {
                writer.TryWrite(new SearchUpdate(batch.ToArray(), scanned, skipped.ToArray(), completed, matcher?.TimeoutCount ?? 0));
                batch.Clear();
                inBatch = 0;
            }

            foreach (var logName in query.Logs)
            {
                ct.ThrowIfCancellationRequested();
                var log = logName.EndsWith(".evtx", StringComparison.OrdinalIgnoreCase) ? LogTarget.File(logName) : LogTarget.Channel(logName);
                try
                {
                    var events = matcher is null
                        ? source.Query(log, xpath.XPath, ct).Select(e => (Event: e, Text: (string?)null))
                        : source.QueryWithText(log, xpath.XPath, query.Target, ct);

                    foreach (var (e, text) in events)
                    {
                        scanned++;
                        inBatch++;

                        var hit = Evaluate(e, text, log, query, matcher, include, exclude);
                        if (hit is not null) batch.Add(hit);

                        if (inBatch >= BatchSize) Flush(completed: false);
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (EventSourceException ex)
                {
                    skipped.Add(SkippedLog.From(logName, ex));
                }

                if (inBatch > 0 || batch.Count > 0) Flush(completed: false);
            }

            Flush(completed: true);
            writer.Complete();
        }
        catch (Exception ex)
        {
            writer.Complete(ex);
        }
    }

    private SearchHit? Evaluate(EventRecordLite e, string? text, LogTarget log, EventQuery query, TextMatcher? matcher,
        HashSet<int>? include, HashSet<int>? exclude)
    {
        if (include is { Count: > 0 } && !include.Contains(e.EventId)) return null;
        if (exclude is { Count: > 0 } && exclude.Contains(e.EventId)) return null;
        if (query.Tasks.Count > 0 && !query.Tasks.Any(t => t.Task == e.Task && MatchesProvider(e, t.Provider))) return null;

        if (matcher is null) return new SearchHit(e, null, []);

        var haystack = text;
        if (haystack is null && query.Target == TextTarget.Message) haystack = DataText(log, e);

        var spans = matcher.Matches(haystack);
        if (spans.Count == 0 && !matcher.IsMatch(haystack)) return null;

        if (query.Target == TextTarget.Message && text is not null) cache.GetOrAdd(e.LogName, e.RecordId, () => text);
        return new SearchHit(e, haystack, spans);
    }

    private string? DataText(LogTarget log, EventRecordLite e)
    {
        try
        {
            var values = source.GetEventData(log, e.RecordId).Select(p => p.Value);
            var joined = string.Join(" ", values);
            return joined.Length == 0 ? null : joined;
        }
        catch (EventSourceException)
        {
            return null;
        }
    }

    private static bool MatchesProvider(EventRecordLite e, string provider) =>
        string.Equals(e.ProviderName, provider, StringComparison.OrdinalIgnoreCase)
        || string.Equals(e.EventSourceName, provider, StringComparison.OrdinalIgnoreCase);

}
