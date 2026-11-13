using System.Text.Json;
using System.Text.Json.Serialization;

namespace HvViewer.Core.Settings;

/// <summary>
/// Reads and writes <c>HvViewer.settings.json</c>, the only file HvViewer keeps (besides exports the user saves).
/// Loading never creates anything; a missing or damaged file gives the defaults.
/// </summary>
public sealed class SettingsStore(string directory)
{
    public const string FileName = "HvViewer.settings.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>%AppData%\HvViewer</summary>
    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HvViewer");

    private string FilePath => Path.Combine(directory, FileName);

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new AppSettings();
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new AppSettings(); // a damaged file is not overwritten until the next save
        }
    }

    /// <returns>True when saved; false when the folder or file could not be written.</returns>
    public bool Save(AppSettings settings)
    {
        var temp = FilePath + ".tmp";
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(temp, FilePath, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDelete(temp);
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
