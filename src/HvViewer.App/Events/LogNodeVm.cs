using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace HvViewer.App.Events;

/// <summary>One row in the log picker tree: a folder, a log, or a troubleshooter.</summary>
public sealed partial class LogNodeVm : ObservableObject
{
    private readonly LogPickerViewModel _owner;

    public LogNodeVm(LogPickerViewModel owner, string label, string? channelName, IReadOnlyList<string>? explicitChannels = null,
        bool isEnabled = true, string tooltip = "")
    {
        _owner = owner;
        Label = label;
        ChannelName = channelName;
        ExplicitChannels = explicitChannels;
        IsEnabled = isEnabled;
        Tooltip = tooltip;
    }

    public string Label { get; }

    /// <summary>The log's internal name; null for folders and troubleshooters.</summary>
    public string? ChannelName { get; }

    /// <summary>For a troubleshooter node: exactly the logs it reads.</summary>
    public IReadOnlyList<string>? ExplicitChannels { get; }

    public ObservableCollection<LogNodeVm> Children { get; } = [];

    public bool IsEnabled { get; }

    public string Tooltip { get; }

    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private bool _isVisible = true;
    [ObservableProperty] private string _countText = "";

    /// <summary>All log names this node stands for.</summary>
    public IEnumerable<string> Channels()
    {
        if (ExplicitChannels is not null)
        {
            foreach (var c in ExplicitChannels) yield return c;
            yield break;
        }
        if (ChannelName is not null) yield return ChannelName;
        foreach (var child in Children)
            foreach (var c in child.Channels()) yield return c;
    }

    /// <summary>True when all of its logs are selected, false when none, null when some.</summary>
    public bool? IsChecked
    {
        get => _owner.StateOf(this);
        set => _owner.Set(this, value == true);
    }

    public void RaiseChecked() => OnPropertyChanged(nameof(IsChecked));
}
