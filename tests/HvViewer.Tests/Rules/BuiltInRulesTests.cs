using HvViewer.Rules;
using Xunit;

namespace HvViewer.Tests.Rules;

public class BuiltInRulesTests
{
    private static readonly string[] ExpectedIds =
    [
        "boot-shutdown", "crashes-hangs", "disk-storage", "logons-accounts", "windows-update", "services",
        "memory-performance", "network-dns", "group-policy", "rdp", "printing",
    ];

    // The event table from the plan (Task 17). Format: troubleshooter | log | provider | id:severity ...
    // Ruling: Ntfs 98 is "volume is healthy" (informational), so it is info here, not warning.
    public static readonly TheoryData<string, string, string, int, RuleSeverity> Table = Build(
        "boot-shutdown|System|Microsoft-Windows-Kernel-Power|41:critical",
        "boot-shutdown|System|EventLog|6008:error 6005:info 6006:info",
        "boot-shutdown|System|Microsoft-Windows-WER-SystemErrorReporting|1001:critical",
        "boot-shutdown|System|User32|1074:info",
        "boot-shutdown|System|Microsoft-Windows-Kernel-General|12:info 13:info",
        "boot-shutdown|Microsoft-Windows-Diagnostics-Performance/Operational|Microsoft-Windows-Diagnostics-Performance|100:warning 200:warning",
        "crashes-hangs|Application|Application Error|1000:error",
        "crashes-hangs|Application|Application Hang|1002:error",
        "crashes-hangs|Application|Windows Error Reporting|1001:info",
        "crashes-hangs|Application|.NET Runtime|1026:error",
        "disk-storage|System|disk|7:error 11:error 51:warning 153:warning",
        "disk-storage|System|Microsoft-Windows-Ntfs|55:error 98:info 140:warning",
        "disk-storage|System|storahci|129:warning",
        "disk-storage|System|stornvme|129:warning",
        "disk-storage|System|LSI_SAS|129:warning",
        "disk-storage|System|srv|2013:warning",
        "logons-accounts|Security|Microsoft-Windows-Security-Auditing|4625:warning 4740:error 4771:warning 4776:info 4672:info 4720:info 4724:info 4728:info 4732:info",
        "logons-accounts|Security|Microsoft-Windows-Eventlog|1102:critical",
        "windows-update|System|Microsoft-Windows-WindowsUpdateClient|19:info 20:error 43:info",
        "windows-update|Setup|Microsoft-Windows-Servicing|2:info 4:warning",
        "services|System|Service Control Manager|7000:error 7001:error 7009:error 7011:error 7022:error 7023:error 7024:error 7031:error 7034:error 7040:info 7045:warning",
        "memory-performance|System|Microsoft-Windows-Resource-Exhaustion-Detector|2004:error",
        "memory-performance|System|Microsoft-Windows-Kernel-Processor-Power|37:warning",
        "memory-performance|System|Microsoft-Windows-WHEA-Logger|1:warning 17:warning 18:critical 19:warning 47:warning",
        "network-dns|System|Microsoft-Windows-DNS-Client|1014:warning",
        "network-dns|System|Tcpip|4199:error",
        "network-dns|System|NETLOGON|5719:error 3210:error",
        "network-dns|System|Microsoft-Windows-Time-Service|36:warning 129:warning",
        "network-dns|System|Schannel|36874:warning 36887:warning",
        "group-policy|System|Microsoft-Windows-GroupPolicy|1030:error 1053:error 1055:error 1058:error 1085:warning 1096:error 1129:error",
        "rdp|Microsoft-Windows-TerminalServices-LocalSessionManager/Operational|Microsoft-Windows-TerminalServices-LocalSessionManager|21:info 23:info 24:info 25:info 40:info",
        "rdp|Microsoft-Windows-TerminalServices-RemoteConnectionManager/Operational|Microsoft-Windows-TerminalServices-RemoteConnectionManager|1149:info",
        "printing|Microsoft-Windows-PrintService/Admin|Microsoft-Windows-PrintService|372:error 808:error");

    private static TheoryData<string, string, string, int, RuleSeverity> Build(params string[] lines)
    {
        var data = new TheoryData<string, string, string, int, RuleSeverity>();
        foreach (var line in lines)
        {
            var parts = line.Split('|');
            foreach (var item in parts[3].Split(' '))
            {
                var pair = item.Split(':');
                data.Add(parts[0], parts[1], parts[2], int.Parse(pair[0]), Enum.Parse<RuleSeverity>(pair[1], ignoreCase: true));
            }
        }
        return data;
    }

    private static readonly RuleSet Set = RuleLoader.LoadBuiltIn();

    [Fact] public void LoadBuiltIn_Has11Troubleshooters_NoErrors()
    {
        Assert.Empty(Set.Errors);
        Assert.Equal(11, Set.Troubleshooters.Count);
    }

    [Fact] public void Ids_MatchFileNamesAndOrder() => Assert.Equal(ExpectedIds, Set.Troubleshooters.Select(t => t.Id));

    [Theory, MemberData(nameof(Table))]
    public void EveryTableRow_IsPresent(string troubleshooter, string log, string provider, int id, RuleSeverity severity)
    {
        var t = Assert.Single(Set.Troubleshooters, x => x.Id == troubleshooter);
        var rule = Assert.Single(t.Events, e => e.Id == id && e.Provider == provider && e.Log == log);
        Assert.Equal(severity, rule.Severity);
    }

    [Fact] public void NoRulesBeyondTheTable() =>
        Assert.Equal(Table.Count, Set.Troubleshooters.Sum(t => t.Events.Count));

    [Fact] public void Titles_AtMost60Chars() =>
        Assert.All(Set.Troubleshooters.SelectMany(t => t.Events), e => Assert.True(e.Title.Length <= 60, $"{e.Provider} {e.Id}: '{e.Title}' is {e.Title.Length} chars"));

    [Fact] public void Docs_PointToLearnSearchUnlessVerified() =>
        Assert.All(Set.Troubleshooters.SelectMany(t => t.Events), e =>
            Assert.StartsWith("https://learn.microsoft.com/", e.Docs));

    [Fact] public void Texts_AreShortAndPlain() =>
        Assert.All(Set.Troubleshooters.SelectMany(t => t.Events), e =>
        {
            foreach (var text in new[] { e.Meaning, e.Causes, e.Next })
                Assert.InRange(text.Length, 15, 320);
        });
}
