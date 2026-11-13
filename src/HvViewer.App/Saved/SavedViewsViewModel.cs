using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HvViewer.App.Services;
using HvViewer.Core.Query;
using HvViewer.Core.Settings;

namespace HvViewer.App.Saved;

/// <summary>One saved search in the list. <see cref="EditName"/> is the name while it is being renamed.</summary>
public sealed partial class SavedViewItem(string name, string queryText) : ObservableObject
{
    public string Name { get; } = name;

    public string QueryText { get; } = queryText;

    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private string _editName = name;
}

/// <summary>The Saved views screen: open, rename and delete the searches saved from the Events screen.</summary>
public sealed partial class SavedViewsViewModel : ObservableObject
{
    private readonly AppState _state;
    private readonly INavigator _navigator;

    public SavedViewsViewModel(AppState state, INavigator navigator)
    {
        _state = state;
        _navigator = navigator;

        OpenCommand = new RelayCommand<SavedViewItem>(Open);
        DeleteCommand = new RelayCommand<SavedViewItem>(Delete);
        StartRenameCommand = new RelayCommand<SavedViewItem>(item =>
        {
            if (item is null) return;
            item.EditName = item.Name;
            item.IsEditing = true;
        });
        CommitRenameCommand = new RelayCommand<SavedViewItem>(Rename);
        CancelRenameCommand = new RelayCommand<SavedViewItem>(item =>
        {
            if (item is null) return;
            item.EditName = item.Name;
            item.IsEditing = false;
        });

        _state.Changed += Reload;
        Reload();
    }

    [ObservableProperty] private IReadOnlyList<SavedViewItem> _items = [];
    [ObservableProperty] private string _message = "";

    public bool HasItems => Items.Count > 0;

    partial void OnItemsChanged(IReadOnlyList<SavedViewItem> value) => OnPropertyChanged(nameof(HasItems));

    public IRelayCommand<SavedViewItem> OpenCommand { get; }

    public IRelayCommand<SavedViewItem> DeleteCommand { get; }

    public IRelayCommand<SavedViewItem> StartRenameCommand { get; }

    public IRelayCommand<SavedViewItem> CommitRenameCommand { get; }

    public IRelayCommand<SavedViewItem> CancelRenameCommand { get; }

    private void Reload() => Items = _state.Settings.SavedViews.Select(v => new SavedViewItem(v.Name, v.QueryText)).ToList();

    private void Open(SavedViewItem? item)
    {
        if (item is null) return;
        try
        {
            var query = QueryText.Parse(item.QueryText, _state.NewQuery(), _state.Zone, _state.UtcNow);
            Message = "";
            _navigator.ShowEvents(query);
        }
        catch (QueryTextException ex)
        {
            Message = $"This saved view could not be opened: {ex.Message}";
        }
    }

    private void Delete(SavedViewItem? item)
    {
        if (item is null) return;
        Save(s => s with { SavedViews = s.SavedViews.Where(v => v.Name != item.Name).ToList() });
    }

    private void Rename(SavedViewItem? item)
    {
        if (item is null) return;

        var name = item.EditName.Trim();
        if (name.Length == 0)
        {
            Message = "A saved view needs a name.";
            return;
        }
        if (!string.Equals(name, item.Name, StringComparison.OrdinalIgnoreCase)
            && _state.Settings.SavedViews.Any(v => string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            Message = "A saved view with that name already exists.";
            return;
        }

        Save(s => s with
        {
            SavedViews = s.SavedViews.Select(v => v.Name == item.Name ? new SavedView(name, v.QueryText) : v).ToList(),
        });
    }

    private void Save(Func<AppSettings, AppSettings> change)
    {
        _state.Update(change);
        Message = _state.LastSaveFailed ? "Your changes could not be saved. They apply for this session only." : "";
    }
}
