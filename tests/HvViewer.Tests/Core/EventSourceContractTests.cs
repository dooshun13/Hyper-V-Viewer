using HvViewer.Core.Model;
using HvViewer.Core.Query;
using HvViewer.Core.Reading;
using Xunit;

namespace HvViewer.Tests.Core;

/// <summary>Behaviour every <see cref="IEventSource"/> must have. Runs against the live logs of this PC.</summary>
public abstract class EventSourceContractTests
{
    protected abstract IEventSource Create();

    private static readonly LogTarget System = LogTarget.Channel("System");

    private static string Last30Days()
    {
        var now = DateTime.UtcNow;
        return XPathBuilder.Build(new EventQuery
        {
            Range = TimeRange.Last(TimeSpan.FromDays(30), now),
            Logs = ["System"],
        }).XPath;
    }

    [WindowsFact]
    public void Query_ReturnsNewestFirst_AndWithinRange()
    {
        var source = Create();
        var now = DateTime.UtcNow;
        var xpath = XPathBuilder.Build(new EventQuery { Range = TimeRange.Last(TimeSpan.FromDays(30), now), Logs = ["System"] }).XPath;

        var events = source.Query(System, xpath, CancellationToken.None).Take(200).ToList();

        Assert.NotEmpty(events);
        // Logs are ordered by record number. Timestamps can be slightly out of order in real logs
        // (for example when a component back-dates an event), so newest-first means descending record IDs.
        Assert.Equal(events.OrderByDescending(e => e.RecordId).Select(e => e.RecordId), events.Select(e => e.RecordId));
        Assert.All(events, e =>
        {
            Assert.True(e.TimeCreatedUtc >= now.AddDays(-30).AddMinutes(-1));
            Assert.Equal("System", e.LogName, ignoreCase: true);
            Assert.False(string.IsNullOrEmpty(e.ProviderName));
        });
    }

    [WindowsFact]
    public void Query_UnknownLog_ThrowsLogNotFound()
    {
        var source = Create();
        Assert.Throws<LogNotFoundException>(() =>
            source.Query(LogTarget.Channel("HvViewer-Does-Not-Exist"), Last30Days(), CancellationToken.None).ToList());
    }

    [WindowsFact]
    public void Query_Cancelled_StopsEnumeration()
    {
        var source = Create();
        using var cts = new CancellationTokenSource();
        var count = 0;
        Assert.Throws<OperationCanceledException>(() =>
        {
            foreach (var _ in source.Query(System, Last30Days(), cts.Token))
            {
                count++;
                cts.Cancel();
            }
        });
        Assert.True(count >= 1);
    }

    [WindowsFact]
    public void FormatMessage_UnknownPublisher_ReturnsNull()
    {
        var sample = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "samples", "no-publisher.evtx");
        if (!File.Exists(sample)) return; // Sample not provided: nothing to check on this PC.

        var source = Create();
        var log = LogTarget.File(Path.GetFullPath(sample));
        var first = source.Query(log, "*", CancellationToken.None).First();
        Assert.Null(source.FormatMessage(log, first.RecordId));
    }

    [WindowsFact]
    public void GetChannels_ContainsSystem_AsAdminEnabled()
    {
        var channels = Create().GetChannels();
        var system = Assert.Single(channels, c => c.Name == "System");
        Assert.Equal(ChannelType.Admin, system.Type);
        Assert.True(system.Enabled);
    }

    [WindowsFact]
    public void GetXml_ContainsEventRecordId()
    {
        var source = Create();
        var e = source.Query(System, Last30Days(), CancellationToken.None).First();

        var xml = source.GetXml(System, e.RecordId);

        Assert.NotNull(xml);
        Assert.Contains($"<EventRecordID>{e.RecordId}</EventRecordID>", xml);
    }

    [WindowsFact]
    public void FormatMessage_FindsTextForSomeRecentEvent()
    {
        var source = Create();
        var found = source.Query(System, Last30Days(), CancellationToken.None).Take(100)
            .Select(e => source.FormatMessage(System, e.RecordId)).Any(m => !string.IsNullOrWhiteSpace(m));
        Assert.True(found);
    }

    [WindowsFact]
    public void QueryWithText_Message_ReturnsTextForSomeEvents()
    {
        var source = Create();
        var results = source.QueryWithText(System, Last30Days(), TextTarget.Message, CancellationToken.None).Take(100).ToList();
        Assert.Contains(results, r => !string.IsNullOrWhiteSpace(r.Text));
    }

    [WindowsFact]
    public void QueryWithText_Message_MatchesFormatMessageForTheSameEvent()
    {
        var source = Create();
        var results = source.QueryWithText(System, Last30Days(), TextTarget.Message, CancellationToken.None).Take(300).ToList();

        Assert.NotEmpty(results);
        foreach (var (e, text) in results)
        {
            // Where Windows can build a message, the text is exactly that message. (Without one the text is the event data.)
            var message = source.FormatMessage(System, e.RecordId);
            if (message is not null) Assert.Equal(message, text);
        }
    }

    [WindowsFact]
    public void QueryWithText_KeepsNewestFirstOrder()
    {
        var source = Create();
        var ids = source.QueryWithText(System, Last30Days(), TextTarget.Message, CancellationToken.None).Take(600).Select(r => r.Event.RecordId).ToList();
        Assert.Equal(ids.OrderByDescending(i => i), ids);
    }

    [WindowsFact]
    public void QueryWithText_Xml_ReturnsXml()
    {
        var source = Create();
        var first = source.QueryWithText(System, Last30Days(), TextTarget.Xml, CancellationToken.None).First();
        Assert.Contains("<Event", first.Text);
    }

    [WindowsFact]
    public void GetEventData_ReturnsPairs_WithoutThrowing()
    {
        var source = Create();
        var events = source.Query(System, Last30Days(), CancellationToken.None).Take(50).ToList();
        var data = events.Select(e => source.GetEventData(System, e.RecordId)).ToList();
        Assert.Contains(data, d => d.Count > 0);
    }
}
