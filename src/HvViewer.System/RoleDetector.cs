using global::System.Globalization;

namespace HvViewer.Machine;

/// <summary>Finds server roles from the service registry keys. Detection only.</summary>
public sealed class RoleDetector(IRegistryReader reg)
{
    private const string ServicesKey = @"SYSTEM\CurrentControlSet\Services";
    private const string ProductOptionsKey = @"SYSTEM\CurrentControlSet\Control\ProductOptions";
    private const string PrintersKey = @"SYSTEM\CurrentControlSet\Control\Print\Printers";
    private const int StartDisabled = 4;

    // Service name -> role, in output order.
    private static readonly (string Service, string Role)[] Map =
    [
        ("W3SVC", "IIS web server"),
        ("NTDS", "AD domain controller"),
        ("DNS", "DNS server"),
        ("DHCPServer", "DHCP server"),
        ("CertSvc", "Certificate Services"),
        ("MSSQLSERVER", "SQL Server"),
        ("TermServLicensing", "RD Licensing"),
        ("Tssdis", "RD Connection Broker"),
        ("WsusService", "WSUS"),
        ("vmms", "Hyper-V"),
        ("ClusSvc", "Failover Clustering"),
    ];

    public IReadOnlyList<string> Detect()
    {
        var installed = reg.GetSubKeyNames(ServicesKey).Where(IsEnabled).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var roles = new List<string>();

        foreach (var (service, role) in Map)
        {
            var present = installed.Contains(service)
                          || (service == "MSSQLSERVER" && installed.Any(s => s.StartsWith("MSSQL$", StringComparison.OrdinalIgnoreCase)));

            // The NTDS key exists on ordinary Windows too; only a real domain controller is a domain controller.
            if (service == "NTDS" && !IsDomainController()) present = false;
            if (present && !roles.Contains(role)) roles.Add(role);
        }

        if (installed.Contains("Spooler") && IsServer() && reg.GetSubKeyNames(PrintersKey).Count > 0) roles.Add("Print server");
        return roles;
    }

    private bool IsEnabled(string service)
    {
        var start = reg.GetValue($@"{ServicesKey}\{service}", "Start");
        return start is null || Convert.ToInt32(start, CultureInfo.InvariantCulture) != StartDisabled;
    }

    private bool IsDomainController() =>
        Convert.ToString(reg.GetValue(ProductOptionsKey, "ProductType"), CultureInfo.InvariantCulture) == "LanmanNT";

    private bool IsServer() =>
        Convert.ToString(reg.GetValue(ProductOptionsKey, "ProductType"), CultureInfo.InvariantCulture) is "ServerNT" or "LanmanNT";
}
