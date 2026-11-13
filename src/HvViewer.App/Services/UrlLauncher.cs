using System.ComponentModel;
using System.Diagnostics;

namespace HvViewer.App.Services;

/// <summary>Opens a documentation link in the default browser. Only Microsoft Learn links are allowed.</summary>
public static class UrlLauncher
{
    private const string AllowedHost = "learn.microsoft.com";

    public static bool IsAllowed(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && string.Equals(uri.Host, AllowedHost, StringComparison.OrdinalIgnoreCase);

    public static bool TryOpen(string url)
    {
        if (!IsAllowed(url)) return false;
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return true;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }
}
