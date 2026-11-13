using HvViewer.Core.Model;
using HvViewer.Machine;
using HvViewer.Machine.Health;
using HvViewer.Tests.Fakes;
using Xunit;

namespace HvViewer.Tests.Machine;

public class HealthCheckRunnerTests
{
    private static readonly DateTime Now = new(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);

    private const string RebootPending = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending";
    private const string RebootRequired = @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired";
    private const string SessionManager = @"SYSTEM\CurrentControlSet\Control\Session Manager";

    private sealed class Drives(params (string Name, long Total, long Free)[] drives) : IDriveProvider
    {
        public IReadOnlyList<(string Name, long Total, long Free)> FixedDrives() => drives;
    }

    private sealed class ThrowingDrives : IDriveProvider
    {
        public IReadOnlyList<(string Name, long Total, long Free)> FixedDrives() => throw new InvalidOperationException("boom");
    }

    private static SystemInfo Info(TimeSpan uptime) => new("PC", "Windows 11 Pro", "Professional", "23H2", "22631.1", MachineKind.Workstation,
        false, false, null, null, Now - uptime, uptime);

    private static HealthResult Run(string id, FakeRegistry? reg = null, IDriveProvider? drives = null,
        FakeEventSource? events = null, TimeSpan? uptime = null) =>
        new HealthCheckRunner(reg ?? new FakeRegistry(), drives ?? new Drives(), events ?? new FakeEventSource().AddLog("System"),
            Info(uptime ?? TimeSpan.FromHours(2)), () => Now).RunAll().Single(r => r.Id == id);

    private static EventRecordLite Sys(long recordId, DateTime time, int id, string provider) =>
        new("System", recordId, time, id, EventLevel.Warning, provider, null, "PC", 0, 0);

    private static FakeEventSource SysEvents(params EventRecordLite[] events) => new FakeEventSource().AddLog("System", events);

    // ---- disk ----

    [Fact] public void Disk_NinePercent_IsProblem()
    {
        var r = Run("disk", drives: new Drives(("C:", 100, 9)));
        Assert.Equal(HealthStatus.Problem, r.Status);
        Assert.Equal("C: 9% free", r.Value);
        Assert.Equal("Low disk space", r.Label);
    }

    [Fact] public void Disk_TwelvePercent_IsWarning() => Assert.Equal(HealthStatus.Warning, Run("disk", drives: new Drives(("C:", 100, 12))).Status);

    [Fact] public void Disk_FortyPercent_IsOk() => Assert.Equal(HealthStatus.Ok, Run("disk", drives: new Drives(("C:", 100, 40))).Status);

    [Fact] public void Disk_SeveralDrives_ReportsTheWorst()
    {
        var r = Run("disk", drives: new Drives(("C:", 100, 40), ("D:", 100, 5), ("E:", 100, 20)));
        Assert.Equal("D: 5% free", r.Value);
        Assert.Equal(HealthStatus.Problem, r.Status);
    }

    // ---- reboot ----

    [Fact] public void Reboot_ComponentServicingKey_IsWarning()
    {
        var r = Run("reboot", reg: new FakeRegistry().Key(RebootPending));
        Assert.Equal(HealthStatus.Warning, r.Status);
        Assert.Equal("Yes", r.Value);
    }

    [Fact] public void Reboot_WindowsUpdateKey_IsWarning() =>
        Assert.Equal(HealthStatus.Warning, Run("reboot", reg: new FakeRegistry().Key(RebootRequired)).Status);

    [Fact] public void Reboot_PendingFileRenames_IsWarning() =>
        Assert.Equal(HealthStatus.Warning, Run("reboot", reg: new FakeRegistry().Set(SessionManager, "PendingFileRenameOperations", new[] { @"\??\C:\a", "" })).Status);

    [Fact] public void Reboot_EmptyFileRenames_IsOk() =>
        Assert.Equal(HealthStatus.Ok, Run("reboot", reg: new FakeRegistry().Set(SessionManager, "PendingFileRenameOperations", Array.Empty<string>())).Status);

    [Fact] public void Reboot_Nothing_IsOk()
    {
        var r = Run("reboot");
        Assert.Equal(HealthStatus.Ok, r.Status);
        Assert.Equal("No", r.Value);
    }

    // ---- time sync ----

    private const string TimeService = "Microsoft-Windows-Time-Service";

    [Fact] public void TimeSync_RecentFailure_IsWarning()
    {
        var r = Run("timesync", events: SysEvents(Sys(1, Now.AddHours(-3), 36, TimeService)));
        Assert.Equal(HealthStatus.Warning, r.Status);
        Assert.Equal("Not synced recently", r.Value);
    }

