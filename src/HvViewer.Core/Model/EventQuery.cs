using System.Text.RegularExpressions;

namespace HvViewer.Core.Model;

/// <summary>Everything a search needs. Turns into an XPath filter plus an in-process text filter.</summary>
public sealed record EventQuery
{
    public string? Text { get; init; }
    public TextOptions TextOptions { get; init; } = new();
    public TextTarget Target { get; init; } = TextTarget.Message;
    public TimeRange Range { get; init; }

    /// <summary>Set when the range was built as "the last N minutes/hours/days", so the query text can say last:24h.</summary>
    public TimeSpan? LastSpan { get; init; }
    public IReadOnlyList<int> IncludeIds { get; init; } = [];
    public IReadOnlyList<int> ExcludeIds { get; init; } = [];
    public IReadOnlyList<string> Sources { get; init; } = [];
    public IReadOnlyList<string> Logs { get; init; } = [];
    public IReadOnlyList<EventLevel> Levels { get; init; } = [];
    public IReadOnlyList<TaskFilter> Tasks { get; init; } = [];

    /// <summary>Returns user-facing validation errors; empty when the query can run.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (Range.FromUtc > Range.ToUtc) errors.Add("From must be before To.");
        if (Logs.Count == 0) errors.Add("Select at least one log.");

        foreach (var id in IncludeIds.Intersect(ExcludeIds).Distinct())
            errors.Add($"Event ID {id} is both included and excluded.");

        if (TextOptions.Regex && !string.IsNullOrEmpty(Text))
        {
            try { _ = new Regex(Text, RegexOptions.None, TimeSpan.FromMilliseconds(100)); }
            catch (ArgumentException ex) { errors.Add("Invalid regular expression: " + ex.Message); }
        }

        return errors;
    }
}
