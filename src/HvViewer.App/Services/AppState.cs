using System.Globalization;
using HvViewer.Core.Model;
using HvViewer.Core.Settings;

namespace HvViewer.App.Services;

/// <summary>The user's settings plus the defaults derived from them. Saves on every change.</summary>
public sealed class AppState
{
    private readonly SettingsStore _store;
    private readonly bool _isAdmin;
    private readonly Func<DateTime> _utcNow;

    public AppState(SettingsStore store, bool isAdmin, Func<DateTime> utcNow)
    {
        _store = store;
        _isAdmin = isAdmin;
        _utcNow = utcNow;
        Settings = store.Load();
    }

    public AppSettings Settings { get; private set; }

    /// <summary>True when the last save to disk failed (the change still applies for this session).</summary>
    public bool LastSaveFailed { get; private set; }

    public event Action? Changed;

    public void Update(Func<AppSettings, AppSettings> change)
    {
        Settings = change(Settings);
        LastSaveFailed = !_store.Save(Settings);
        Changed?.Invoke();
    }

    /// <summary>The zone times are shown in.</summary>
    public TimeZoneInfo Zone => Settings.Time == TimeMode.Utc ? TimeZoneInfo.Utc : TimeZoneInfo.Local;

    public string FormatTime(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    /// <summary>Security is only readable as administrator, so a standard user does not get it by default.</summary>
    public IReadOnlyList<string> DefaultLogs => _isAdmin ? ["System", "Application", "Security"] : ["System", "Application"];

    public DateTime UtcNow => _utcNow();

    public TimeRange RangeFor(string key) => TimeRange.Last(SpanFor(key), _utcNow());

    public static TimeSpan SpanFor(string key) => key switch
    {
        "1h" => TimeSpan.FromHours(1),
        "7d" => TimeSpan.FromDays(7),
        _ => TimeSpan.FromHours(24),
    };

    /// <summary>A fresh query with the default logs and the default period.</summary>
    public EventQuery NewQuery(string? text = null) => new()
    {
        Text = string.IsNullOrWhiteSpace(text) ? null : text.Trim(),
        Logs = DefaultLogs,
        Range = RangeFor(Settings.DefaultRange),
        LastSpan = SpanFor(Settings.DefaultRange),
    };
}
