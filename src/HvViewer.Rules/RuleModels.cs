namespace HvViewer.Rules;

/// <summary>How serious an event is, as judged by its rule. Serialised in lower case.</summary>
public enum RuleSeverity { Info, Warning, Error, Critical }

/// <summary>One event a troubleshooter knows about, matched by provider, event ID and log.</summary>
public sealed record RuleEvent(
    string Provider,
    int Id,
    string Log,
    RuleSeverity Severity,
    string Title,
    string Meaning,
    string Causes,
    string Next,
    string Docs);

public sealed record Troubleshooter(int Schema, string Id, string Title, string Summary, IReadOnlyList<RuleEvent> Events)
{
    /// <summary>The logs this troubleshooter needs, derived from its events.</summary>
    public IReadOnlyList<string> Logs { get; } =
        Events.Select(e => e.Log).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}

/// <summary>A rule file that was skipped, and why.</summary>
public sealed record RuleLoadError(string Source, string Message);
