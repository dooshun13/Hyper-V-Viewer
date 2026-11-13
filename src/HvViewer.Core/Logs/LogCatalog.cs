using HvViewer.Core.Model;

namespace HvViewer.Core.Logs;

/// <summary>Arranges log channels in the same tree Event Viewer shows.</summary>
public static class LogCatalog
{
    private const string WindowsLogsLabel = "Windows Logs";
    private const string AppsLabel = "Applications and Services Logs";
    private const string MicrosoftWindowsPrefix = "Microsoft-Windows-";
    private const string MicrosoftPrefix = "Microsoft-";

    // Channel name -> label, in Event Viewer order.
    private static readonly (string Channel, string Label)[] WindowsLogs =
    [
        ("Application", "Application"),
        ("Security", "Security"),
        ("Setup", "Setup"),
        ("System", "System"),
        ("ForwardedEvents", "Forwarded Events"),
    ];

    private static readonly Dictionary<string, string> FriendlyTopLevel = new(StringComparer.OrdinalIgnoreCase)
    {
        ["HardwareEvents"] = "Hardware Events",
    };

    public static IReadOnlyList<LogNode> Build(IReadOnlyList<ChannelInfo> channels, bool includeHidden)
    {
        var visible = channels
            .Where(c => includeHidden || (c.Enabled && c.Type is ChannelType.Admin or ChannelType.Operational))
            .ToList();

        var windowsLogs = new List<LogNode>();
        foreach (var (channel, label) in WindowsLogs)
        {
            if (visible.Any(c => string.Equals(c.Name, channel, StringComparison.OrdinalIgnoreCase)))
                windowsLogs.Add(new LogNode(label, channel, []));
        }

        var reserved = WindowsLogs.Select(w => w.Channel).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var apps = new Builder(AppsLabel, null);
        foreach (var channel in visible.Where(c => !reserved.Contains(c.Name)))
            apps.Add(Path(channel.Name), channel.Name);

        return [new LogNode(WindowsLogsLabel, null, windowsLogs), apps.ToNode()];
    }

    /// <summary>"Microsoft-Windows-GroupPolicy/Operational" becomes Microsoft, Windows, GroupPolicy, Operational.</summary>
    private static List<string> Path(string channel)
    {
        var slash = channel.IndexOf('/');
        var provider = slash < 0 ? channel : channel[..slash];
        var rest = slash < 0 ? [] : channel[(slash + 1)..].Split('/', StringSplitOptions.RemoveEmptyEntries);

        var path = new List<string>();
        if (FriendlyTopLevel.TryGetValue(provider, out var friendly)) path.Add(friendly);
        else if (provider.StartsWith(MicrosoftWindowsPrefix, StringComparison.OrdinalIgnoreCase))
            path.AddRange(["Microsoft", "Windows", provider[MicrosoftWindowsPrefix.Length..]]);
        else if (provider.StartsWith(MicrosoftPrefix, StringComparison.OrdinalIgnoreCase))
            path.AddRange(["Microsoft", provider[MicrosoftPrefix.Length..]]);
        else path.Add(provider);

        path.AddRange(rest);
        return path;
    }

    private sealed class Builder(string label, string? channelName)
    {
        private readonly Dictionary<string, Builder> _children = new(StringComparer.OrdinalIgnoreCase);

        public string? ChannelName { get; private set; } = channelName;

        public void Add(IReadOnlyList<string> path, string channel)
        {
            var node = this;
            foreach (var segment in path)
            {
                if (!node._children.TryGetValue(segment, out var child))
                    node._children[segment] = child = new Builder(segment, null);
                node = child;
            }
            node.ChannelName = channel;
        }

        public LogNode ToNode() => new(label, ChannelName,
            _children.Values.OrderBy(c => c.Label, StringComparer.OrdinalIgnoreCase).Select(c => c.ToNode()).ToList());

        private string Label => label;
    }
}