    [Fact] public void TimeSync_RecentSuccess_IsOk()
    {
        var r = Run("timesync", events: SysEvents(Sys(1, Now.AddDays(-2), 37, TimeService)));
        Assert.Equal(HealthStatus.Ok, r.Status);
        Assert.Equal("In sync", r.Value);
    }

    [Fact] public void TimeSync_OldFailureAndRecentSuccess_IsOk() =>
        Assert.Equal(HealthStatus.Ok, Run("timesync", events: SysEvents(Sys(1, Now.AddDays(-3), 36, TimeService), Sys(2, Now.AddDays(-1), 35, TimeService))).Status);

    [Fact] public void TimeSync_NoEvents_IsUnknown()
    {
        var r = Run("timesync");
        Assert.Equal(HealthStatus.Unknown, r.Status);
        Assert.Equal("No data", r.Value);
    }


    private const string WuClient = "Microsoft-Windows-WindowsUpdateClient";

    [Fact] public void Update_SixDaysAgo_IsOk()
    {
        var r = Run("update", events: SysEvents(Sys(1, Now.AddDays(-6), 19, WuClient)));
        Assert.Equal(HealthStatus.Ok, r.Status);
        Assert.Equal("6 days ago", r.Value);
    }

    [Fact] public void Update_FortyDaysAgo_IsWarning() =>
        Assert.Equal(HealthStatus.Warning, Run("update", events: SysEvents(Sys(1, Now.AddDays(-40), 19, WuClient))).Status);

    [Fact] public void Update_SeventyDaysAgo_IsProblem() =>
        Assert.Equal(HealthStatus.Problem, Run("update", events: SysEvents(Sys(1, Now.AddDays(-70), 19, WuClient))).Status);

    [Fact] public void Update_UsesNewestEvent19() =>
        Assert.Equal("2 days ago", Run("update", events: SysEvents(Sys(1, Now.AddDays(-50), 19, WuClient), Sys(2, Now.AddDays(-2), 19, WuClient))).Value);

    [Fact] public void Update_None_IsUnknown() => Assert.Equal(HealthStatus.Unknown, Run("update").Status);

    // ---- uptime ----

    [Fact] public void Uptime_UncleanBoot_IsWarning()
    {
        var uptime = TimeSpan.FromHours(7) + TimeSpan.FromMinutes(20);
        var boot = Now - uptime;
        var r = Run("uptime", uptime: uptime, events: SysEvents(Sys(1, boot.AddMinutes(1), 41, "Microsoft-Windows-Kernel-Power")));
        Assert.Equal(HealthStatus.Warning, r.Status);
        Assert.Equal("7 h 20 min", r.Value);
        Assert.Equal("Last restart was unexpected", r.Detail);
    }

    [Fact] public void Uptime_Event6008_AlsoCountsAsUnclean()
    {
        var uptime = TimeSpan.FromHours(3);
        var r = Run("uptime", uptime: uptime, events: SysEvents(Sys(1, Now - uptime, 6008, "EventLog")));
        Assert.Equal(HealthStatus.Warning, r.Status);
    }

    [Fact] public void Uptime_OldUncleanEvent_IsIgnored()
    {
        var uptime = TimeSpan.FromHours(3);
        var r = Run("uptime", uptime: uptime, events: SysEvents(Sys(1, Now.AddDays(-4), 41, "Microsoft-Windows-Kernel-Power")));
        Assert.Equal(HealthStatus.Ok, r.Status);
    }

    [Fact] public void Uptime_Days_AreShown() =>
        Assert.Equal("3 d 4 h", Run("uptime", uptime: TimeSpan.FromHours(76)).Value);

    // ---- runner ----

    [Fact] public void ProviderThrowing_GivesUnknownUnavailable()
    {
        var r = Run("disk", drives: new ThrowingDrives());
        Assert.Equal(HealthStatus.Unknown, r.Status);
        Assert.Equal("Unavailable", r.Value);
    }

    [Fact] public void LogAccessDenied_GivesUnknownUnavailable()
    {
        var r = Run("update", events: new FakeEventSource().Deny("System"));
        Assert.Equal(HealthStatus.Unknown, r.Status);
    }

    [Fact] public void RunAll_ReturnsFiveChecksInOrder() => Assert.Equal(
        ["disk", "reboot", "timesync", "update", "uptime"],
        new HealthCheckRunner(new FakeRegistry(), new Drives(), new FakeEventSource().AddLog("System"), Info(TimeSpan.FromHours(1)), () => Now)
            .RunAll().Select(r => r.Id));

    [Fact] public void RunAll_Labels() => Assert.Equal(
        ["Low disk space", "Pending reboot", "Time sync", "Last update", "Uptime"],
        new HealthCheckRunner(new FakeRegistry(), new Drives(), new FakeEventSource().AddLog("System"), Info(TimeSpan.FromHours(1)), () => Now)
            .RunAll().Select(r => r.Label));
}
