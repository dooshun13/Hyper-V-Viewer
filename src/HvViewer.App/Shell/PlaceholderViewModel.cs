using CommunityToolkit.Mvvm.ComponentModel;

namespace HvViewer.App.Shell;

/// <summary>Stands in for a screen that is not built yet.</summary>
public sealed class PlaceholderViewModel(string title) : ObservableObject
{
    public string Title { get; } = title;
}
