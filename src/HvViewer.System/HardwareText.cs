using global::System.Globalization;
using global::System.Text.RegularExpressions;

namespace HvViewer.Machine;

/// <summary>Turns hardware numbers into the short texts shown on the Home page.</summary>
public static partial class HardwareText
{
    private const double BytesPerGb = 1024.0 * 1024 * 1024;

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    /// <summary>"AMD Ryzen 5 5500", without the padding Windows adds or the (R) and (TM) marks.</summary>
    public static string CpuName(string raw)
    {
        var tidy = Spaces().Replace(raw.Replace("(R)", "").Replace("(TM)", "").Replace("(r)", "").Replace("(tm)", ""), " ").Trim();
        return tidy.Length == 0 ? "Unknown processor" : tidy;
    }

    /// <summary>"6 cores · 12 threads · 3.6 GHz". Anything that is not known is left out.</summary>
    public static string CpuDetail(HardwareInfo info)
    {
        var parts = new List<string>();
        if (info.Cores > 0) parts.Add(Plural(info.Cores, "core"));
        if (info.Threads > 0) parts.Add(Plural(info.Threads, "thread"));
        if (info.MaxGHz > 0) parts.Add(info.MaxGHz.ToString("0.#", CultureInfo.InvariantCulture) + " GHz");
        return string.Join(" · ", parts);
    }

    /// <summary>"31.8 GB", "16 GB".</summary>
    public static string Size(long bytes) => (bytes / BytesPerGb).ToString("0.#", CultureInfo.InvariantCulture) + " GB";

    /// <summary>How much of the memory is in use, 0 to 100.</summary>
    public static int UsedPercent(long total, long available)
    {
        if (total <= 0) return 0;
        var used = Math.Clamp(total - available, 0, total);
        return (int)Math.Round(used * 100.0 / total, MidpointRounding.AwayFromZero);
    }

    /// <summary>DDR4, DDR5 and so on, from the SMBIOS memory type number. Empty when unknown.</summary>
    public static string MemoryType(int smbios) => smbios switch
    {
        20 => "DDR",
        21 => "DDR2",
        24 => "DDR3",
        26 => "DDR4",
        29 => "LPDDR3",
        30 => "LPDDR4",
        34 => "DDR5",
        35 => "LPDDR5",
        _ => "",
    };

    /// <summary>"2 × 16 GB · DDR4 · 2133 MHz". Empty when no memory details are known.</summary>
    public static string Modules(IReadOnlyList<MemoryModule> modules)
    {
        if (modules.Count == 0) return "";

        // Group by size, keeping the order the sticks were first seen in.
        var groups = modules.GroupBy(m => m.CapacityBytes).Select(g => (Size: Size(g.Key), Count: g.Count())).ToList();
        var sizes = groups.Count == 1
            ? $"{groups[0].Count} × {groups[0].Size}"
            : string.Join(" + ", groups.Select(g => g.Count == 1 ? g.Size : $"{g.Count} × {g.Size}"));

        var parts = new List<string> { sizes };
        var types = modules.Select(m => m.Type).Where(t => t.Length > 0).Distinct().ToList();
        if (types.Count > 0) parts.Add(string.Join("/", types));
        var speed = modules.Max(m => m.SpeedMHz);
        if (speed > 0) parts.Add($"{speed} MHz");
        return string.Join(" · ", parts);
    }

    private static string Plural(int n, string word) => n == 1 ? $"1 {word}" : $"{n} {word}s";
}
