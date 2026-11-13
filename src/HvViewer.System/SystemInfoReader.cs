using global::System.Globalization;

namespace HvViewer.Machine;

/// <summary>Builds a <see cref="SystemInfo"/> from registry reads only.</summary>
public sealed class SystemInfoReader(IRegistryReader reg, Func<DateTime> utcNow, Func<long> tickCount64, Func<string> computerName)
{
    private const string CurrentVersionKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
    private const string ProductOptionsKey = @"SYSTEM\CurrentControlSet\Control\ProductOptions";
    private const string BiosKey = @"HARDWARE\DESCRIPTION\System\BIOS";
    private const string TcpipKey = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters";
    private const string Unknown = "Unknown";
    private const int Windows11FirstBuild = 22000;

    public SystemInfo Read()
    {
        var buildNumber = Text(CurrentVersionKey, "CurrentBuildNumber");
        var ubr = Text(CurrentVersionKey, "UBR");

        var uptime = TimeSpan.FromMilliseconds(tickCount64());
        var (isVirtual, platform) = DetectVirtual(Text(BiosKey, "SystemManufacturer") ?? "", Text(BiosKey, "SystemProductName") ?? "");
        var domain = Text(TcpipKey, "Domain");

        return new SystemInfo(
            ComputerName: computerName(),
            ProductName: FixProductName(Text(CurrentVersionKey, "ProductName"), buildNumber),
            Edition: Text(CurrentVersionKey, "EditionID") ?? Unknown,
            Version: Text(CurrentVersionKey, "DisplayVersion") ?? Text(CurrentVersionKey, "ReleaseId") ?? Text(CurrentVersionKey, "CurrentVersion") ?? Unknown,
            Build: buildNumber is null ? Unknown : ubr is null ? buildNumber : $"{buildNumber}.{ubr}",
            Kind: Text(ProductOptionsKey, "ProductType") switch
            {
                "ServerNT" => MachineKind.Server,
                "LanmanNT" => MachineKind.DomainController,
                _ => MachineKind.Workstation,
            },
            IsServerCore: string.Equals(Text(CurrentVersionKey, "InstallationType"), "Server Core", StringComparison.OrdinalIgnoreCase),
            IsVirtual: isVirtual,
            VirtualPlatform: platform,
            Domain: domain,
            BootTimeUtc: utcNow() - uptime,
            Uptime: uptime);
    }

    /// <summary>Windows 11 still says "Windows 10" in ProductName.</summary>
    private static string FixProductName(string? productName, string? buildNumber)
    {
        if (productName is null) return Unknown;
        var isWindows11 = int.TryParse(buildNumber, NumberStyles.None, CultureInfo.InvariantCulture, out var build) && build >= Windows11FirstBuild;
        return isWindows11 && productName.StartsWith("Windows 10", StringComparison.OrdinalIgnoreCase)
            ? "Windows 11" + productName["Windows 10".Length..]
            : productName;
    }

    private static (bool IsVirtual, string? Platform) DetectVirtual(string manufacturer, string product)
    {
        static bool Has(string s, string part) => s.Contains(part, StringComparison.OrdinalIgnoreCase);

        if (Has(manufacturer, "VMware")) return (true, "VMware");
        if (Has(manufacturer, "Microsoft") && Has(product, "Virtual Machine")) return (true, "Hyper-V");
        if (Has(manufacturer, "QEMU")) return (true, "QEMU/KVM");
        if (Has(manufacturer, "Xen")) return (true, "Xen");
        if (Has(manufacturer, "innotek") || Has(product, "VirtualBox")) return (true, "VirtualBox");
        if (Has(manufacturer, "Amazon EC2")) return (true, "AWS");
        if (Has(manufacturer, "Google") && Has(product, "Google Compute Engine")) return (true, "Google Cloud");
        return (false, null);
    }

    /// <summary>The value as text, or null when missing or empty.</summary>
    private string? Text(string key, string name)
    {
        var value = Convert.ToString(reg.GetValue(key, name), CultureInfo.InvariantCulture);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
