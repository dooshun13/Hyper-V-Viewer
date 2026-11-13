using HvViewer.Machine;

namespace HvViewer.Tests.Fakes;

/// <summary>An in-memory registry for tests. Paths are HKLM-relative and case-insensitive.</summary>
public sealed class FakeRegistry : IRegistryReader
{
    private readonly Dictionary<string, Dictionary<string, object?>> _keys = new(StringComparer.OrdinalIgnoreCase);

    public FakeRegistry Key(string path)
    {
        if (!_keys.ContainsKey(path)) _keys[path] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        return this;
    }

    public FakeRegistry Set(string path, string name, object? value)
    {
        Key(path);
        _keys[path][name] = value;
        return this;
    }

    public object? GetValue(string hklmPath, string name) =>
        _keys.TryGetValue(hklmPath, out var values) && values.TryGetValue(name, out var v) ? v : null;

    public bool KeyExists(string hklmPath) =>
        _keys.ContainsKey(hklmPath) || _keys.Keys.Any(k => k.StartsWith(hklmPath + "\\", StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<string> GetSubKeyNames(string hklmPath)
    {
        var prefix = hklmPath + "\\";
        return _keys.Keys
            .Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(k => k[prefix.Length..].Split('\\')[0])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
