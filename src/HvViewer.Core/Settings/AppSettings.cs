namespace HvViewer.Core.Settings;

public enum ThemeMode { System, Dark, Light }

/// <summary>Whether times are shown in this PC's time zone or in UTC.</summary>
public enum TimeMode { Local, Utc }

/// <summary>A search the user saved: a name and its one-line query text.</summary>
public sealed record SavedView(string Name, string QueryText);

public sealed record AppSettings
{
    public ThemeMode Theme { get; init; } = ThemeMode.System;

    public TimeMode Time { get; init; } = TimeMode.Local;

    /// <summary>The dashboard period: "1h", "24h" or "7d".</summary>
    public string DefaultRange { get; init; } = "24h";

    public IReadOnlyList<SavedView> SavedViews { get; init; } = [];
}
