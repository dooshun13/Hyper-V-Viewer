using global::System.Globalization;
using global::System.Management;
using global::System.Runtime.Versioning;

namespace HvViewer.Machine;

/// <summary>Reads the processor and memory with small, read-only WMI queries (never Win32_Product).</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsHardwareProvider : IHardwareProvider
{
    public HardwareInfo? Read()
    {
        try
        {
            string name = "";
            int cores = 0, threads = 0, maxMhz = 0;
            foreach (var cpu in Query("SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor"))
            {
                if (name.Length == 0) name = Text(cpu, "Name");
                cores += Number(cpu, "NumberOfCores");
                threads += Number(cpu, "NumberOfLogicalProcessors");
                maxMhz = Math.Max(maxMhz, Number(cpu, "MaxClockSpeed"));
            }
            if (threads == 0) threads = Environment.ProcessorCount;

            var modules = new List<MemoryModule>();
            foreach (var m in Query("SELECT Capacity, Speed, ConfiguredClockSpeed, SMBIOSMemoryType FROM Win32_PhysicalMemory"))
            {
                var speed = Math.Max(Number(m, "Speed"), Number(m, "ConfiguredClockSpeed"));
                modules.Add(new MemoryModule(Long(m, "Capacity"), speed, HardwareText.MemoryType(Number(m, "SMBIOSMemoryType"))));
            }

            // "Visible" memory is what Windows can use (a little less than what is installed).
            var total = Query("SELECT TotalVisibleMemorySize FROM Win32_OperatingSystem")
                .Select(os => Long(os, "TotalVisibleMemorySize") * 1024).FirstOrDefault();
            if (total <= 0) total = modules.Sum(m => m.CapacityBytes);

            return new HardwareInfo(HardwareText.CpuName(name), cores, threads, maxMhz / 1000.0, total, modules);
        }
        catch (Exception ex) when (ex is ManagementException or global::System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public long? AvailableRamBytes()
    {
        try
        {
            var kilobytes = Query("SELECT FreePhysicalMemory FROM Win32_OperatingSystem").Select(os => Long(os, "FreePhysicalMemory")).FirstOrDefault();
            return kilobytes > 0 ? kilobytes * 1024 : null;
        }
        catch (Exception ex) when (ex is ManagementException or global::System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static List<ManagementBaseObject> Query(string wql)
    {
        using var searcher = new ManagementObjectSearcher(wql);
        using var results = searcher.Get();
        return results.Cast<ManagementBaseObject>().ToList();
    }

    private static string Text(ManagementBaseObject o, string property) => Convert.ToString(o[property], CultureInfo.InvariantCulture) ?? "";

    private static int Number(ManagementBaseObject o, string property) => (int)Math.Min(int.MaxValue, Long(o, property));

    private static long Long(ManagementBaseObject o, string property) =>
        o[property] is null ? 0 : Convert.ToInt64(o[property], CultureInfo.InvariantCulture);
}
