using System.Globalization;

namespace HvViewer.App.Services;

/// <summary>Works out Hyper-V Viewer's own CPU and memory use for the status bar, e.g. "CPU 1% · 84 MB".</summary>
public sealed class FootprintMonitor(Func<TimeSpan> cpuTime, Func<long> workingSetBytes, Func<DateTime> now, int cores)
{
    private TimeSpan _lastCpu = cpuTime();
    private DateTime _lastTime = now();
    private int _lastPercent;

    public string Sample()
    {
        var time = now();
        var elapsed = time - _lastTime;
        if (elapsed > TimeSpan.Zero)
        {
            var cpu = cpuTime();
            var used = (cpu - _lastCpu).TotalSeconds / (elapsed.TotalSeconds * Math.Max(1, cores)) * 100;
            _lastPercent = (int)Math.Round(Math.Clamp(used, 0, 100));
            _lastCpu = cpu;
            _lastTime = time;
        }

        var megabytes = (long)Math.Round(workingSetBytes() / 1024.0 / 1024.0);
        return string.Create(CultureInfo.InvariantCulture, $"CPU {_lastPercent}% · {megabytes} MB");
    }
}
