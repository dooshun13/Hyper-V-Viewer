using HvViewer.Core.Reading;
using HvViewer.Core.Search;
using HvViewer.Machine;
using HvViewer.Machine.Health;
using HvViewer.Rules;

namespace HvViewer.App.Services;

/// <summary>The app's long-lived services, created once at start-up. Every one of them only reads.</summary>
public sealed class AppServices
{
    private AppServices(AppState state, bool isAdmin)
    {
        State = state;
        IsAdmin = isAdmin;

        Source = new NativeEventSource();
        Cache = new MessageCache();
        Search = new SearchService(Source, Cache);
        Rules = RuleLoader.LoadBuiltIn();
        Dashboard = new DashboardService(Source, Rules);

        var registry = new WindowsRegistryReader();
        SystemInfo = new SystemInfoReader(registry, () => DateTime.UtcNow, () => Environment.TickCount64, () => Environment.MachineName).Read();
        Health = new HealthCheckRunner(registry, new WindowsDriveProvider(), Source, SystemInfo, () => DateTime.UtcNow);
    }

    public static AppServices Create(AppState state, bool isAdmin) => new(state, isAdmin);

    public AppState State { get; }

    public bool IsAdmin { get; }

    public IEventSource Source { get; }

    public MessageCache Cache { get; }

    public SearchService Search { get; }

    public RuleSet Rules { get; }

    public DashboardService Dashboard { get; }

    /// <summary>The latest dashboard result, shared with the Troubleshooters screen.</summary>
    public DashboardResultStore Store { get; } = new();

    public SystemInfo SystemInfo { get; }


    public HealthCheckRunner Health { get; }

    public IHardwareProvider Hardware { get; } = new WindowsHardwareProvider();
}

/// <summary>Lets screens be created before the shell exists; the shell is plugged in right after it is built.</summary>
public sealed class LateNavigator : INavigator
{
    public INavigator? Target { get; set; }

    public void Show(string key) => Target?.Show(key);

    public void ShowEvents(HvViewer.Core.Model.EventQuery query) => Target?.ShowEvents(query);
}
