using CommunityToolkit.Mvvm.ComponentModel;
using HvViewer.Core.Export;
using HvViewer.Core.Model;
using HvViewer.Core.Search;

namespace HvViewer.App.Events;

/// <summary>One row of the results: a single event, or a group of identical ones (x N).</summary>
public sealed record ResultRow(
    EventGroup Group,
    string TimeText,
    string DateText,
    string LevelText,
    string LevelKey,
    string Title,
    string Source,
    int EventId,
    string CountText,
    string? Message,
    IReadOnlyList<(int Start, int Length)> Matches);

/// <summary>One bar of the "when did the matches happen" mini chart.</summary>
public sealed record HistogramBar(double Height, string Tooltip);

public sealed record TaskOption(string Label, TaskFilter Filter);

public sealed record PresetOption(string Key, string Label);

/// <summary>A level toggle (Critical, Error, ...). Changing it updates the query line.</summary>
public sealed partial class LevelOption(string label, EventLevel level, Action changed) : ObservableObject
{
    public string Label { get; } = label;

    public EventLevel Level { get; } = level;

    [ObservableProperty] private bool _isSelected;

    partial void OnIsSelectedChanged(bool value) => changed();
}

/// <summary>Saves exports. The real one shows a Save dialog; tests use a fake.</summary>
public interface IExportService
{
    /// <returns>Null on success or when the user cancelled; otherwise a message to show.</returns>
    string? Export(string title, string subtitle, IReadOnlyList<ExportRow> rows);
}
