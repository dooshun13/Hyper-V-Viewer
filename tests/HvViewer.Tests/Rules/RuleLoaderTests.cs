using System.Text;
using HvViewer.Core.Model;
using HvViewer.Rules;
using Xunit;

namespace HvViewer.Tests.Rules;

public class RuleLoaderTests
{
    private const string GoodEvent = """
        { "provider": "disk", "id": 153, "log": "System", "severity": "warning",
          "title": "Disk retried an I/O", "meaning": "m", "causes": "c", "next": "n",
          "docs": "https://learn.microsoft.com/search/?terms=disk%20event%20153" }
        """;

    private static string File(string id = "disk-storage", int schema = 1, string title = "Disk & storage", string summary = "Disks",
        params string[] events) =>
        $$"""
        { "schema": {{schema}}, "id": "{{id}}", "title": "{{title}}", "summary": "{{summary}}",
          "events": [ {{string.Join(",", events.Length == 0 ? [GoodEvent] : events)}} ] }
        """;

    private static string Event(string provider = "disk", int id = 153, string log = "System", string severity = "warning",
        string title = "t", string docs = "https://learn.microsoft.com/x", string next = "n") =>
        $$"""
        { "provider": "{{provider}}", "id": {{id}}, "log": "{{log}}", "severity": "{{severity}}",
          "title": "{{title}}", "meaning": "m", "causes": "c", "next": "{{next}}", "docs": "{{docs}}" }
        """;

    private static RuleSet Load(params (string Source, string Json)[] files) =>
        RuleLoader.Load(files.Select(f => (f.Source, (Stream)new MemoryStream(Encoding.UTF8.GetBytes(f.Json)))));

    private static EventRecordLite Rec(string log, int id, string provider, string? source = null) =>
        new(log, 1, DateTime.UtcNow, id, EventLevel.Warning, provider, source, "PC", 0, 0);

    [Fact] public void ValidFile_Loads()
    {
        var set = Load(("disk.json", File()));
        var t = Assert.Single(set.Troubleshooters);
        Assert.Equal("disk-storage", t.Id);
        Assert.Equal("Disk & storage", t.Title);
        var e = Assert.Single(t.Events);
        Assert.Equal((153, RuleSeverity.Warning, "System"), (e.Id, e.Severity, e.Log));
        Assert.Empty(set.Errors);
        Assert.Equal(["System"], t.Logs);
    }

    [Fact] public void Logs_AreDerivedFromEventsAndDistinct()
    {
        var set = Load(("a.json", File(events: [Event(), Event(id: 154), Event(log: "Application", id: 1000)])));
        Assert.Equal(["System", "Application"], set.Troubleshooters[0].Logs);
        Assert.Equal(["System", "Application"], set.AllLogs);
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("id-format")]
    [InlineData("empty-title")]
    [InlineData("empty-summary")]
    [InlineData("no-events")]
    [InlineData("event-no-provider")]
    [InlineData("event-id-zero")]
    [InlineData("event-no-log")]
    [InlineData("event-no-title")]
    [InlineData("event-no-next")]
    [InlineData("event-docs")]
    [InlineData("event-severity")]
    [InlineData("not-json")]
    public void InvalidFile_IsSkippedWithErrorNamingTheSource(string kind)
    {
        var json = kind switch
        {
            "schema" => File(schema: 2),
            "id-format" => File(id: "Bad Id"),
            "empty-title" => File(title: ""),
            "empty-summary" => File(summary: ""),
            "no-events" => """{ "schema": 1, "id": "x", "title": "t", "summary": "s", "events": [] }""",
            "event-no-provider" => File(events: [Event(provider: "")]),
            "event-id-zero" => File(events: [Event(id: 0)]),
            "event-no-log" => File(events: [Event(log: "")]),
            "event-no-title" => File(events: [Event(title: "")]),
            "event-no-next" => File(events: [Event(next: "")]),
            "event-docs" => File(events: [Event(docs: "https://example.com/page")]),
            "event-severity" => File(events: [Event(severity: "scary")]),
            _ => "{ not json",
        };

        var set = Load(("bad.json", json));

        Assert.Empty(set.Troubleshooters);
        var error = Assert.Single(set.Errors);
        Assert.Equal("bad.json", error.Source);
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
    }

    [Fact] public void BadFile_DoesNotStopOthers()
    {
        var set = Load(("bad.json", "{ nope"), ("good.json", File()));
        Assert.Single(set.Troubleshooters);
        Assert.Single(set.Errors);
    }

    [Fact] public void DuplicateTroubleshooterId_KeepsFirst()
    {
        var set = Load(("a.json", File(title: "First")), ("b.json", File(title: "Second", events: [Event(id: 999)])));
        Assert.Equal("First", Assert.Single(set.Troubleshooters).Title);
        Assert.Equal("b.json", Assert.Single(set.Errors).Source);
    }

    [Fact] public void DuplicateEventAcrossFiles_KeepsFirst()
    {
        var set = Load(("a.json", File(id: "one", events: [Event()])), ("b.json", File(id: "two", events: [Event(), Event(id: 154)])));

        Assert.Equal(2, set.Troubleshooters.Count);
        Assert.Equal([154], set.Troubleshooters[1].Events.Select(e => e.Id));
        Assert.Equal("b.json", Assert.Single(set.Errors).Source);
    }

    [Fact] public void FileWhoseEventsAreAllDuplicates_IsSkipped()
    {
        var set = Load(("a.json", File(id: "one", events: [Event()])), ("b.json", File(id: "two", events: [Event()])));
        Assert.Single(set.Troubleshooters);
        Assert.Single(set.Errors);
    }

    [Fact] public void Match_FindsRuleByProviderIdAndLog()
    {
        var set = Load(("a.json", File()));
        var (t, e) = set.Match(Rec("System", 153, "disk"))!.Value;
        Assert.Equal("disk-storage", t.Id);
        Assert.Equal(153, e.Id);
    }

    [Fact] public void Match_IgnoresCase()
    {
        var set = Load(("a.json", File()));
        Assert.NotNull(set.Match(Rec("SYSTEM", 153, "DISK")));
    }

    [Fact] public void Match_FindsRuleByEventSourceName()
    {
        var set = Load(("a.json", File(events: [Event(provider: "BugCheck", id: 1001)])));
        Assert.NotNull(set.Match(Rec("System", 1001, "Microsoft-Windows-WER-SystemErrorReporting", source: "BugCheck")));
    }

    [Fact] public void Match_UnknownId_IsNull() => Assert.Null(Load(("a.json", File())).Match(Rec("System", 9999, "disk")));

    [Fact] public void Match_WrongLog_IsNull() => Assert.Null(Load(("a.json", File())).Match(Rec("Application", 153, "disk")));

    [Fact] public void Match_WrongProvider_IsNull() => Assert.Null(Load(("a.json", File())).Match(Rec("System", 153, "other")));

    [Fact] public void EventsForLog_IsCaseInsensitive()
    {
        var set = Load(("a.json", File()));
        Assert.Single(set.EventsForLog("system"));
        Assert.Empty(set.EventsForLog("Application"));
    }

    [Fact] public void LoadBuiltIn_NeverThrows()
    {
        var set = RuleLoader.LoadBuiltIn();
        Assert.NotNull(set);
    }
}
