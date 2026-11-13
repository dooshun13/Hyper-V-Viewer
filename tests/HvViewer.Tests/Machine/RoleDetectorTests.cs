using HvViewer.Machine;
using HvViewer.Tests.Fakes;
using Xunit;

namespace HvViewer.Tests.Machine;

public class RoleDetectorTests
{
    private const string Services = @"SYSTEM\CurrentControlSet\Services";
    private const string ProductOptions = @"SYSTEM\CurrentControlSet\Control\ProductOptions";
    private const string Printers = @"SYSTEM\CurrentControlSet\Control\Print\Printers";

    private static FakeRegistry WithService(string name, int start = 2) =>
        new FakeRegistry().Set($@"{Services}\{name}", "Start", start);

    [Theory]
    [InlineData("W3SVC", "IIS web server")]
    [InlineData("DNS", "DNS server")]
    [InlineData("DHCPServer", "DHCP server")]
    [InlineData("CertSvc", "Certificate Services")]
    [InlineData("MSSQLSERVER", "SQL Server")]
    [InlineData("TermServLicensing", "RD Licensing")]
    [InlineData("Tssdis", "RD Connection Broker")]
    [InlineData("WsusService", "WSUS")]
    [InlineData("vmms", "Hyper-V")]
    [InlineData("ClusSvc", "Failover Clustering")]
    public void Detect_MapsServiceToRole(string service, string role) =>
        Assert.Equal([role], new RoleDetector(WithService(service)).Detect());

    [Fact] public void Detect_DisabledService_IsIgnored() =>
        Assert.Empty(new RoleDetector(WithService("W3SVC", start: 4)).Detect());

    [Fact] public void Detect_ServiceWithoutStartValue_CountsAsPresent() =>
        Assert.Equal(["IIS web server"], new RoleDetector(new FakeRegistry().Key($@"{Services}\W3SVC")).Detect());

    [Fact] public void Detect_NamedSqlInstance_ReportedOnce()
    {
        var reg = WithService("MSSQL$SQLEXPRESS").Set($@"{Services}\MSSQL$OTHER", "Start", 2).Set($@"{Services}\MSSQLSERVER", "Start", 2);
        Assert.Equal(["SQL Server"], new RoleDetector(reg).Detect());
    }

    [Fact] public void Detect_OutputFollowsMapOrder()
    {
        var reg = WithService("vmms").Set($@"{Services}\W3SVC", "Start", 2).Set($@"{Services}\DNS", "Start", 2);
        Assert.Equal(["IIS web server", "DNS server", "Hyper-V"], new RoleDetector(reg).Detect());
    }

    [Fact] public void Detect_PrintServer_OnServerWithPrinters()
    {
        var reg = WithService("Spooler").Set(ProductOptions, "ProductType", "ServerNT").Key($@"{Printers}\Office Printer");
        Assert.Equal(["Print server"], new RoleDetector(reg).Detect());
    }

    [Fact] public void Detect_Spooler_OnWorkstation_IsNotAPrintServer()
    {
        var reg = WithService("Spooler").Set(ProductOptions, "ProductType", "WinNT").Key($@"{Printers}\Office Printer");
        Assert.Empty(new RoleDetector(reg).Detect());
    }

    [Fact] public void Detect_Spooler_OnServerWithoutPrinters_IsNotAPrintServer()
    {
        var reg = WithService("Spooler").Set(ProductOptions, "ProductType", "ServerNT");
        Assert.Empty(new RoleDetector(reg).Detect());
    }

    // The NTDS service key also exists on ordinary Windows, so it only means "domain controller" on a domain controller.
    [Theory]
    [InlineData("LanmanNT", true)]
    [InlineData("ServerNT", false)]
    [InlineData("WinNT", false)]
    public void Detect_Ntds_OnlyCountsOnADomainController(string productType, bool expected)
    {
        var reg = WithService("NTDS").Set(ProductOptions, "ProductType", productType);
        Assert.Equal(expected, new RoleDetector(reg).Detect().Contains("AD domain controller"));
    }

    [Fact] public void Detect_NothingInstalled_ReturnsEmpty() => Assert.Empty(new RoleDetector(new FakeRegistry()).Detect());
}
