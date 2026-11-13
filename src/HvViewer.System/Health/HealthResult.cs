namespace HvViewer.Machine.Health;

public enum HealthStatus { Ok, Warning, Problem, Unknown }

/// <summary>The outcome of one health check: a short value for the card and a longer detail line.</summary>
public sealed record HealthResult(string Id, string Label, HealthStatus Status, string Value, string Detail);
