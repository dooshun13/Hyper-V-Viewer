namespace HvViewer.Machine;

/// <summary>One memory stick.</summary>
public sealed record MemoryModule(long CapacityBytes, int SpeedMHz, string Type);

/// <summary>The processor and memory of this PC.</summary>
public sealed record HardwareInfo(
    string CpuName,
    int Cores,
    int Threads,
    double MaxGHz,
    long TotalRamBytes,
    IReadOnlyList<MemoryModule> Modules);

/// <summary>Reads hardware details. Read-only.</summary>
public interface IHardwareProvider
{
    /// <summary>Processor and memory details, or null when Windows cannot say.</summary>
    HardwareInfo? Read();

    /// <summary>Memory that is free right now, in bytes (changes all the time, so it is read separately).</summary>
    long? AvailableRamBytes();
}
