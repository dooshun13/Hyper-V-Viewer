using System.Runtime.Versioning;
using HvViewer.Machine;
using Xunit;

namespace HvViewer.Tests.Machine;

public class HardwareTextTests
{
    private const long Gb = 1024L * 1024 * 1024;

    private static HardwareInfo Info(int cores = 6, int threads = 12, double ghz = 3.6, long ram = 32 * Gb, params MemoryModule[] modules) =>
        new("AMD Ryzen 5 5500", cores, threads, ghz, ram, modules);

    [Fact] public void CpuDetail_ListsCoresThreadsAndClock() =>
        Assert.Equal("6 cores · 12 threads · 3.6 GHz", HardwareText.CpuDetail(Info()));

    [Fact] public void CpuDetail_UsesSingularWords() =>
        Assert.Equal("1 core · 1 thread · 2.4 GHz", HardwareText.CpuDetail(Info(cores: 1, threads: 1, ghz: 2.4)));

    [Fact] public void CpuDetail_LeavesOutAClockItDoesNotKnow() =>
        Assert.Equal("4 cores · 8 threads", HardwareText.CpuDetail(Info(cores: 4, threads: 8, ghz: 0)));

    [Fact] public void CpuDetail_UsesTheThreadCountWhenCoresAreUnknown() =>
        Assert.Equal("8 threads · 3.6 GHz", HardwareText.CpuDetail(Info(cores: 0, threads: 8)));

    [Theory]
    [InlineData("  AMD Ryzen 5 5500                ", "AMD Ryzen 5 5500")]
    [InlineData("Intel(R) Core(TM) i7-9700K CPU @ 3.60GHz", "Intel Core i7-9700K CPU @ 3.60GHz")]
    [InlineData("  ", "Unknown processor")]
    public void CpuName_IsTidied(string raw, string expected) => Assert.Equal(expected, HardwareText.CpuName(raw));

    [Theory]
    [InlineData(34_100_000_000L, "31.8 GB")]
    [InlineData(16L * Gb, "16 GB")]
    [InlineData(8L * Gb + Gb / 2, "8.5 GB")]
    public void Size_IsInGigabytes(long bytes, string expected) => Assert.Equal(expected, HardwareText.Size(bytes));

    [Fact] public void UsedPercent_IsRoundedAndClamped()
    {
        Assert.Equal(45, HardwareText.UsedPercent(total: 100, available: 55));
        Assert.Equal(0, HardwareText.UsedPercent(total: 100, available: 120));
        Assert.Equal(100, HardwareText.UsedPercent(total: 100, available: -5));
        Assert.Equal(0, HardwareText.UsedPercent(total: 0, available: 0));
    }

    [Theory]
    [InlineData(26, "DDR4")]
    [InlineData(34, "DDR5")]
    [InlineData(24, "DDR3")]
    [InlineData(21, "DDR2")]
    [InlineData(0, "")]
    [InlineData(2, "")]
    public void MemoryType_NamesTheGeneration(int smbios, string expected) => Assert.Equal(expected, HardwareText.MemoryType(smbios));

    [Fact] public void Modules_IdenticalSticksAreCounted() => Assert.Equal("2 × 16 GB · DDR4 · 2133 MHz",
        HardwareText.Modules([new MemoryModule(16 * Gb, 2133, "DDR4"), new MemoryModule(16 * Gb, 2133, "DDR4")]));

    [Fact] public void Modules_DifferentSticksAreListed() => Assert.Equal("16 GB + 8 GB · DDR4 · 2666 MHz",
        HardwareText.Modules([new MemoryModule(16 * Gb, 2666, "DDR4"), new MemoryModule(8 * Gb, 2666, "DDR4")]));

    [Fact] public void Modules_OneStick() => Assert.Equal("1 × 8 GB · DDR5 · 4800 MHz",
        HardwareText.Modules([new MemoryModule(8 * Gb, 4800, "DDR5")]));

    [Fact] public void Modules_LeavesOutWhatIsUnknown() => Assert.Equal("2 × 8 GB",
        HardwareText.Modules([new MemoryModule(8 * Gb, 0, ""), new MemoryModule(8 * Gb, 0, "")]));

    [Fact] public void Modules_NoneKnown_IsEmpty() => Assert.Equal("", HardwareText.Modules([]));
}

[SupportedOSPlatform("windows")]
public class WindowsHardwareProviderTests
{
    [WindowsFact]
    public void Read_ReturnsThisPcsProcessorAndMemory()
    {
        var info = new WindowsHardwareProvider().Read();

        Assert.NotNull(info);
        Assert.False(string.IsNullOrWhiteSpace(info.CpuName));
        Assert.True(info.Threads >= 1);
        Assert.InRange(info.Cores, 1, info.Threads);
        Assert.True(info.TotalRamBytes > 512L * 1024 * 1024);
    }

    [WindowsFact]
    public void AvailableRam_IsBetweenZeroAndTotal()
    {
        var provider = new WindowsHardwareProvider();
        var info = provider.Read()!;
        var available = provider.AvailableRamBytes();

        Assert.NotNull(available);
        Assert.InRange(available.Value, 1, info.TotalRamBytes);
    }
}
