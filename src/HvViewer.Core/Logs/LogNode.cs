namespace HvViewer.Core.Logs;

/// <summary>A node in the log category tree. <see cref="ChannelName"/> is the log's internal name; null for folders.</summary>
public sealed record LogNode(string Label, string? ChannelName, IReadOnlyList<LogNode> Children);
