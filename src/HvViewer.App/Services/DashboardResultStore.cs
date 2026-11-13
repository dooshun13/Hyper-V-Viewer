using HvViewer.Core.Model;
using HvViewer.Rules;

namespace HvViewer.App.Services;

/// <summary>Holds the latest dashboard result so other screens (Troubleshooters) can show the same counts and period.</summary>
public sealed class DashboardResultStore
{
    public DashboardResult? Latest { get; private set; }

    public TimeRange? Range { get; private set; }

    public TimeSpan? LastSpan { get; private set; }

    public event Action? Changed;

    public void Publish(DashboardResult result, TimeRange range, TimeSpan? lastSpan)
    {
        Latest = result;
        Range = range;
        LastSpan = lastSpan;
        Changed?.Invoke();
    }
}
