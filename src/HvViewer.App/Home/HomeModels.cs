using HvViewer.Core.Model;

namespace HvViewer.App.Home;

// Plain display models for the Home screen. Colours are named resource keys (Critical, Error, Warning, Info, Ok, TextMuted),
// so view models stay free of WPF types and the theme decides the actual colours.

public sealed record RangeOption(string Key, string Label);

public sealed record SeverityRow(string Label, int Value, string Delta, string ColorKey, EventLevel Level);

public sealed record BarVm(double ErrorHeight, double WarningHeight, string Tooltip);

public sealed record TileVm(string TroubleshooterId, string Title, string ValueText, string ColorKey, string Hint, bool IsUnavailable);

public sealed record HealthRow(string Label, string Value, string Mark, string MarkKey, string ValueKey, string Detail);

public sealed record RestartRow(string Time, string ColorKey, string Message, string Source);

public sealed record UnusualRow(int EventId, string Provider, string Log, string Title, string Source, string When, string ColorKey, int Count);
