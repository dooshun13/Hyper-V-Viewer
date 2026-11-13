using System.Runtime.Versioning;
using HvViewer.Core.Model;
using HvViewer.Core.Reading;
using Xunit;

namespace HvViewer.Tests.Performance;

/// <summary>
/// Events whose message Windows cannot build must still be searchable by their event data, and the reader must give
/// that text itself: asking again per event means re-scanning the whole log for every such event, which made search 2x slower.
/// Needs tests/samples/perf.evtx (does nothing without it).
/// </summary>
[SupportedOSPlatform("windows")]
public class NoMessageFallbackTests
{
    private static string? PerfLog()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "samples", "perf.evtx");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    [WindowsFact]
    public void Native_EventWithoutAMessage_StillCarriesItsEventDataAsText()
    {
        if (PerfLog() is not { } path) return;
        var source = new NativeEventSource();
        var log = LogTarget.File(path);

        var noText = source.QueryWithText(log, "*", TextTarget.Message, CancellationToken.None)
            .Where(r => string.IsNullOrWhiteSpace(r.Text)).Take(8).Select(r => r.Event).ToList();

        // An event may genuinely have neither a message nor data; only those may be left without text.
        foreach (var e in noText)
            Assert.True(source.GetEventData(log, e.RecordId).Count == 0,
                $"Event {e.ProviderName} {e.EventId} (record {e.RecordId}) has event data but no search text.");
    }

    [WindowsFact]
    public void Managed_EventWithoutAMessage_StillCarriesItsEventDataAsText()
    {
        if (PerfLog() is not { } path) return;
        var source = new ManagedEventSource();
        var log = LogTarget.File(path);

        var noText = source.QueryWithText(log, "*", TextTarget.Message, CancellationToken.None)
            .Where(r => string.IsNullOrWhiteSpace(r.Text)).Take(8).Select(r => r.Event).ToList();

        foreach (var e in noText)
            Assert.True(source.GetEventData(log, e.RecordId).Count == 0,
                $"Event {e.ProviderName} {e.EventId} (record {e.RecordId}) has event data but no search text.");
    }
}
