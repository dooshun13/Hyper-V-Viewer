using HvViewer.Core.Model;

namespace HvViewer.Core.Search;

/// <summary>A run of identical events shown as one row (x N).</summary>
public sealed record EventGroup(EventRecordLite Latest, EventRecordLite First, int Count);
