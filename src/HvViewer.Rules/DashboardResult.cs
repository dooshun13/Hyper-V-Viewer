using HvViewer.Core.Model;
using HvViewer.Core.Search;

namespace HvViewer.Rules;

public sealed record SeverityCounts(int Critical, int Error, int Warning, int Information);

/// <summary>One bar of the chart. Errors = Critical + Error.</summary>
public sealed record Bucket(DateTime StartUtc, int Errors, int Warnings);

/// <summary>A "Needs attention" tile. When every log it needs was skipped, <see cref="UnavailableReason"/> says why and the count is not a real zero.</summary>
public sealed record Tile(string TroubleshooterId, string Title, int Count, RuleSeverity Highest, string? UnavailableReason);

public sealed record UnusualItem(EventRecordLite Latest, RuleEvent? Rule, int Count);

/// <summary>What happened just before the last unexpected restart.</summary>
public sealed record RestartStory(DateTime AnchorUtc, EventRecordLite RestartEvent, IReadOnlyList<EventRecordLite> Before);

public sealed record DashboardResult(
    SeverityCounts Current,
    SeverityCounts Previous,
    IReadOnlyList<Bucket> Buckets,
    IReadOnlyList<Tile> Tiles,
    IReadOnlyList<UnusualItem> NewAndUnusual,
    RestartStory? Restart,
    IReadOnlyList<SkippedLog> Skipped);
