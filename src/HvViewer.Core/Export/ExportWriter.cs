using System.Globalization;

namespace HvViewer.Core.Export;

/// <summary>Writes results as CSV or as a standalone HTML page. It writes to a <see cref="TextWriter"/> it is given; no file handling here.</summary>
public static class ExportWriter
{
    private const string NewLine = "\r\n";

    public static void WriteCsv(TextWriter writer, IEnumerable<ExportRow> rows)
    {
        writer.Write("Time,Level,Event ID,Source,Log,Computer,Message,Count" + NewLine);
        foreach (var r in rows)
        {
            writer.Write(string.Join(",",
                Field(r.Time), Field(r.Level), r.EventId.ToString(CultureInfo.InvariantCulture), Field(r.Source),
                Field(r.Log), Field(r.Computer), Field(r.Message), r.Count.ToString(CultureInfo.InvariantCulture)));
            writer.Write(NewLine);
        }
    }

    private static string Field(string value) =>
        value.IndexOfAny([',', '"', '\n', '\r']) < 0 ? value : "\"" + value.Replace("\"", "\"\"") + "\"";

    public static void WriteHtml(TextWriter writer, string title, string subtitle, IEnumerable<ExportRow> rows)
    {
        writer.WriteLine("<!DOCTYPE html>");
        writer.WriteLine("<html lang=\"en\"><head><meta charset=\"utf-8\">");
        writer.WriteLine($"<title>{Encode(title)}</title>");
        writer.WriteLine("<style>");
        writer.WriteLine(Css);
        writer.WriteLine("</style></head><body>");
        writer.WriteLine($"<h1>{Encode(title)}</h1>");
        writer.WriteLine($"<p class=\"sub\">{Encode(subtitle)}</p>");
        writer.WriteLine("<table>");
        writer.WriteLine("<tr><th>Time</th><th>Level</th><th>Event ID</th><th>Source</th><th>Log</th><th>Computer</th><th>Message</th><th>Count</th></tr>");
        foreach (var r in rows)
        {
            var count = r.Count > 1 ? "×" + r.Count.ToString(CultureInfo.InvariantCulture) : "";
            writer.WriteLine(
                $"<tr><td>{Encode(r.Time)}</td><td class=\"lvl-{Encode(r.Level.ToLowerInvariant())}\">{Encode(r.Level)}</td>"
                + $"<td>{r.EventId.ToString(CultureInfo.InvariantCulture)}</td><td>{Encode(r.Source)}</td><td>{Encode(r.Log)}</td>"
                + $"<td>{Encode(r.Computer)}</td><td>{EncodeMultiline(r.Message)}</td><td>{count}</td></tr>");
        }
        writer.WriteLine("</table></body></html>");
    }

    /// <summary>Escapes the characters HTML treats specially. Other characters stay readable because the page is UTF-8.</summary>
    private static string Encode(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&#39;");

    private static string EncodeMultiline(string text) =>
        Encode(text).Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "<br>");

    private const string Css = """
        body { font-family: Segoe UI, Arial, sans-serif; margin: 24px; color: #16171a; }
        h1 { font-size: 20px; margin: 0 0 4px; }
        .sub { color: #5f6168; margin: 0 0 16px; }
        table { border-collapse: collapse; width: 100%; font-size: 13px; }
        th, td { border: 1px solid #e3e4e8; padding: 6px 8px; text-align: left; vertical-align: top; }
        th { background: #f7f7f8; }
        .lvl-critical, .lvl-error { color: #b42318; font-weight: 600; }
        .lvl-warning { color: #9a5b00; font-weight: 600; }
        """;
}
