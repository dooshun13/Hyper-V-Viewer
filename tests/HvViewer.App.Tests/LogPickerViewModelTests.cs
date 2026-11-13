using HvViewer.App.Events;
using HvViewer.Core.Model;
using HvViewer.Rules;
using Xunit;

namespace HvViewer.App.Tests;

public class LogPickerViewModelTests
{
    private static ChannelInfo Ch(string name, ChannelType type = ChannelType.Operational, bool enabled = true) => new(name, type, enabled);

    private static readonly ChannelInfo[] Channels =
    [
        Ch("Application", ChannelType.Admin), Ch("Security", ChannelType.Admin), Ch("Setup", ChannelType.Admin), Ch("System", ChannelType.Admin),
        Ch("Microsoft-Windows-GroupPolicy/Operational"), Ch("Microsoft-Windows-Kernel-Power/Thermal-Operational"),
        Ch("Vendor App/Admin", ChannelType.Admin), Ch("Vendor App/Analytic", ChannelType.Analytic),
    ];

    private static RuleSet Rules() => new(
    [
        new Troubleshooter(1, "disk-storage", "Disk & storage", "s",
            [new RuleEvent("disk", 153, "System", RuleSeverity.Warning, "t", "m", "c", "n", "https://learn.microsoft.com/x")]),
        new Troubleshooter(1, "group-policy", "Group Policy", "s",
            [new RuleEvent("Microsoft-Windows-GroupPolicy", 1129, "System", RuleSeverity.Error, "t", "m", "c", "n", "https://learn.microsoft.com/x"),
             new RuleEvent("Microsoft-Windows-GroupPolicy", 1058, "Microsoft-Windows-GroupPolicy/Operational", RuleSeverity.Error, "t", "m", "c", "n", "https://learn.microsoft.com/x")]),
    ], []);

    private static LogPickerViewModel Create(bool admin = true, params string[] initial) =>
        new(() => Channels, Rules(), admin, initial.Length == 0 ? ["System", "Application"] : initial);

    private static LogNodeVm Find(IEnumerable<LogNodeVm> nodes, string label)
    {
        foreach (var n in nodes)
        {
            if (n.Label == label) return n;
            var inner = n.Children.Count > 0 ? FindOrNull(n.Children, label) : null;
            if (inner is not null) return inner;
        }
        throw new InvalidOperationException("No node " + label);
    }

    private static LogNodeVm? FindOrNull(IEnumerable<LogNodeVm> nodes, string label)
    {
        foreach (var n in nodes)
        {
            if (n.Label == label) return n;
            var inner = FindOrNull(n.Children, label);
            if (inner is not null) return inner;
        }
        return null;
    }

    [Fact] public void Selected_StartsWithTheInitialLogs() =>
        Assert.Equal(["System", "Application"], Create().Selected);

    [Theory]
    [InlineData("System", "System")]
    [InlineData("System,Application", "System + 1 more")]
    [InlineData("System,Application,Setup", "System + 2 more")]
    public void Summary_NamesTheFirstAndCountsTheRest(string initial, string expected) =>
        Assert.Equal(expected, Create(true, initial.Split(',')).Summary);

    [Fact] public void Summary_NothingSelected() => Assert.Equal("No logs selected", new LogPickerViewModel(() => Channels, Rules(), true, []).Summary);

    [Fact] public void Tree_HasWindowsLogsApplicationsAndTroubleshooters()
    {
        var picker = Create();
        picker.EnsureLoaded();
        Assert.Equal(["Windows Logs", "Applications and Services Logs", "By troubleshooter"], picker.Roots.Select(r => r.Label));
    }

    [Fact] public void Tree_IsNotBuiltUntilNeeded()
    {
        var calls = 0;
        var picker = new LogPickerViewModel(() => { calls++; return Channels; }, Rules(), true, ["System"]);
        Assert.Equal(0, calls);
        picker.EnsureLoaded();
        picker.EnsureLoaded();
        Assert.Equal(1, calls);
    }

    [Fact] public void CheckingALeaf_SelectsItsChannelAndRaisesChanged()
    {
        var picker = Create();
        picker.EnsureLoaded();
        var changed = 0;
        picker.SelectionChanged += () => changed++;

        Find(picker.Roots, "Setup").IsChecked = true;

        Assert.Contains("Setup", picker.Selected);
        Assert.Equal(1, changed);
    }

    [Fact] public void UncheckingALeaf_RemovesIt()
    {
        var picker = Create();
        picker.EnsureLoaded();
        Find(picker.Roots, "System").IsChecked = false;
        Assert.Equal(["Application"], picker.Selected);
    }

    [Fact] public void CheckingAFolder_SelectsEverythingUnderIt()
    {
        var picker = Create();
        picker.EnsureLoaded();

        Find(picker.Roots, "Windows").IsChecked = true;

        Assert.Contains("Microsoft-Windows-GroupPolicy/Operational", picker.Selected);
        Assert.Contains("Microsoft-Windows-Kernel-Power/Thermal-Operational", picker.Selected);
    }

