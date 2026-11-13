using HvViewer.Core.Model;
using HvViewer.Core.Query;
using Xunit;
using static HvViewer.Core.Model.EventLevel;
using static HvViewer.Tests.Core.TestData;

namespace HvViewer.Tests.Core;

public class QueryTextTests
{
    private static readonly DateTime Now = Utc("2026-11-11T03:00:00Z");

    private static readonly EventQuery Defaults = new()
    {
        Logs = ["System", "Application"],
        Range = TimeRange.Last(TimeSpan.FromHours(24), Now),
        LastSpan = TimeSpan.FromHours(24),
    };

    // 22:00 to 04:00 local in Mauritius (UTC+4) = 18:00Z to 00:00Z
    private static readonly EventQuery MockupQuery = Defaults with
    {
        Text = "Disk 3",
        TextOptions = new TextOptions(WholeWord: true),
        Range = R("2026-11-10T18:00:00Z", "2026-11-11T00:00:00Z"),
        LastSpan = null,
        IncludeIds = [153],
        ExcludeIds = [7036],
        Levels = [Critical, Error, Warning],
    };

    [Fact] public void Format_MatchesMockupExample() => Assert.Equal(
        "\"Disk 3\" word from:2026-11-10 22:00:00 to:2026-11-11 04:00:00 id:153,-7036 level:>=warning",
        QueryText.Format(MockupQuery, Mauritius));

    [Fact] public void Parse_RoundTrips()
    {
        var q = QueryText.Parse(QueryText.Format(MockupQuery, Mauritius), Defaults, Mauritius, Now);
        Assert.Equal(MockupQuery, q, new QueryComparer());
    }

    [Fact] public void Parse_RoundTrips_TrickyValues()
    {
        var tricky = MockupQuery with
        {
            Text = "say \"hi\" key:value",
            TextOptions = new TextOptions(MatchCase: true, Regex: true, IgnoreAccents: true),
            Target = TextTarget.Xml,
            Sources = ["disk", "Service Control Manager"],
            Logs = ["System", "Microsoft-Windows-GroupPolicy/Operational"],
            Levels = [Error, Information],
            Tasks = [new TaskFilter("Microsoft-Windows-Security-Auditing", 12544)],
        };
        var q = QueryText.Parse(QueryText.Format(tricky, Mauritius), Defaults, Mauritius, Now);
        Assert.Equal(tricky, q, new QueryComparer());
    }

    [Fact] public void Format_LastRange_UsesLastToken() =>
        Assert.Equal("disk last:24h", QueryText.Format(Defaults with { Text = "disk" }, Mauritius));

    [Fact] public void Parse_Last24h_UsesNow() => Assert.Equal(
        TimeRange.Last(TimeSpan.FromHours(24), Now), QueryText.Parse("last:24h", Defaults, Mauritius, Now).Range);

    [Fact] public void Parse_Empty_UsesDefaults() =>
        Assert.Equal(Defaults, QueryText.Parse("", Defaults, Mauritius, Now), new QueryComparer());

    [Fact] public void Parse_BareWordsJoinedWithOneSpace() =>
        Assert.Equal("disk retry", QueryText.Parse("disk   retry", Defaults, Mauritius, Now).Text);

    [Fact] public void Parse_QuotedSourceInList() => Assert.Equal(
        ["disk", "Service Control Manager"],
        QueryText.Parse("source:disk,\"Service Control Manager\"", Defaults, Mauritius, Now).Sources);

    [Fact] public void Parse_FromOnly_EndsAtNow() =>
        Assert.Equal(Now, QueryText.Parse("from:2026-11-11 05:00:00", Defaults, Mauritius, Now).Range.ToUtc);

    [Fact] public void Parse_DateWithoutTime_IsMidnight() => Assert.Equal(
        Utc("2026-11-09T20:00:00Z"), QueryText.Parse("from:2026-11-10", Defaults, Mauritius, Now).Range.FromUtc);

    [Fact] public void Parse_LogsAbsent_KeepDefaults() =>
        Assert.Equal(["System", "Application"], QueryText.Parse("disk", Defaults, Mauritius, Now).Logs);

    [Fact] public void Parse_IdsSplitIncludeAndExclude()
    {
        var q = QueryText.Parse("id:153,-7036,129", Defaults, Mauritius, Now);
        Assert.Equal([153, 129], q.IncludeIds);
        Assert.Equal([7036], q.ExcludeIds);
    }

    [Fact] public void Parse_UnknownKey_Throws() => Assert.Equal(
        "Unknown filter 'foo'", Assert.Throws<QueryTextException>(() => QueryText.Parse("foo:1", Defaults, Mauritius, Now)).Message);

    [Fact] public void Parse_BadDate_ThrowsWithPosition() => Assert.Equal(
        0, Assert.Throws<QueryTextException>(() => QueryText.Parse("from:2026-13-01", Defaults, Mauritius, Now)).Position);

    [Fact] public void Parse_BadDate_PositionPointsAtToken() => Assert.Equal(
        5, Assert.Throws<QueryTextException>(() => QueryText.Parse("disk to:nope", Defaults, Mauritius, Now)).Position);

    [Fact] public void Parse_UnclosedQuote_Throws() =>
        Assert.Throws<QueryTextException>(() => QueryText.Parse("\"Disk 3", Defaults, Mauritius, Now));

    [Fact] public void Parse_FromTo_DstGap_ConvertsWithZoneRules() => Assert.Equal(
        Utc("2026-03-29T01:00:00Z"), QueryText.Parse("from:2026-03-29 02:30:00", Defaults, Paris, Now).Range.FromUtc);

    [Fact] public void Parse_LevelGreaterOrEqualWarning() => Assert.Equal(
        [Critical, Error, Warning], QueryText.Parse("level:>=warning", Defaults, Mauritius, Now).Levels);

    [Fact] public void Parse_LevelList() => Assert.Equal(
        [Error, Critical], QueryText.Parse("level:error,critical", Defaults, Mauritius, Now).Levels);

    [Fact] public void Parse_InAndFlags()
    {
        var q = QueryText.Parse("x case regex noaccent in:all", Defaults, Mauritius, Now);
        Assert.Equal(TextTarget.AllFields, q.Target);
        Assert.Equal(new TextOptions(MatchCase: true, Regex: true, IgnoreAccents: true), q.TextOptions);
        Assert.Equal("x", q.Text);
    }

    private sealed class QueryComparer : IEqualityComparer<EventQuery>
    {
        public bool Equals(EventQuery? a, EventQuery? b) =>
            a is not null && b is not null && a.Text == b.Text && a.TextOptions == b.TextOptions && a.Target == b.Target
            && a.Range == b.Range && a.LastSpan == b.LastSpan
            && a.IncludeIds.SequenceEqual(b.IncludeIds) && a.ExcludeIds.SequenceEqual(b.ExcludeIds)
            && a.Sources.SequenceEqual(b.Sources) && a.Logs.SequenceEqual(b.Logs)
            && a.Levels.SequenceEqual(b.Levels) && a.Tasks.SequenceEqual(b.Tasks);

        public int GetHashCode(EventQuery q) => HashCode.Combine(q.Text, q.Range);
    }
}
