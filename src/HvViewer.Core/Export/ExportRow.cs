namespace HvViewer.Core.Export;

/// <summary>One line of an export. <see cref="Count"/> is greater than 1 for a group of identical events.</summary>
public sealed record ExportRow(string Time, string Level, int EventId, string Source, string Log, string Computer, string Message, int Count);
