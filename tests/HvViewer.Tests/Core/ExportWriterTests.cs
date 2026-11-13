using HvViewer.Core.Export;
using Xunit;

namespace HvViewer.Tests.Core;

public class ExportWriterTests
{
    private static ExportRow Row(string message = "Disk 3 was retried.", int count = 1, string source = "disk") =>
        new("2026-11-10 23:41:10", "Warning", 153, source, "System", "PC-01", message, count);

    private static string Csv(params ExportRow[] rows)
    {
        var w = new StringWriter();
        ExportWriter.WriteCsv(w, rows);
        return w.ToString();
    }

    private static string Html(string title, string subtitle, params ExportRow[] rows)
    {
        var w = new StringWriter();
        ExportWriter.WriteHtml(w, title, subtitle, rows);
        return w.ToString();
    }

    private static int Occurrences(string text, string part) =>
        (text.Length - text.Replace(part, "").Length) / part.Length;

    [Fact] public void Csv_Header_IsExact() =>
        Assert.Equal("Time,Level,Event ID,Source,Log,Computer,Message,Count", Csv().Split("\r\n")[0]);

    [Fact] public void Csv_Row_HasFieldsInOrder() =>
        Assert.Equal("2026-11-10 23:41:10,Warning,153,disk,System,PC-01,Disk 3 was retried.,1", Csv(Row()).Split("\r\n")[1]);

    [Fact] public void Csv_QuotesFieldsWithCommasQuotesAndNewlines()
    {
        var text = Csv(Row(message: "a,b"), Row(message: "say \"hi\""), Row(message: "line1\nline2"));
        Assert.Contains(",\"a,b\",", text);
        Assert.Contains(",\"say \"\"hi\"\"\",", text);
        Assert.Contains(",\"line1\nline2\",", text);
    }

    [Fact] public void Csv_PlainFields_AreNotQuoted() => Assert.DoesNotContain("\"", Csv(Row()));

    [Fact] public void Csv_ThreeRows_HaveThreeDataLines()
    {
        var lines = Csv(Row(), Row(), Row()).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(4, lines.Length);
    }

    [Fact] public void Html_EncodesMessageAndTitle()
    {
        var html = Html("<b>Report</b>", "sub", Row(message: "<script>alert(1)</script>"));
        Assert.DoesNotContain("<script", html);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html);
        Assert.DoesNotContain("<b>Report</b>", html);
    }

    [Fact] public void Html_ContainsTitleSubtitleAndOneRowPerEventPlusHeader()
    {
        var html = Html("Disk problems", "Last 24 hours · 3 events", Row(), Row(source: "ntfs"), Row());
        Assert.Contains("Disk problems", html);
        Assert.Contains("Last 24 hours · 3 events", html);
        Assert.Equal(4, Occurrences(html, "<tr"));
    }

    [Fact] public void Html_IsStandalone_NoExternalLinksOrScripts()
    {
        var html = Html("T", "S", Row());
        Assert.StartsWith("<!DOCTYPE html>", html);
        Assert.Contains("<style>", html);
        Assert.DoesNotContain("http://", html);
        Assert.DoesNotContain("https://", html);
        Assert.DoesNotContain("<link", html);
        Assert.DoesNotContain("<script", html);
        Assert.DoesNotContain("src=", html);
    }

    [Fact] public void Html_GroupedRows_ShowCount()
    {
        Assert.Contains("×9", Html("T", "S", Row(count: 9)));
        Assert.DoesNotContain("×1<", Html("T", "S", Row(count: 1)));
    }

    [Fact] public void Html_NewlinesInMessage_BecomeLineBreaks() =>
        Assert.Contains("line1<br>line2", Html("T", "S", Row(message: "line1\nline2")));
}
