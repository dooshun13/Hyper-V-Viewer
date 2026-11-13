using System.Globalization;
using HvViewer.Core.Model;

namespace HvViewer.Core.Query;

public sealed record XPathResult(string XPath, bool IdsInProcess);

/// <summary>Turns the filterable parts of an <see cref="EventQuery"/> into a native event log XPath filter. Text is never part of it.</summary>
public static class XPathBuilder
{
    public const int MaxXPathIds = 20;

    public static XPathResult Build(EventQuery q)
    {
        var idsInProcess = q.IncludeIds.Count > MaxXPathIds || q.ExcludeIds.Count > MaxXPathIds;
        var clauses = new List<string>();

        if (!idsInProcess)
        {
            if (q.IncludeIds.Count > 0)
                clauses.Add("(" + string.Join(" or ", q.IncludeIds.Select(id => $"EventID={id}")) + ")");
            if (q.ExcludeIds.Count > 0)
                clauses.Add(string.Join(" and ", q.ExcludeIds.Select(id => $"EventID!={id}")));
        }

        if (q.Levels.Count > 0)
        {
            var levels = q.Levels.SelectMany(l => l == EventLevel.Information ? new[] { 0, 4 } : new[] { (int)l }).Distinct();
            clauses.Add("(" + string.Join(" or ", levels.Select(l => $"Level={l}")) + ")");
        }

        if (q.Sources.Count > 0)
        {
            var parts = q.Sources.Select(Escape).SelectMany(s => new[] { $"@Name='{s}'", $"@EventSourceName='{s}'" });
            clauses.Add("Provider[" + string.Join(" or ", parts) + "]");
        }

        if (q.Tasks.Count > 0)
            clauses.Add("(" + string.Join(" or ", q.Tasks.Select(t => t.Task).Distinct().Select(t => $"Task={t}")) + ")");

        clauses.Add($"TimeCreated[@SystemTime>='{Format(q.Range.FromUtc)}' and @SystemTime<='{Format(q.Range.ToUtc)}']");

        return new XPathResult("*[System[" + string.Join(" and ", clauses) + "]]", idsInProcess);
    }

    /// <summary>Events at one of the given levels OR with one of the given IDs, inside the time range.</summary>
    public static string LevelsOrIds(IReadOnlyCollection<EventLevel> levels, IReadOnlyCollection<int> ids, TimeRange range)
    {
        var either = levels.Select(l => $"Level={(int)l}").Concat(ids.Select(id => $"EventID={id}")).ToList();
        var time = $"TimeCreated[@SystemTime>='{Format(range.FromUtc)}' and @SystemTime<='{Format(range.ToUtc)}']";
        return either.Count == 0
            ? $"*[System[{time}]]"
            : $"*[System[({string.Join(" or ", either)}) and {time}]]";
    }

    private static string Format(DateTime utc) =>
        utc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private static string Escape(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace("'", "&apos;");
}
