using global::System.Globalization;
using HvViewer.Core.Model;
using HvViewer.Core.Query;
using HvViewer.Core.Reading;

namespace HvViewer.Machine.Health;

/// <summary>Runs the five read-only health checks. A check that fails shows as Unknown / "Unavailable".</summary>
public sealed class HealthCheckRunner(
    IRegistryReader reg, IDriveProvider drives, IEventSource events, SystemInfo info, Func<DateTime> utcNow)
{
    private const double DiskWarnPercent = 15, DiskProblemPercent = 10;
    private const int UpdateWarnDays = 35, UpdateProblemDays = 60;

    private const string KernelPower = "Microsoft-Windows-Kernel-Power";
    private const string TimeService = "Microsoft-Windows-Time-Service";
    private const string WindowsUpdateClient = "Microsoft-Windows-WindowsUpdateClient";

    private static readonly string[] PendingRebootKeys =
    [
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending",
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired",
    ];

    public IReadOnlyList<HealthResult> RunAll() =>
    [
        Safe("disk", "Low disk space", CheckDisk),
        Safe("reboot", "Pending reboot", CheckReboot),
        Safe("timesync", "Time sync", CheckTimeSync),
        Safe("update", "Last update", CheckUpdate),
        Safe("uptime", "Uptime", CheckUptime),
    ];

    private static HealthResult Safe(string id, string label, Func<(HealthStatus Status, string Value, string Detail)> check)
    {
        try
        {
            var (status, value, detail) = check();
            return new HealthResult(id, label, status, value, detail);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new HealthResult(id, label, HealthStatus.Unknown, "Unavailable", "This check could not be read on this PC.");
        }
    }

    // ---- disk ----

    private (HealthStatus, string, string) CheckDisk()
    {
        var all = drives.FixedDrives().Where(d => d.Total > 0).ToList();
        if (all.Count == 0) return (HealthStatus.Unknown, "No data", "No fixed drives found.");

        var worst = all.OrderBy(d => (double)d.Free / d.Total).First();
        var percent = (double)worst.Free / worst.Total * 100;
        var status = percent < DiskProblemPercent ? HealthStatus.Problem : percent < DiskWarnPercent ? HealthStatus.Warning : HealthStatus.Ok;
        var freeGb = worst.Free / 1_073_741_824.0;
        var totalGb = worst.Total / 1_073_741_824.0;
        return (status, $"{worst.Name} {Math.Round(percent).ToString(CultureInfo.InvariantCulture)}% free",
            $"{freeGb:0.#} GB free of {totalGb:0.#} GB.".Replace(',', '.'));
    }

    // ---- pending reboot ----

    private (HealthStatus, string, string) CheckReboot()
    {
        var pending = PendingRebootKeys.Any(reg.KeyExists) || HasPendingFileRenames();
        return pending
            ? (HealthStatus.Warning, "Yes", "Windows is waiting for a restart to finish an install or update.")
            : (HealthStatus.Ok, "No", "No restart is waiting.");
    }

    private bool HasPendingFileRenames() =>
        reg.GetValue(@"SYSTEM\CurrentControlSet\Control\Session Manager", "PendingFileRenameOperations") switch
        {
            string[] values => values.Any(v => !string.IsNullOrEmpty(v)),
            string value => value.Length > 0,
            _ => false,
        };

    // ---- time sync ----

    private (HealthStatus, string, string) CheckTimeSync()
    {
        var now = utcNow();
        var found = Find(TimeSpan.FromDays(7), TimeService, 35, 36, 37).ToList(); // newest first
        if (found.Any(e => e.EventId == 36 && e.TimeCreatedUtc >= now.AddHours(-24)))
            return (HealthStatus.Warning, "Not synced recently", "The Windows Time service reported it could not sync in the last 24 hours.");
        if (found.Any(e => e.EventId is 35 or 37))
            return (HealthStatus.Ok, "In sync", "The Windows Time service synced in the last 7 days.");
        return (HealthStatus.Unknown, "No data", "No time sync events found in the last 7 days.");
    }

    // ---- last update ----

    private (HealthStatus, string, string) CheckUpdate()
    {
        var newest = Find(TimeSpan.FromDays(365), WindowsUpdateClient, 19).FirstOrDefault();
        if (newest is null) return (HealthStatus.Unknown, "No data", "No installed update found in the last year.");

        var days = (int)Math.Floor((utcNow() - newest.TimeCreatedUtc).TotalDays);
        var status = days > UpdateProblemDays ? HealthStatus.Problem : days > UpdateWarnDays ? HealthStatus.Warning : HealthStatus.Ok;
        var value = days switch { <= 0 => "today", 1 => "1 day ago", _ => $"{days} days ago" };
        return (status, value, $"Last update installed {newest.TimeCreatedUtc:yyyy-MM-dd HH:mm} UTC.");
    }

    // ---- uptime ----

    private (HealthStatus, string, string) CheckUptime()
    {
        var uptime = info.Uptime;
        var value = uptime.TotalDays >= 1
            ? $"{(int)uptime.TotalDays} d {uptime.Hours} h"
            : uptime.TotalHours >= 1 ? $"{(int)uptime.TotalHours} h {uptime.Minutes} min" : $"{uptime.Minutes} min";

        var since = info.BootTimeUtc.AddMinutes(-5);
        var unclean = Find(uptime + TimeSpan.FromMinutes(5), KernelPower, 41).Any(e => e.TimeCreatedUtc >= since)
                      || Find(uptime + TimeSpan.FromMinutes(5), "EventLog", 6008).Any(e => e.TimeCreatedUtc >= since);

        return unclean
            ? (HealthStatus.Warning, value, "Last restart was unexpected")
            : (HealthStatus.Ok, value, $"Up since {info.BootTimeUtc:yyyy-MM-dd HH:mm} UTC.");
    }

    // ---- helpers ----

    /// <summary>Events from the System log for one provider and set of IDs, newest first. Filtered again here so any event source behaves the same.</summary>
    private IEnumerable<EventRecordLite> Find(TimeSpan lookback, string provider, params int[] ids)
    {
        var now = utcNow();
        var from = now - lookback;
        var query = new EventQuery { Range = new TimeRange(from, now), Logs = ["System"], IncludeIds = ids, Sources = [provider] };
        var xpath = XPathBuilder.Build(query).XPath;

        return events.Query(LogTarget.Channel("System"), xpath, CancellationToken.None)
            .Where(e => ids.Contains(e.EventId)
                        && e.TimeCreatedUtc >= from
                        && (string.Equals(e.ProviderName, provider, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(e.EventSourceName, provider, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(e => e.TimeCreatedUtc);
    }
}
