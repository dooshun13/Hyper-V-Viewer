namespace HvViewer.Machine;

public enum MachineKind { Workstation, Server, DomainController }

/// <summary>What kind of machine this is, read from the registry.</summary>
public sealed record SystemInfo(
    string ComputerName,
    string ProductName,
    string Edition,
    string Version,
    string Build,
    MachineKind Kind,
    bool IsServerCore,
    bool IsVirtual,
    string? VirtualPlatform,
    string? Domain,
    DateTime BootTimeUtc,
    TimeSpan Uptime);
