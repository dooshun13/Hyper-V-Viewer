using HvViewer.Core.Model;
using Xunit;
using static HvViewer.Tests.Core.TestData;

namespace HvViewer.Tests.Core;

public class EventQueryTests
{
    [Fact] public void Validate_FromAfterTo_ReturnsError() =>
        Assert.Contains("From must be before To.", Q(range: new(T("2026-11-11 04:00"), T("2026-11-10 22:00"))).Validate());

    [Fact] public void Validate_InvalidRegex_ReturnsError() =>
        Assert.Contains(Q(text: "([", regex: true).Validate(), e => e.StartsWith("Invalid regular expression"));

    [Fact] public void Validate_NoLogs_ReturnsError() =>
        Assert.Contains("Select at least one log.", Q(logs: []).Validate());

    [Fact] public void Validate_IdInBothIncludeAndExclude_ReturnsError() =>
        Assert.Contains("Event ID 153 is both included and excluded.", Q(include: [153], exclude: [153]).Validate());

    [Fact] public void Validate_ValidQuery_ReturnsNoErrors() =>
        Assert.Empty(Q(text: "disk", include: [153], exclude: [7036]).Validate());

    [Theory]
    [InlineData(EventLevel.LogAlways, "Information")]
    [InlineData(EventLevel.Critical, "Critical")]
    [InlineData(EventLevel.Error, "Error")]
    [InlineData(EventLevel.Warning, "Warning")]
    [InlineData(EventLevel.Information, "Information")]
    [InlineData(EventLevel.Verbose, "Verbose")]
    public void Label_UsesEnglishNames(EventLevel level, string expected) => Assert.Equal(expected, level.Label());

    [Fact] public void Source_PrefersEventSourceName() =>
        Assert.Equal("BugCheck", new EventRecordLite("System", 1, DateTime.UtcNow, 1001, EventLevel.Critical,
            "Microsoft-Windows-WER-SystemErrorReporting", "BugCheck", "PC", 0, 0).Source);
}
