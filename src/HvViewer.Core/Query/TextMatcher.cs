using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using HvViewer.Core.Model;

namespace HvViewer.Core.Query;

/// <summary>
/// Matches search text against event text. Supports match case, whole word, regular expressions and
/// ignoring accents. Regular expressions have a 100 ms timeout; a timeout counts as "no match".
/// </summary>
public sealed class TextMatcher
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(100);

    private readonly Regex _regex;
    private readonly bool _ignoreAccents;
    private int _timeoutCount;

    public TextMatcher(string text, TextOptions options)
    {
        _ignoreAccents = options.IgnoreAccents;

        // Plain text is matched in its accent-stripped form when accents are ignored.
        var body = options.Regex ? text : Regex.Escape(_ignoreAccents ? StripAccents(text, out _) : text);
        if (options.WholeWord) body = @"(?<![\p{L}\p{N}_])(?:" + body + @")(?![\p{L}\p{N}_])";

        var flags = RegexOptions.CultureInvariant;
        if (!options.MatchCase) flags |= RegexOptions.IgnoreCase;
        _regex = new Regex(body, flags, Timeout);
    }

    /// <summary>How many times a regular expression gave up because it took too long.</summary>
    public int TimeoutCount => Volatile.Read(ref _timeoutCount);

    public bool IsMatch(string? s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        var map = (int[]?)null;
        var haystack = _ignoreAccents ? StripAccents(s, out map) : s;
        try { return _regex.IsMatch(haystack); }
        catch (RegexMatchTimeoutException) { Interlocked.Increment(ref _timeoutCount); return false; }
    }

    /// <summary>The (start, length) of every non-empty match, as positions in the original string.</summary>
    public IReadOnlyList<(int Start, int Length)> Matches(string? s)
    {
        if (string.IsNullOrEmpty(s)) return [];
        var map = (int[]?)null;
        var haystack = _ignoreAccents ? StripAccents(s, out map) : s;
        var result = new List<(int, int)>();
        try
        {
            foreach (Match m in _regex.Matches(haystack))
            {
                if (m.Length == 0) continue;
                if (map is null) { result.Add((m.Index, m.Length)); continue; }
                var start = map[m.Index];
                var end = map[m.Index + m.Length - 1] + 1;
                result.Add((start, end - start));
            }
        }
        catch (RegexMatchTimeoutException) { Interlocked.Increment(ref _timeoutCount); return []; }
        return result;
    }

    /// <summary>Removes combining marks. <paramref name="map"/> gives, for each output character, its index in the input.</summary>
    private static string StripAccents(string s, out int[] map)
    {
        var sb = new StringBuilder(s.Length);
        var indexes = new List<int>(s.Length);
        for (var i = 0; i < s.Length; i++)
        {
            var decomposed = s[i].ToString().Normalize(NormalizationForm.FormD);
            foreach (var c in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
                sb.Append(c);
                indexes.Add(i);
            }
        }
        map = indexes.ToArray();
        return sb.ToString();
    }
}
