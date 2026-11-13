using HvViewer.Core.Model;
using Xunit;
using static HvViewer.Tests.Core.TestData;

namespace HvViewer.Tests.Core;

public class TimeRangeTests
{
    private static readonly DateTime Now = Utc("2026-11-11T03:00:00Z"); // 07:00 in Mauritius (UTC+4)

    [Fact] public void Today_UsesZoneMidnight() =>
        Assert.Equal(new TimeRange(Utc("2026-11-10T20:00:00Z"), Now), TimeRange.Today(Mauritius, Now));

    [Fact] public void Yesterday_IsFullPreviousLocalDay() =>
        Assert.Equal(new TimeRange(Utc("2026-11-09T20:00:00Z"), Utc("2026-11-10T20:00:00Z")), TimeRange.Yesterday(Mauritius, Now));

    [Fact] public void Last_EndsAtNow() =>
        Assert.Equal(new TimeRange(Now - TimeSpan.FromHours(24), Now), TimeRange.Last(TimeSpan.FromHours(24), Now));

    [Fact] public void SinceBoot_StartsAtBoot() =>
        Assert.Equal(new TimeRange(Utc("2026-11-10T22:00:00Z"), Now), TimeRange.SinceBoot(Utc("2026-11-10T22:00:00Z"), Now));

    [Fact] public void Length_IsToMinusFrom() =>
        Assert.Equal(TimeSpan.FromHours(3), new TimeRange(Utc("2026-11-11T00:00:00Z"), Now).Length);
}
