using System.Windows;
using System.Windows.Media;
using ThemeMode = HvViewer.Core.Settings.ThemeMode;
using Microsoft.Win32;
using Wpf.Ui.Appearance;

namespace HvViewer.App.Theme;

/// <summary>Applies the dark or light colours to the whole app.</summary>
public static class ThemeService
{
    public static void Apply(ThemeMode mode)
    {
        var dark = mode switch
        {
            ThemeMode.Dark => true,
            ThemeMode.Light => false,
            _ => WindowsPrefersDark(),
        };

        ApplicationThemeManager.Apply(dark ? ApplicationTheme.Dark : ApplicationTheme.Light);

        // Xdisk's accent: neon cyan at night, a darker cyan by day. WPF-UI's own primary buttons follow it.
        ApplicationAccentColorManager.Apply(dark ? Color.FromRgb(0x00, 0xE5, 0xFF) : Color.FromRgb(0x00, 0x91, 0xA8),
            dark ? ApplicationTheme.Dark : ApplicationTheme.Light);

        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var old = dictionaries.FirstOrDefault(d => d.Source?.OriginalString.Contains("Tokens.", StringComparison.Ordinal) == true);
        if (old is not null) dictionaries.Remove(old);
        var uri = new Uri($"/HvViewer;component/Theme/Tokens.{(dark ? "Dark" : "Light")}.xaml", UriKind.Relative);
        dictionaries.Add(new ResourceDictionary { Source = uri });
    }

    /// <summary>Reads the Windows "app mode" setting. Dark when it cannot be read.</summary>
    private static bool WindowsPrefersDark()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", writable: false);
        return key?.GetValue("AppsUseLightTheme") is not 1;
    }
}