    [Fact] public void FolderState_IsPartialWhenSomeChildrenAreSelected()
    {
        var picker = Create(true, "System", "Microsoft-Windows-GroupPolicy/Operational");
        picker.EnsureLoaded();
        Assert.Null(Find(picker.Roots, "Windows").IsChecked);
        Assert.True(Find(picker.Roots, "GroupPolicy").IsChecked);
    }

    [Fact] public void StandardUser_SecurityIsDisabledWithTooltip_AndCannotBeSelected()
    {
        var picker = Create(admin: false);
        picker.EnsureLoaded();
        var security = Find(picker.Roots, "Security");

        security.IsChecked = true;

        Assert.False(security.IsEnabled);
        Assert.Equal("Run as administrator to include", security.Tooltip);
        Assert.DoesNotContain("Security", picker.Selected);
    }

    [Fact] public void Admin_CanSelectSecurity()
    {
        var picker = Create(admin: true);
        picker.EnsureLoaded();
        Find(picker.Roots, "Security").IsChecked = true;
        Assert.Contains("Security", picker.Selected);
    }

    [Fact] public void SelectAll_TicksEveryVisibleLog_AndClearAllUnticksThem()
    {
        var picker = Create();
        picker.SelectAllCommand.Execute(null);
        Assert.Equal(["Application", "Microsoft-Windows-GroupPolicy/Operational", "Microsoft-Windows-Kernel-Power/Thermal-Operational", "Security", "Setup", "System", "Vendor App/Admin"],
            picker.Selected.Order(StringComparer.OrdinalIgnoreCase));   // the analytic log stays hidden

        picker.ClearAllCommand.Execute(null);
        Assert.Empty(picker.Selected);
        Assert.Equal("No logs selected", picker.Summary);
    }

    [Fact] public void SelectAll_WithAFilter_OnlyTicksTheMatches()
    {
        var picker = Create(true, "System");
        picker.EnsureLoaded();
        picker.FilterText = "GroupPolicy";
        picker.SelectAllCommand.Execute(null);
        Assert.Equal(["Microsoft-Windows-GroupPolicy/Operational", "System"], picker.Selected.Order(StringComparer.OrdinalIgnoreCase));
    }

    [Fact] public void SelectAll_AsStandardUser_SkipsSecurity()
    {
        var picker = Create(admin: false);
        picker.SelectAllCommand.Execute(null);
        Assert.DoesNotContain("Security", picker.Selected);
        Assert.Contains("Setup", picker.Selected);
    }

    [Fact] public void Filter_HidesNodesThatDoNotMatch_AndKeepsMatchingParents()
    {
        var picker = Create();
        picker.EnsureLoaded();

        picker.FilterText = "grouppolicy";

        Assert.True(Find(picker.Roots, "GroupPolicy").IsVisible);
        Assert.True(Find(picker.Roots, "Windows").IsVisible);
        Assert.False(Find(picker.Roots, "Setup").IsVisible);

        picker.FilterText = "";
        Assert.True(Find(picker.Roots, "Setup").IsVisible);
    }

    [Fact] public void AnalyticLogs_AreHiddenUntilTheToggleIsOn()
    {
        var picker = Create();
        picker.EnsureLoaded();
        Assert.Null(FindOrNull(picker.Roots, "Analytic"));

        picker.ShowAnalyticAndDebug = true;

        Assert.NotNull(FindOrNull(picker.Roots, "Analytic"));
    }

    [Fact] public void SelectionIsKept_WhenTheTreeIsRebuilt()
    {
        var picker = Create();
        picker.EnsureLoaded();
        Find(picker.Roots, "Setup").IsChecked = true;
        picker.ShowAnalyticAndDebug = true;
        Assert.True(Find(picker.Roots, "Setup").IsChecked);
    }

    [Fact] public void TroubleshooterNode_SelectsItsLogs()
    {
        var picker = Create(true, "Application");
        picker.EnsureLoaded();

        Find(picker.Roots, "Group Policy").IsChecked = true;

        Assert.Contains("System", picker.Selected);
        Assert.Contains("Microsoft-Windows-GroupPolicy/Operational", picker.Selected);
    }

    [Fact] public void SetSelected_ReplacesTheSelection()
    {
        var picker = Create();
        picker.SetSelected(["Setup"]);
        Assert.Equal(["Setup"], picker.Selected);
    }

    [Fact] public void Counts_AreShownOnNodes()
    {
        var picker = Create();
        picker.EnsureLoaded();
        picker.SetCounts(new Dictionary<string, int> { ["System"] = 1234 });
        Assert.Equal("1,234", Find(picker.Roots, "System").CountText);
        Assert.Equal("", Find(picker.Roots, "Setup").CountText);
    }
}
