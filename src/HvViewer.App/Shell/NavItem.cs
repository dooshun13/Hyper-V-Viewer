using CommunityToolkit.Mvvm.ComponentModel;

namespace HvViewer.App.Shell;

/// <summary>One button in the left icon bar.</summary>
public sealed partial class NavItem(string key, string label, string glyph) : ObservableObject
{
    public string Key { get; } = key;

    public string Label { get; } = label;

    /// <summary>A Segoe Fluent Icons character.</summary>
    public string Glyph { get; } = glyph;

    [ObservableProperty]
    private bool _isSelected;
}
