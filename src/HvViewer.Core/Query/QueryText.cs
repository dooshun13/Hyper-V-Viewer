using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using HvViewer.Core.Model;

namespace HvViewer.Core.Query;

/// <summary>Reads and writes the one-line query text, e.g. <c>"Disk 3" word from:2026-11-10 22:00:00 id:153,-7036 level:&gt;=warning</c>.</summary>
public static class QueryText
{
    public static readonly IReadOnlyList<string> DefaultLogs = ["System", "Application"];

    private const string DateTimeFormat = "yyyy-MM-dd HH:mm:ss";
    private static readonly string[] DateFormats = ["yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-dd"];
    private static readonly string[] Flags = ["case", "word", "regex", "noaccent"];
    private static readonly string[] LevelNames = ["", "critical", "error", "warning", "information", "verbose"];
    private static readonly Regex TimeOnly = new(@"^\d{1,2}:\d{2}(:\d{2})?$", RegexOptions.CultureInvariant);
    private static readonly Regex Span = new(@"^(\d+)([mhd])$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    // ---------- Format ----------

    public static string Format(EventQuery q, TimeZoneInfo zone, IReadOnlyList<string>? defaultLogs = null)
    {
        var parts = new List<string>();

        if (!string.IsNullOrEmpty(q.Text)) parts.Add(NeedsQuotes(q.Text, forText: true) ? Quote(q.Text) : q.Text);

        if (q.TextOptions.MatchCase) parts.Add("case");
        if (q.TextOptions.WholeWord) parts.Add("word");
        if (q.TextOptions.Regex) parts.Add("regex");
        if (q.TextOptions.IgnoreAccents) parts.Add("noaccent");

        if (q.Target != TextTarget.Message) parts.Add("in:" + (q.Target == TextTarget.Xml ? "xml" : "all"));

        if (q.LastSpan is { } span)
        {
            parts.Add("last:" + FormatSpan(span));
        }
        else if (q.Range != default)
        {
            parts.Add("from:" + Local(q.Range.FromUtc, zone));
            parts.Add("to:" + Local(q.Range.ToUtc, zone));
        }

        if (q.IncludeIds.Count + q.ExcludeIds.Count > 0)
            parts.Add("id:" + string.Join(",", q.IncludeIds.Select(i => i.ToString(CultureInfo.InvariantCulture))
                .Concat(q.ExcludeIds.Select(i => "-" + i.ToString(CultureInfo.InvariantCulture)))));

        if (q.Sources.Count > 0) parts.Add("source:" + string.Join(",", q.Sources.Select(ListItem)));

        if (q.Logs.Count > 0 && !q.Logs.SequenceEqual(defaultLogs ?? DefaultLogs))
            parts.Add("log:" + string.Join(",", q.Logs.Select(ListItem)));

        if (q.Levels.Count > 0) parts.Add("level:" + FormatLevels(q.Levels));

        if (q.Tasks.Count > 0)
            parts.Add("task:" + string.Join(",", q.Tasks.Select(t => ListItem($"{t.Provider}/{t.Task.ToString(CultureInfo.InvariantCulture)}"))));

        return string.Join(" ", parts);
    }

    // ---------- Parse ----------

    public static EventQuery Parse(string text, EventQuery defaults, TimeZoneInfo zone, DateTime nowUtc)
    {
        var tokens = Tokenize(text);
        var words = new List<string>();
        var options = new TextOptions();
        var target = TextTarget.Message;
        DateTime? from = null, to = null;
        TimeSpan? last = null;
        var include = new List<int>();
        var exclude = new List<int>();
        var sources = new List<string>();
        List<string>? logs = null;
        var levels = new List<EventLevel>();
        var tasks = new List<TaskFilter>();

        for (var i = 0; i < tokens.Count; i++)
        {
            var (pos, raw) = tokens[i];

            if (raw.StartsWith('"')) { words.Add(Unquote(raw)); continue; }

            var colon = raw.IndexOf(':');
            if (colon > 0 && raw[..colon].All(char.IsLetter))
            {
                var key = raw[..colon].ToLowerInvariant();
                var value = raw[(colon + 1)..];
                switch (key)
                {
                    case "from":
                    case "to":
                        // The date may be followed by a separate time token: from:2026-11-10 22:00:00
                        var dateText = Unquote(value);
                        if (i + 1 < tokens.Count && TimeOnly.IsMatch(tokens[i + 1].Raw)) dateText += " " + tokens[++i].Raw;
                        var utc = ParseLocal(dateText, zone, pos);
                        if (key == "from") from = utc; else to = utc;
                        break;
                    case "last": last = ParseSpan(Unquote(value), pos); break;
                    case "in": target = ParseTarget(Unquote(value), pos); break;
                    case "id": ParseIds(value, include, exclude, pos); break;
                    case "source": sources.AddRange(SplitList(value, pos)); break;
                    case "log": (logs ??= []).AddRange(SplitList(value, pos)); break;
                    case "level": levels.AddRange(ParseLevels(value, pos)); break;
                    case "task": tasks.AddRange(SplitList(value, pos).Select(v => ParseTask(v, pos))); break;
                    default: throw new QueryTextException(pos, $"Unknown filter '{raw[..colon]}'");
                }
                continue;
            }

            switch (raw.ToLowerInvariant())
            {
                case "case": options = options with { MatchCase = true }; break;
                case "word": options = options with { WholeWord = true }; break;
                case "regex": options = options with { Regex = true }; break;
                case "noaccent": options = options with { IgnoreAccents = true }; break;
                default: words.Add(Unquote(raw)); break;
            }
        }

        var range = defaults.Range;
        TimeSpan? lastSpan = defaults.LastSpan;
        if (last is { } span)
        {
            range = TimeRange.Last(span, nowUtc);
            lastSpan = span;
        }
        else if (from is not null || to is not null)
        {
            var end = to ?? nowUtc;
            var start = from ?? (defaults.Range.FromUtc < end ? defaults.Range.FromUtc : end - TimeSpan.FromHours(24));
            range = new TimeRange(start, end);
            lastSpan = null;
        }
        else if (lastSpan is { } keep)
        {
            range = TimeRange.Last(keep, nowUtc);
        }

        return defaults with
        {
            Text = words.Count == 0 ? null : string.Join(" ", words),
            TextOptions = options,
            Target = target,
            Range = range,
            LastSpan = lastSpan,
            IncludeIds = include,
            ExcludeIds = exclude,
            Sources = sources,
            Logs = logs ?? defaults.Logs,
            Levels = levels,
            Tasks = tasks,
        };
    }

    // ---------- Tokenizer ----------

    private static List<(int Pos, string Raw)> Tokenize(string s)
    {
        var tokens = new List<(int, string)>();
        var i = 0;
        while (i < s.Length)
        {
            if (char.IsWhiteSpace(s[i])) { i++; continue; }
            var start = i;
            var inQuote = false;
            while (i < s.Length)
            {
                var c = s[i];
                if (inQuote)
                {
                    if (c == '\\' && i + 1 < s.Length) { i += 2; continue; }
                    if (c == '"') inQuote = false;
                }
                else
                {
                    if (c == '"') inQuote = true;
                    else if (char.IsWhiteSpace(c)) break;
                }
                i++;
            }
            if (inQuote) throw new QueryTextException(start, "Missing closing quote");
            tokens.Add((start, s[start..i]));
        }
        return tokens;
    }

    /// <summary>Removes quotes and resolves backslash escapes inside them.</summary>
    private static string Unquote(string raw)
    {
        if (!raw.Contains('"')) return raw;
        var sb = new StringBuilder();
        var inQuote = false;
        for (var i = 0; i < raw.Length; i++)
        {
            var c = raw[i];
            if (inQuote && c == '\\' && i + 1 < raw.Length) { sb.Append(raw[++i]); continue; }
            if (c == '"') { inQuote = !inQuote; continue; }
            sb.Append(c);
        }
        return sb.ToString();
    }

    private static string Quote(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static bool NeedsQuotes(string s, bool forText) =>
        s.Any(c => char.IsWhiteSpace(c) || c == '"' || c == '\\' || c == ':' || (!forText && c == ','))
        || (forText && Flags.Contains(s, StringComparer.OrdinalIgnoreCase));

    private static string ListItem(string s) => NeedsQuotes(s, forText: false) ? Quote(s) : s;

    /// <summary>Splits a comma list, keeping commas that are inside quotes.</summary>
    private static List<string> SplitList(string value, int pos)
    {
        var items = new List<string>();
        var sb = new StringBuilder();
        var inQuote = false;
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (inQuote && c == '\\' && i + 1 < value.Length) { sb.Append(value[++i]); continue; }
            if (c == '"') { inQuote = !inQuote; continue; }
            if (c == ',' && !inQuote) { Flush(); continue; }
            sb.Append(c);
        }
        Flush();
        return items;

        void Flush()
        {
            var item = sb.ToString().Trim();
            if (item.Length > 0) items.Add(item);
            sb.Clear();
        }
    }

    // ---------- Value parsers ----------

    private static DateTime ParseLocal(string text, TimeZoneInfo zone, int pos)
    {
        if (!DateTime.TryParseExact(text.Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
            throw new QueryTextException(pos, $"Invalid date '{text}'. Use yyyy-MM-dd HH:mm:ss.");
        return ZoneTime.LocalToUtc(local, zone);
    }

    private static TimeSpan ParseSpan(string text, int pos)
    {
        var m = Span.Match(text);
        if (!m.Success) throw new QueryTextException(pos, $"Invalid time span '{text}'. Use 15m, 1h, 24h or 7d.");
        var n = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        return char.ToLowerInvariant(m.Groups[2].Value[0]) switch
        {
            'm' => TimeSpan.FromMinutes(n),
            'h' => TimeSpan.FromHours(n),
            _ => TimeSpan.FromDays(n),
        };
    }

    private static string FormatSpan(TimeSpan span)
    {
        var minutes = (long)span.TotalMinutes;
        if (minutes % 1440 == 0 && minutes >= 2880) return (minutes / 1440).ToString(CultureInfo.InvariantCulture) + "d";
        if (minutes % 60 == 0) return (minutes / 60).ToString(CultureInfo.InvariantCulture) + "h";
        return minutes.ToString(CultureInfo.InvariantCulture) + "m";
    }

    private static TextTarget ParseTarget(string text, int pos) => text.ToLowerInvariant() switch
    {
        "message" => TextTarget.Message,
        "all" => TextTarget.AllFields,
        "xml" => TextTarget.Xml,
        _ => throw new QueryTextException(pos, $"Invalid 'in:' value '{text}'. Use message, all or xml."),
    };

    private static void ParseIds(string value, List<int> include, List<int> exclude, int pos)
    {
        foreach (var item in SplitList(value, pos))
        {
            var negative = item.StartsWith('-');
            if (!int.TryParse(negative ? item[1..] : item, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
                throw new QueryTextException(pos, $"Invalid event ID '{item}'");
            (negative ? exclude : include).Add(id);
        }
    }

    private static IEnumerable<EventLevel> ParseLevels(string value, int pos)
    {
        var text = Unquote(value);
        if (text.StartsWith(">="))
        {
            var top = LevelFromName(text[2..], pos);
            return Enumerable.Range(1, (int)top).Select(n => (EventLevel)n);
        }
        return SplitList(text, pos).Select(n => LevelFromName(n, pos)).ToList();
    }

    private static EventLevel LevelFromName(string name, int pos)
    {
        var index = Array.IndexOf(LevelNames, name.Trim().ToLowerInvariant());
        if (index < 1) throw new QueryTextException(pos, $"Unknown level '{name}'");
        return (EventLevel)index;
    }

    private static string FormatLevels(IReadOnlyList<EventLevel> levels)
    {
        var contiguousFromCritical = levels.Select(l => (int)l).SequenceEqual(Enumerable.Range(1, levels.Count));
        return contiguousFromCritical && levels.Count > 1
            ? ">=" + LevelNames[(int)levels[^1]]
            : string.Join(",", levels.Select(l => LevelNames[(int)l]));
    }

    private static TaskFilter ParseTask(string value, int pos)
    {
        var slash = value.LastIndexOf('/');
        if (slash <= 0 || !int.TryParse(value[(slash + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var task))
            throw new QueryTextException(pos, $"Invalid task '{value}'. Use provider/number.");
        return new TaskFilter(value[..slash], task);
    }

    private static string Local(DateTime utc, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone).ToString(DateTimeFormat, CultureInfo.InvariantCulture);
}
