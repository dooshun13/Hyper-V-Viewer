using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace HvViewer.Rules;

/// <summary>Loads and validates troubleshooter rule files. A bad file is skipped and reported; loading never throws.</summary>
public static partial class RuleLoader
{
    private const string ResourcePrefix = "Troubleshooters/";
    private const string DocsPrefix = "https://learn.microsoft.com/";

    /// <summary>Display order of the built-in troubleshooters (also the order of the dashboard tiles).</summary>
    private static readonly string[] BuiltInOrder =
    [
        "boot-shutdown", "crashes-hangs", "disk-storage", "logons-accounts", "windows-update", "services",
        "memory-performance", "network-dns", "group-policy", "rdp", "printing",
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    [GeneratedRegex("^[a-z0-9-]+$")]
    private static partial Regex IdFormat();

    public static RuleSet LoadBuiltIn()
    {
        var assembly = typeof(RuleLoader).Assembly;
        var names = assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal))
            .OrderBy(n => Rank(Path.GetFileNameWithoutExtension(n)))
            .ThenBy(n => n, StringComparer.Ordinal)
            .ToList();

        var streams = new List<(string, Stream)>();
        try
        {
            foreach (var name in names)
                streams.Add((name[ResourcePrefix.Length..], assembly.GetManifestResourceStream(name)!));
            return Load(streams);
        }
        finally
        {
            foreach (var (_, stream) in streams) stream.Dispose();
        }
    }

    private static int Rank(string id)
    {
        var index = Array.IndexOf(BuiltInOrder, id);
        return index < 0 ? int.MaxValue : index;
    }

    public static RuleSet Load(IEnumerable<(string Source, Stream Json)> files)
    {
        var troubleshooters = new List<Troubleshooter>();
        var errors = new List<RuleLoadError>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var seenEvents = new HashSet<(string Provider, int Id, string Log)>();

        foreach (var (source, json) in files)
        {
            var parsed = Parse(source, json, errors);
            if (parsed is null) continue;

            if (!ids.Add(parsed.Id))
            {
                errors.Add(new RuleLoadError(source, $"Duplicate troubleshooter id '{parsed.Id}'; the first one was kept."));
                continue;
            }

            var kept = new List<RuleEvent>();
            var duplicates = new List<RuleLoadError>();
            foreach (var rule in parsed.Events)
            {
                var key = (rule.Provider.ToLowerInvariant(), rule.Id, rule.Log.ToLowerInvariant());
                if (seenEvents.Add(key)) kept.Add(rule);
                else duplicates.Add(new RuleLoadError(source, $"Duplicate rule {rule.Provider} {rule.Id} in {rule.Log}; the first one was kept."));
            }

            if (kept.Count == 0)
            {
                ids.Remove(parsed.Id);
                errors.Add(new RuleLoadError(source, "Every rule in this file is already defined in an earlier file, so the file was skipped."));
                continue;
            }
            errors.AddRange(duplicates);

            troubleshooters.Add(new Troubleshooter(parsed.Schema, parsed.Id, parsed.Title, parsed.Summary, kept));
        }

        return new RuleSet(troubleshooters, errors);
    }

    /// <summary>Parses and validates one file. Returns null (and records an error) when it is invalid.</summary>
    private static Troubleshooter? Parse(string source, Stream json, List<RuleLoadError> errors)
    {
        FileDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<FileDto>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            errors.Add(new RuleLoadError(source, "Invalid JSON: " + ex.Message));
            return null;
        }

        var problem = Validate(dto);
        if (problem is not null)
        {
            errors.Add(new RuleLoadError(source, problem));
            return null;
        }

        var events = dto!.Events!.Select(e => new RuleEvent(
            e.Provider!, e.Id!.Value, e.Log!, e.Severity!.Value, e.Title!, e.Meaning!, e.Causes!, e.Next!, e.Docs!)).ToList();
        return new Troubleshooter(dto.Schema!.Value, dto.Id!, dto.Title!, dto.Summary!, events);
    }

    private static string? Validate(FileDto? dto)
    {
        if (dto is null) return "The file is empty.";
        if (dto.Schema != 1) return $"Unsupported schema '{dto.Schema?.ToString() ?? "missing"}'; expected 1.";
        if (string.IsNullOrEmpty(dto.Id) || !IdFormat().IsMatch(dto.Id)) return "The id must use only a-z, 0-9 and '-'.";
        if (string.IsNullOrWhiteSpace(dto.Title)) return "The title is missing.";
        if (string.IsNullOrWhiteSpace(dto.Summary)) return "The summary is missing.";
        if (dto.Events is not { Count: > 0 }) return "The file has no events.";

        for (var i = 0; i < dto.Events.Count; i++)
        {
            var e = dto.Events[i];
            var where = $"Event #{i + 1}";
            if (string.IsNullOrWhiteSpace(e.Provider)) return $"{where}: provider is missing.";
            if (e.Id is not > 0) return $"{where}: id must be greater than 0.";
            if (string.IsNullOrWhiteSpace(e.Log)) return $"{where}: log is missing.";
            if (e.Severity is null) return $"{where}: severity is missing.";
            if (string.IsNullOrWhiteSpace(e.Title)) return $"{where}: title is missing.";
            if (string.IsNullOrWhiteSpace(e.Meaning)) return $"{where}: meaning is missing.";
            if (string.IsNullOrWhiteSpace(e.Causes)) return $"{where}: causes is missing.";
            if (string.IsNullOrWhiteSpace(e.Next)) return $"{where}: next is missing.";
            if (e.Docs is null || !e.Docs.StartsWith(DocsPrefix, StringComparison.Ordinal)) return $"{where}: docs must start with {DocsPrefix}";
        }
        return null;
    }

    private sealed class FileDto
    {
        public int? Schema { get; set; }
        public string? Id { get; set; }
        public string? Title { get; set; }
        public string? Summary { get; set; }
        public List<EventDto>? Events { get; set; }
    }

    private sealed class EventDto
    {
        public string? Provider { get; set; }
        public int? Id { get; set; }
        public string? Log { get; set; }
        public RuleSeverity? Severity { get; set; }
        public string? Title { get; set; }
        public string? Meaning { get; set; }
        public string? Causes { get; set; }
        public string? Next { get; set; }
        public string? Docs { get; set; }
    }
}
