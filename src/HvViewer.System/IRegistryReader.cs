namespace HvViewer.Machine;

/// <summary>Read-only access to HKEY_LOCAL_MACHINE. Paths are relative to it, with backslashes.</summary>
public interface IRegistryReader
{
    object? GetValue(string hklmPath, string name);

    bool KeyExists(string hklmPath);

    IReadOnlyList<string> GetSubKeyNames(string hklmPath);
}
