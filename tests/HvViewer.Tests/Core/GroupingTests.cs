using HvViewer.Core.Model;
using HvViewer.Core.Search;
using Xunit;
using static HvViewer.Tests.Core.TestData;

namespace HvViewer.Tests.Core;

public class GroupingTests
{
    private static EventRecordLite Ev(string time, int id = 153, string provider = "disk", string log = "System") =>
        new(log, 1, Utc("2026-11-10T" + time + "Z"), id, EventLevel.Warning, provider, null, "PC", 0, 0);

    private static IReadOnlyList<EventRecordLite> NewestFirst(params EventRecordLite[] events) =>
        events.OrderByDescending(e => e.TimeCreatedUtc).ToList();

    [Fact] public void MockupCase_TwoGroups()
    {
        var nine = Enumerable.Range(0, 9).Select(i => Ev($"23:41:{10 + i * 5:00}"));
        var three = new[] { Ev("23:44:58"), Ev("23:45:05"), Ev("23:45:20") };
        var groups = Grouping.Group(NewestFirst(nine.Concat(three).ToArray()));

        Assert.Equal([3, 9], groups.Select(g => g.Count));
    }

    [Fact] public void SameKey_GapUnder2Min_OneGroup()
    {
        var groups = Grouping.Group(NewestFirst(Ev("10:00:00"), Ev("10:01:30"), Ev("10:03:00")));
        Assert.Single(groups);
        Assert.Equal(3, groups[0].Count);
    }

    [Fact] public void SameKey_GapOver2Min_Splits() =>
        Assert.Equal(2, Grouping.Group(NewestFirst(Ev("10:00:00"), Ev("10:02:01"))).Count);

    [Fact] public void DifferentEventBetween_SplitsGroups()
    {
        var groups = Grouping.Group(NewestFirst(Ev("10:00:00"), Ev("10:00:10", id: 7036, provider: "Service Control Manager"), Ev("10:00:20")));
        Assert.Equal([1, 1, 1], groups.Select(g => g.Count));
    }

    [Fact] public void DifferentLog_DoesNotGroup() =>
        Assert.Equal(2, Grouping.Group(NewestFirst(Ev("10:00:00"), Ev("10:00:10", log: "Application"))).Count);

    [Fact] public void ProviderComparison_IgnoresCase() =>
        Assert.Single(Grouping.Group(NewestFirst(Ev("10:00:00"), Ev("10:00:10", provider: "DISK"))));

    [Fact] public void Latest_IsNewest_First_IsOldest()
    {
        var oldest = Ev("10:00:00");
        var newest = Ev("10:01:00");
        var g = Assert.Single(Grouping.Group(NewestFirst(oldest, newest)));
        Assert.Equal(newest, g.Latest);
        Assert.Equal(oldest, g.First);
    }

    [Fact] public void Empty_ReturnsEmpty() => Assert.Empty(Grouping.Group([]));
}
