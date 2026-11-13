using HvViewer.Core.Model;
using HvViewer.Core.Query;
using Xunit;
using static HvViewer.Core.Model.EventLevel;
using static HvViewer.Tests.Core.TestData;

namespace HvViewer.Tests.Core;

public class XPathBuilderTests
{
    private static readonly TimeRange Range = R("2026-11-10T18:00:00Z", "2026-11-11T00:00:00Z");

    [Fact] public void Build_TimeOnly() => Assert.Equal(
        "*[System[TimeCreated[@SystemTime>='2026-11-10T18:00:00.000Z' and @SystemTime<='2026-11-11T00:00:00.000Z']]]",
        XPathBuilder.Build(Q(range: Range)).XPath);

    [Fact] public void Build_IdsLevelsSourcesExclude() => Assert.Equal(
        "*[System[(EventID=153 or EventID=129) and EventID!=7036 and (Level=1 or Level=2 or Level=3) and Provider[@Name='disk' or @EventSourceName='disk'] and TimeCreated[@SystemTime>='2026-11-10T18:00:00.000Z' and @SystemTime<='2026-11-11T00:00:00.000Z']]]",
        XPathBuilder.Build(Q(include: [153, 129], exclude: [7036], levels: [Critical, Error, Warning], sources: ["disk"], range: Range)).XPath);

    [Fact] public void Build_InformationLevelIncludesLevel0() =>
        Assert.Contains("(Level=0 or Level=4)", XPathBuilder.Build(Q(levels: [Information])).XPath);

    [Fact] public void Build_EscapesQuotes() =>
        Assert.Contains("@Name='O&apos;Brien'", XPathBuilder.Build(Q(sources: ["O'Brien"])).XPath);

    [Fact] public void Build_EscapesAmpersandAndAngle() =>
        Assert.Contains("@Name='A&amp;B&lt;C'", XPathBuilder.Build(Q(sources: ["A&B<C"])).XPath);

    [Fact] public void Build_MoreThan20Ids_FilteredInProcess()
    {
        var r = XPathBuilder.Build(Q(include: Enumerable.Range(1, 21).ToArray()));
        Assert.True(r.IdsInProcess);
        Assert.DoesNotContain("EventID", r.XPath);
    }

    [Fact] public void Build_20Ids_StaysInXPath()
    {
        var r = XPathBuilder.Build(Q(include: Enumerable.Range(1, 20).ToArray()));
        Assert.False(r.IdsInProcess);
        Assert.Contains("EventID=20", r.XPath);
    }

    [Fact] public void Build_TasksAddTaskClause() =>
        Assert.Contains("(Task=12544)", XPathBuilder.Build(Q(tasks: [new("Microsoft-Windows-Security-Auditing", 12544)])).XPath);

    [Fact] public void LevelsOrIds_CombinesWithOr() => Assert.Equal(
        "*[System[(Level=1 or Level=2 or Level=3 or EventID=4625 or EventID=4740) and TimeCreated[@SystemTime>='2026-11-10T18:00:00.000Z' and @SystemTime<='2026-11-11T00:00:00.000Z']]]",
        XPathBuilder.LevelsOrIds([Critical, Error, Warning], [4625, 4740], Range));

    [Fact] public void LevelsOrIds_NoIds_LevelsOnly() => Assert.Equal(
        "*[System[(Level=1 or Level=2 or Level=3) and TimeCreated[@SystemTime>='2026-11-10T18:00:00.000Z' and @SystemTime<='2026-11-11T00:00:00.000Z']]]",
        XPathBuilder.LevelsOrIds([Critical, Error, Warning], [], Range));

    [Fact] public void Build_TextIsNeverInXPath() =>
        Assert.DoesNotContain("secret-needle", XPathBuilder.Build(Q(text: "secret-needle")).XPath);
}
