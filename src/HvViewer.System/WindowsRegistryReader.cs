using Microsoft.Win32;

namespace HvViewer.Machine;

/// <summary>Reads the 64-bit view of HKEY_LOCAL_MACHINE. Keys are opened read-only; nothing is ever written.</summary>
[global::System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class WindowsRegistryReader : IRegistryReader
{
    public object? GetValue(string hklmPath, string name)
    {
        using var key = Open(hklmPath);
        return key?.GetValue(name);
    }

    public bool KeyExists(string hklmPath)
    {
        using var key = Open(hklmPath);
        return key is not null;
    }

    public IReadOnlyList<string> GetSubKeyNames(string hklmPath)
    {
        using var key = Open(hklmPath);
        return key?.GetSubKeyNames() ?? [];
    }

    private static RegistryKey? Open(string path)
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            return root.OpenSubKey(path, writable: false);
        }
        catch (Exception ex) when (ex is global::System.Security.SecurityException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
