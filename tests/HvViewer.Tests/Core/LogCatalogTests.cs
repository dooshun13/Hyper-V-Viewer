using HvViewer.Core.Logs;
using HvViewer.Core.Model;
using Xunit;

namespace HvViewer.Tests.Core;

public class LogCatalogTests
{
    private static ChannelInfo Ch(string name, ChannelType type = ChannelType.Operational, bool enabled = true) => new(name, type, enabled);

    private static readonly ChannelInfo[] Sample =
    [
        Ch("System", ChannelType.Admin), Ch("Application", ChannelType.Admin), Ch("Security", ChannelType.Admin),
        Ch("Setup", ChannelType.Admin), Ch("ForwardedEvents", ChannelType.Operational),
        Ch("Microsoft-Windows-GroupPolicy/Operational"),
        Ch("HardwareEvents", ChannelType.Admin),
        Ch("Vendor App/Admin", ChannelType.Admin),
    ];

    private static LogNode Root(IReadOnlyList<LogNode> roots, string label) => Assert.Single(roots, r => r.Label == label);

    private static LogNode? Walk(LogNode node, params string[] path)
    {
        var current = node;
        foreach (var label in path)
        {
            current = current.Children.FirstOrDefault(c => c.Label == label);
            if (current is null) return null;
        }
        return current;
    }

    [Fact] public void TwoRoots_InOrder() =>
        Assert.Equal(["Windows Logs", "Applications and Services Logs"], LogCatalog.Build(Sample, false).Select(r => r.Label));

    [Fact] public void WindowsLogs_AreInEventViewerOrder() => Assert.Equal(
        ["Application", "Security", "Setup", "System", "Forwarded Events"],
        Root(LogCatalog.Build(Sample, false), "Windows Logs").Children.Select(c => c.Label));

    [Fact] public void WindowsLogs_CarryChannelNames() => Assert.Equal(
        ["Application", "Security", "Setup", "System", "ForwardedEvents"],
        Root(LogCatalog.Build(Sample, false), "Windows Logs").Children.Select(c => c.ChannelName));

    [Fact] public void WindowsLogs_MissingOnesAreOmitted()
    {
        var roots = LogCatalog.Build([Ch("System", ChannelType.Admin)], false);
        Assert.Equal(["System"], Root(roots, "Windows Logs").Children.Select(c => c.Label));
    }

    [Fact] public void MicrosoftWindowsChannel_SplitsIntoPath()
    {
        var apps = Root(LogCatalog.Build(Sample, false), "Applications and Services Logs");
        var leaf = Walk(apps, "Microsoft", "Windows", "GroupPolicy", "Operational");
        Assert.NotNull(leaf);
        Assert.Equal("Microsoft-Windows-GroupPolicy/Operational", leaf.ChannelName);
    }

    [Fact] public void HardwareEvents_HasFriendlyLabelAtTopLevel()
    {
        var apps = Root(LogCatalog.Build(Sample, false), "Applications and Services Logs");
        var node = Assert.Single(apps.Children, c => c.Label == "Hardware Events");
        Assert.Equal("HardwareEvents", node.ChannelName);
    }

    [Fact] public void VendorChannel_SplitsOnSlashOnly()
    {
        var apps = Root(LogCatalog.Build(Sample, false), "Applications and Services Logs");
        Assert.Equal("Vendor App/Admin", Walk(apps, "Vendor App", "Admin")?.ChannelName);
    }

    [Fact] public void AnalyticDebugAndDisabled_AreHiddenByDefault()
    {
        ChannelInfo[] channels =
        [
            Ch("Vendor/Operational"), Ch("Vendor/Analytic", ChannelType.Analytic), Ch("Vendor/Debug", ChannelType.Debug),
            Ch("Vendor/Off", ChannelType.Operational, enabled: false),
        ];
        var apps = Root(LogCatalog.Build(channels, includeHidden: false), "Applications and Services Logs");
        Assert.Equal(["Operational"], Walk(apps, "Vendor")!.Children.Select(c => c.Label));
    }

    [Fact] public void IncludeHidden_ShowsEverything()
    {
        ChannelInfo[] channels = [Ch("Vendor/Operational"), Ch("Vendor/Analytic", ChannelType.Analytic), Ch("Vendor/Off", ChannelType.Operational, false)];
        var apps = Root(LogCatalog.Build(channels, includeHidden: true), "Applications and Services Logs");
        Assert.Equal(["Analytic", "Off", "Operational"], Walk(apps, "Vendor")!.Children.Select(c => c.Label));
    }

    [Fact] public void Children_AreSortedAlphabeticallyIgnoringCase()
    {
        ChannelInfo[] channels = [Ch("zeta/Admin"), Ch("Alpha/Admin"), Ch("beta/Admin")];
        var apps = Root(LogCatalog.Build(channels, false), "Applications and Services Logs");
        Assert.Equal(["Alpha", "beta", "zeta"], apps.Children.Select(c => c.Label));
    }

    [Fact] public void ChannelAndParentNode_Coexist()
    {
        ChannelInfo[] channels = [Ch("Windows PowerShell", ChannelType.Admin), Ch("Windows PowerShell/Extra")];
        var apps = Root(LogCatalog.Build(channels, false), "Applications and Services Logs");
        var node = Assert.Single(apps.Children);
        Assert.Equal("Windows PowerShell", node.ChannelName);
        Assert.Equal(["Extra"], node.Children.Select(c => c.Label));
    }
}
