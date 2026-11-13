using HvViewer.Machine;
using HvViewer.Tests.Fakes;
using Xunit;

namespace HvViewer.Tests.Machine;

public class SystemInfoReaderTests
{
    private const string CurrentVersion = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
    private const string ProductOptions = @"SYSTEM\CurrentControlSet\Control\ProductOptions";
    private const string Bios = @"HARDWARE\DESCRIPTION\System\BIOS";
    private const string Tcpip = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters";

    private static readonly DateTime Now = new(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);

    private static SystemInfo Read(FakeRegistry reg, long tickMs = 0) =>
        new SystemInfoReader(reg, () => Now, () => tickMs, () => "PC-01").Read();

    private static FakeRegistry Windows11() => new FakeRegistry()
        .Set(CurrentVersion, "ProductName", "Windows 10 Pro")
        .Set(CurrentVersion, "EditionID", "Professional")
        .Set(CurrentVersion, "DisplayVersion", "23H2")
        .Set(CurrentVersion, "CurrentBuildNumber", "22631")
        .Set(CurrentVersion, "UBR", 4037)
        .Set(CurrentVersion, "InstallationType", "Client")
        .Set(ProductOptions, "ProductType", "WinNT");

    [Fact] public void Read_Windows11_FixesProductName()
    {
        var info = Read(Windows11());
        Assert.Equal("Windows 11 Pro", info.ProductName);
        Assert.Equal("Professional", info.Edition);
        Assert.Equal("23H2", info.Version);
        Assert.Equal("PC-01", info.ComputerName);
        Assert.Equal(MachineKind.Workstation, info.Kind);
    }

    [Fact] public void Read_Windows10_KeepsProductName() =>
        Assert.Equal("Windows 10 Pro", Read(Windows11().Set(CurrentVersion, "CurrentBuildNumber", "19045")).ProductName);

    [Fact] public void Read_Server2012R2_UsesCurrentVersionFallback()
    {
        var reg = new FakeRegistry()
            .Set(CurrentVersion, "ProductName", "Windows Server 2012 R2 Standard")
            .Set(CurrentVersion, "CurrentVersion", "6.3")
            .Set(CurrentVersion, "CurrentBuildNumber", "9600");
        Assert.Equal("6.3", Read(reg).Version);
    }

    [Fact] public void Read_ReleaseId_UsedWhenNoDisplayVersion()
    {
        var reg = new FakeRegistry().Set(CurrentVersion, "ReleaseId", "1809").Set(CurrentVersion, "CurrentVersion", "6.3");
        Assert.Equal("1809", Read(reg).Version);
    }

    [Fact] public void Read_BuildIncludesUbr()
    {
        var reg = new FakeRegistry().Set(CurrentVersion, "CurrentBuildNumber", "20348").Set(CurrentVersion, "UBR", 2700);
        Assert.Equal("20348.2700", Read(reg).Build);
    }

    [Fact] public void Read_ServerCore_FromInstallationType()
    {
        var reg = new FakeRegistry().Set(CurrentVersion, "InstallationType", "Server Core").Set(ProductOptions, "ProductType", "ServerNT");
        var info = Read(reg);
        Assert.True(info.IsServerCore);
        Assert.Equal(MachineKind.Server, info.Kind);
    }

    [Fact] public void Read_DomainController_FromProductType() =>
        Assert.Equal(MachineKind.DomainController, Read(new FakeRegistry().Set(ProductOptions, "ProductType", "LanmanNT")).Kind);

    [Theory]
    [InlineData("VMware, Inc.", "VMware7,1", "VMware")]
    [InlineData("Microsoft Corporation", "Virtual Machine", "Hyper-V")]
    [InlineData("QEMU", "Standard PC (Q35 + ICH9, 2009)", "QEMU/KVM")]
    [InlineData("Xen", "HVM domU", "Xen")]
    [InlineData("innotek GmbH", "VirtualBox", "VirtualBox")]
    [InlineData("Amazon EC2", "t3.large", "AWS")]
    [InlineData("Google", "Google Compute Engine", "Google Cloud")]
    public void Read_VirtualPlatforms(string manufacturer, string product, string expected)
    {
        var info = Read(new FakeRegistry().Set(Bios, "SystemManufacturer", manufacturer).Set(Bios, "SystemProductName", product));
        Assert.True(info.IsVirtual);
        Assert.Equal(expected, info.VirtualPlatform);
    }

    [Theory]
    [InlineData("Dell Inc.", "PowerEdge R740")]
    [InlineData("Microsoft Corporation", "Surface Laptop 5")]
    public void Read_PhysicalMachines_AreNotVirtual(string manufacturer, string product)
    {
        var info = Read(new FakeRegistry().Set(Bios, "SystemManufacturer", manufacturer).Set(Bios, "SystemProductName", product));
        Assert.False(info.IsVirtual);
        Assert.Null(info.VirtualPlatform);
    }

    [Fact] public void Read_Domain_ReturnedWhenSet() =>
        Assert.Equal("corp.example.com", Read(new FakeRegistry().Set(Tcpip, "Domain", "corp.example.com")).Domain);

    [Fact] public void Read_NoDomain_IsNull()
    {
        Assert.Null(Read(new FakeRegistry()).Domain);
        Assert.Null(Read(new FakeRegistry().Set(Tcpip, "Domain", "")).Domain);
    }

    [Fact] public void Read_BootTime_FromTickCount()
    {
        var uptime = TimeSpan.FromHours(7) + TimeSpan.FromMinutes(20);
        var info = Read(new FakeRegistry(), (long)uptime.TotalMilliseconds);
        Assert.Equal(uptime, info.Uptime);
        Assert.Equal(Now - uptime, info.BootTimeUtc);
    }

    [Fact] public void Read_MissingValues_DoNotThrow()
    {
        var info = Read(new FakeRegistry());
        Assert.Equal("Unknown", info.ProductName);
        Assert.Equal("Unknown", info.Edition);
        Assert.Equal("Unknown", info.Version);
        Assert.Equal("Unknown", info.Build);
        Assert.False(info.IsServerCore);
        Assert.False(info.IsVirtual);
    }
}
