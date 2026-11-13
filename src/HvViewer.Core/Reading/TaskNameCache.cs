using System.Collections.Concurrent;
using System.Runtime.Versioning;
using Reader = System.Diagnostics.Eventing.Reader;

namespace HvViewer.Core.Reading;

/// <summary>Looks up task category names from a provider's metadata. Results are cached in memory.</summary>
[SupportedOSPlatform("windows")]
internal sealed class TaskNameCache
{
    private readonly ConcurrentDictionary<string, IReadOnlyDictionary<int, string>> _tasks = new(StringComparer.OrdinalIgnoreCase);

    public string? Get(string provider, int task)
    {
        var names = _tasks.GetOrAdd(provider, Load);
        return names.TryGetValue(task, out var value) ? value : null;
    }

    private static IReadOnlyDictionary<int, string> Load(string provider)
    {
        var result = new Dictionary<int, string>();
        try
        {
            using var metadata = new Reader.ProviderMetadata(provider);
            foreach (var t in metadata.Tasks)
            {
                var name = string.IsNullOrWhiteSpace(t.DisplayName) ? t.Name : t.DisplayName;
                if (!string.IsNullOrWhiteSpace(name)) result[t.Value] = name;
            }
        }
        catch (Reader.EventLogException) { }
        catch (UnauthorizedAccessException) { }
        return result;
    }
}
