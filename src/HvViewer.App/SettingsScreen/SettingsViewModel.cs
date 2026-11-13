using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using HvViewer.App.Services;
using HvViewer.Core.Settings;
using HvViewer.Rules;

namespace HvViewer.App.SettingsScreen;

public sealed record RangeChoice(string Key, string Label);

/// <summary>The Settings screen: theme, time zone, default period, and About.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private static readonly string[] KnownRanges = ["1h", "24h", "7d"];

    private readonly AppState _state;
    private readonly RuleSet _rules;
    private readonly Func<TimeSpan?> _startupTime;

    public SettingsViewModel(AppState state, RuleSet rules, Func<TimeSpan?> startupTime, string settingsPath, string version)
    {
        _state = state;
        _rules = rules;
        _startupTime = startupTime;
        SettingsPath = settingsPath;
        Version = version;
        _state.Changed += OnChanged;
    }

    public string Version { get; }

    public string SettingsPath { get; }

    public string ReadOnlyStatement => "Hyper-V Viewer only reads. It never changes logs, the registry, services or settings.";

    public IReadOnlyList<RangeChoice> DefaultRangeOptions { get; } =
        [new("1h", "Last hour"), new("24h", "Last 24 hours"), new("7d", "Last 7 days")];

    // ---- theme ----

    public ThemeMode Theme
    {
        get => _state.Settings.Theme;
        set
        {
            if (_state.Settings.Theme != value) _state.Update(s => s with { Theme = value });
        }
    }

    public bool IsThemeSystem { get => Theme == ThemeMode.System; set { if (value) Theme = ThemeMode.System; } }

    public bool IsThemeDark { get => Theme == ThemeMode.Dark; set { if (value) Theme = ThemeMode.Dark; } }

    public bool IsThemeLight { get => Theme == ThemeMode.Light; set { if (value) Theme = ThemeMode.Light; } }

    // ---- time ----

    public TimeMode Time
    {
        get => _state.Settings.Time;
        set
        {
            if (_state.Settings.Time != value) _state.Update(s => s with { Time = value });
        }
    }

    public bool IsTimeLocal { get => Time == TimeMode.Local; set { if (value) Time = TimeMode.Local; } }

    public bool IsTimeUtc { get => Time == TimeMode.Utc; set { if (value) Time = TimeMode.Utc; } }

    // ---- default period ----

    public string DefaultRange
    {
        get => _state.Settings.DefaultRange;
        set
        {
            var key = KnownRanges.Contains(value) ? value : "24h";
            if (_state.Settings.DefaultRange != key) _state.Update(s => s with { DefaultRange = key });
        }
    }

    // ---- about ----

    public string StartupText =>
        _startupTime() is { } t ? string.Create(CultureInfo.InvariantCulture, $"Opened in {t.TotalSeconds:0.0} s") : "—";

    public IReadOnlyList<string> RuleErrors => _rules.Errors.Select(e => $"{e.Source}: {e.Message}").ToList();

    public string RulesText => _rules.Errors.Count > 0
        ? $"{_rules.Errors.Count} rule {(_rules.Errors.Count == 1 ? "file was" : "files were")} skipped"
        : $"{_rules.Troubleshooters.Count} troubleshooters loaded";

    public string SaveMessage => _state.LastSaveFailed
        ? $"Your settings could not be saved to {SettingsPath}. They apply for this session only."
        : "";

    /// <summary>Called when the screen is shown, so the start-up time and anything changed elsewhere are current.</summary>
    public void RefreshAbout()
    {
        OnPropertyChanged(nameof(StartupText));
        OnChanged();
    }

    private void OnChanged()
    {
        OnPropertyChanged(nameof(Theme));
        OnPropertyChanged(nameof(IsThemeSystem));
        OnPropertyChanged(nameof(IsThemeDark));
        OnPropertyChanged(nameof(IsThemeLight));
        OnPropertyChanged(nameof(Time));
        OnPropertyChanged(nameof(IsTimeLocal));
        OnPropertyChanged(nameof(IsTimeUtc));
        OnPropertyChanged(nameof(DefaultRange));
        OnPropertyChanged(nameof(SaveMessage));
    }
}
