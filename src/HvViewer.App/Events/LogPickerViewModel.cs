using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HvViewer.Core.Logs;
using HvViewer.Core.Model;
using HvViewer.Rules;

namespace HvViewer.App.Events;

/// <summary>
/// The log category dropdown: Windows Logs, Applications and Services Logs, and By troubleshooter.
/// The tree is only built when the dropdown is first opened (reading all channels takes a moment).
/// </summary>
public sealed partial class LogPickerViewModel : ObservableObject
{
    private const string AdminOnlyTooltip = "Run as administrator to include";

    private readonly Func<IReadOnlyList<ChannelInfo>> _channels;
    private readonly RuleSet _rules;
    private readonly bool _isAdmin;
    private readonly List<string> _selected;
    private IReadOnlyList<ChannelInfo>? _loadedChannels;
    private Dictionary<string, int> _counts = new(StringComparer.OrdinalIgnoreCase);
    private bool _loaded;

    public LogPickerViewModel(Func<IReadOnlyList<ChannelInfo>> channels, RuleSet rules, bool isAdmin, IReadOnlyList<string> initial)
    {
        _channels = channels;
        _rules = rules;
        _isAdmin = isAdmin;
        _selected = initial.Where(CanSelect).ToList();
    }

    public event Action? SelectionChanged;

    public IReadOnlyList<string> Selected => _selected.ToList();

    public string Summary => _selected.Count switch
    {
        0 => "No logs selected",
        1 => _selected[0],
        _ => $"{_selected[0]} + {_selected.Count - 1} more",
    };

    [ObservableProperty] private IReadOnlyList<LogNodeVm> _roots = [];
    [ObservableProperty] private string _filterText = "";
    [ObservableProperty] private bool _showAnalyticAndDebug;

    partial void OnFilterTextChanged(string value) => ApplyFilter();

    partial void OnShowAnalyticAndDebugChanged(bool value)
    {
        if (_loaded) Rebuild();
    }

    /// <summary>Builds the tree the first time it is needed.</summary>
    public void EnsureLoaded()
    {
        if (_loaded) return;
        _loadedChannels = _channels();
        _loaded = true;
        Rebuild();
    }

    /// <summary>Ticks every log you can read that matches the filter box (all of them when it is empty).</summary>
    [RelayCommand]
    private void SelectAll()
    {
        EnsureLoaded();
        var channels = Roots.Take(Math.Max(0, Roots.Count - 1))   // the last root, By troubleshooter, only repeats these logs
            .SelectMany(Leaves)
            .Where(n => n.IsVisible && n.ChannelName is not null)
            .Select(n => n.ChannelName!);
        SetSelected(_selected.Concat(channels).ToList());   // copy first: SetSelected clears the list it is given from
    }

    /// <summary>Unticks everything.</summary>
    [RelayCommand]
    private void ClearAll() => SetSelected([]);

    private static IEnumerable<LogNodeVm> Leaves(LogNodeVm node)
    {
        if (node.Children.Count == 0) yield return node;
        foreach (var child in node.Children)
            foreach (var leaf in Leaves(child)) yield return leaf;
    }

    public void SetSelected(IEnumerable<string> logs)
    {
        _selected.Clear();
        _selected.AddRange(logs.Where(CanSelect).Distinct(StringComparer.OrdinalIgnoreCase));
        Changed();
    }

    public void SetCounts(IReadOnlyDictionary<string, int> counts)
    {
        _counts = new Dictionary<string, int>(counts, StringComparer.OrdinalIgnoreCase);
        foreach (var node in All(Roots)) node.CountText = CountFor(node);
    }

    // ---- used by the nodes ----

    internal bool? StateOf(LogNodeVm node)
    {
        var channels = node.Channels().Where(CanSelect).ToList();
        if (channels.Count == 0) return false;
        var selected = channels.Count(c => _selected.Contains(c, StringComparer.OrdinalIgnoreCase));
        return selected == 0 ? false : selected == channels.Count ? true : null;
    }

    internal void Set(LogNodeVm node, bool on)
    {
        foreach (var channel in node.Channels().Where(CanSelect).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var present = _selected.Contains(channel, StringComparer.OrdinalIgnoreCase);
            if (on && !present) _selected.Add(channel);
            else if (!on && present) _selected.RemoveAll(s => string.Equals(s, channel, StringComparison.OrdinalIgnoreCase));
        }
        Changed();
    }

    // ---- internals ----

    /// <summary>Security can only be read as administrator, so a standard user cannot select it.</summary>
    private bool CanSelect(string channel) => _isAdmin || !string.Equals(channel, "Security", StringComparison.OrdinalIgnoreCase);

    private void Changed()
    {
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(Selected));
        foreach (var node in All(Roots)) node.RaiseChecked();
        SelectionChanged?.Invoke();
    }

    private void Rebuild()
    {
        var tree = LogCatalog.Build(_loadedChannels ?? [], ShowAnalyticAndDebug);
        var roots = tree.Select(ToNode).ToList();
        roots.Add(BuildTroubleshooterRoot());
        foreach (var root in roots) root.IsExpanded = true;
        Roots = roots;
        SetCounts(_counts);
        ApplyFilter();
    }

    private LogNodeVm ToNode(LogNode node)
    {
        var enabled = CanSelect(node.ChannelName ?? "");
        var vm = new LogNodeVm(this, node.Label, node.ChannelName, null, enabled, enabled ? "" : AdminOnlyTooltip);
        foreach (var child in node.Children) vm.Children.Add(ToNode(child));
        return vm;
    }

    private LogNodeVm BuildTroubleshooterRoot()
    {
        var root = new LogNodeVm(this, "By troubleshooter", null);
        foreach (var t in _rules.Troubleshooters)
            root.Children.Add(new LogNodeVm(this, t.Title, null, t.Logs));
        return root;
    }

    private void ApplyFilter()
    {
        var filter = FilterText.Trim();
        foreach (var root in Roots) Filter(root, filter);
    }

    private static bool Filter(LogNodeVm node, string filter)
    {
        var any = false;
        foreach (var child in node.Children) any |= Filter(child, filter);

        var self = filter.Length == 0
                   || node.Label.Contains(filter, StringComparison.OrdinalIgnoreCase)
                   || (node.ChannelName?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false);
        node.IsVisible = self || any;
        if (filter.Length > 0 && any) node.IsExpanded = true;
        return node.IsVisible;
    }

    private string CountFor(LogNodeVm node)
    {
        if (node.ChannelName is null || !_counts.TryGetValue(node.ChannelName, out var n) || n <= 0) return "";
        return n.ToString("N0", CultureInfo.InvariantCulture);
    }

    private static IEnumerable<LogNodeVm> All(IEnumerable<LogNodeVm> nodes)
    {
        foreach (var n in nodes)
        {
            yield return n;
            foreach (var child in All(n.Children)) yield return child;
        }
    }
}
