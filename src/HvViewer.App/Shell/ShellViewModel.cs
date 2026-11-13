using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HvViewer.App.Services;
using HvViewer.Core.Model;
using HvViewer.Core.Settings;

namespace HvViewer.App.Shell;

/// <summary>The window around the screens: navigation, the top bar and the status bar.</summary>
public sealed partial class ShellViewModel : ObservableObject, INavigator
{
    private readonly AppState _state;
    private readonly Func<string, ObservableObject> _pageFactory;
    private readonly Dictionary<string, ObservableObject> _pages = new();
    private readonly Action? _restartAsAdmin;

    public ShellViewModel(AppState state, string machineName, bool isAdmin, Func<string, ObservableObject> pageFactory, Action? restartAsAdmin = null)
    {
        _state = state;
        _pageFactory = pageFactory;
        MachineName = machineName;
        IsAdmin = isAdmin;
        _restartAsAdmin = restartAsAdmin;
        RestartAsAdminCommand = new RelayCommand(() => _restartAsAdmin?.Invoke());

        NavItems =
        [
            new NavItem("home", "Home", ""),
            new NavItem("events", "Events", ""),
            new NavItem("saved", "Saved", ""),
            new NavItem("settings", "Settings", ""),
        ];

        NavigateCommand = new RelayCommand<string>(Show);
        SubmitSearchCommand = new RelayCommand(SubmitSearch);
        _state.Changed += OnSettingsChanged;
        Show("home");
    }

    public string MachineName { get; }

    public bool IsAdmin { get; }

    /// <summary>Shown only to a standard user, when the app knows how to relaunch itself elevated.</summary>
    public bool CanRestartAsAdmin => !IsAdmin && _restartAsAdmin is not null;

    public IRelayCommand RestartAsAdminCommand { get; }

    public string AccessBadge => IsAdmin ? "Administrator" : "Standard user";

    public IReadOnlyList<NavItem> NavItems { get; }

    public IRelayCommand<string> NavigateCommand { get; }

    public IRelayCommand SubmitSearchCommand { get; }

    [ObservableProperty]
    private string _currentKey = "";

    [ObservableProperty]
    private ObservableObject? _currentPage;

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private string _footprint = "";

    public TimeMode Time
    {
        get => _state.Settings.Time;
        set
        {
            if (_state.Settings.Time != value) _state.Update(s => s with { Time = value });
        }
    }

    public ThemeMode Theme
    {
        get => _state.Settings.Theme;
        set
        {
            if (_state.Settings.Theme != value) _state.Update(s => s with { Theme = value });
        }
    }


    // The Night / Day / Auto switch in the top bar (the same setting as in Settings).
    public bool IsThemeAuto { get => Theme == ThemeMode.System; set { if (value) Theme = ThemeMode.System; } }

    public bool IsThemeNight { get => Theme == ThemeMode.Dark; set { if (value) Theme = ThemeMode.Dark; } }

    public bool IsThemeDay { get => Theme == ThemeMode.Light; set { if (value) Theme = ThemeMode.Light; } }

    public void Show(string? key)
    {
        if (key is null || !NavItems.Any(i => i.Key == key)) return;

        if (!_pages.TryGetValue(key, out var page)) _pages[key] = page = _pageFactory(key);
        CurrentKey = key;
        CurrentPage = page;
        foreach (var item in NavItems) item.IsSelected = item.Key == key;
    }

    public void ShowEvents(EventQuery query)
    {
        Show("events");
        if (CurrentPage is IQueryTarget target) target.LoadQuery(query);
    }

    private void SubmitSearch()
    {
        if (string.IsNullOrWhiteSpace(SearchText)) return;
        ShowEvents(_state.NewQuery(SearchText));
    }

    private void OnSettingsChanged()
    {
        OnPropertyChanged(nameof(Time));
        OnPropertyChanged(nameof(Theme));
        OnPropertyChanged(nameof(IsThemeAuto));
        OnPropertyChanged(nameof(IsThemeNight));
        OnPropertyChanged(nameof(IsThemeDay));
    }
}
