using HvViewer.App.Services;
using HvViewer.Core.Model;
using HvViewer.Core.Settings;
using Xunit;

namespace HvViewer.App.Tests;

public sealed class AppStateTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "evt-appstate-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private AppState State(bool admin = false) => new(new SettingsStore(_dir), admin, () => Now);

    [Fact] public void Settings_StartFromStore()
    {
        new SettingsStore(_dir).Save(new AppSettings { Theme = ThemeMode.Light });
        Assert.Equal(ThemeMode.Light, State().Settings.Theme);
    }

    [Fact] public void Update_SavesAndRaisesChanged()
    {
        var state = State();
        var raised = 0;
        state.Changed += () => raised++;

        state.Update(s => s with { Time = TimeMode.Utc });

        Assert.Equal(TimeMode.Utc, state.Settings.Time);
        Assert.Equal(TimeMode.Utc, new SettingsStore(_dir).Load().Time);
        Assert.Equal(1, raised);
        Assert.False(state.LastSaveFailed);
    }

    [Fact] public void Update_WhenSaveFails_StillAppliesAndFlags()
    {
        File.WriteAllText(_dir, "blocking file");
        var state = State();
        state.Update(s => s with { Theme = ThemeMode.Dark });
        Assert.Equal(ThemeMode.Dark, state.Settings.Theme);
        Assert.True(state.LastSaveFailed);
        File.Delete(_dir);
    }

    [Fact] public void Zone_FollowsTimeMode()
    {
        var state = State();
        Assert.Equal(TimeZoneInfo.Local, state.Zone);
        state.Update(s => s with { Time = TimeMode.Utc });
        Assert.Equal(TimeZoneInfo.Utc, state.Zone);
    }

    [Fact] public void FormatTime_UsesIsoInTheChosenZone()
    {
        var state = State();
        state.Update(s => s with { Time = TimeMode.Utc });
        Assert.Equal("2026-11-11 12:00:00", state.FormatTime(Now));
    }

    [Fact] public void DefaultLogs_ExcludeSecurityForStandardUser() =>
        Assert.Equal(["System", "Application"], State(admin: false).DefaultLogs);

    [Fact] public void DefaultLogs_IncludeSecurityForAdmin() =>
        Assert.Equal(["System", "Application", "Security"], State(admin: true).DefaultLogs);

    [Theory]
    [InlineData("1h", 1)]
    [InlineData("24h", 24)]
    [InlineData("7d", 168)]
    public void RangeFor_KnownKeys(string key, int hours) =>
        Assert.Equal(TimeRange.Last(TimeSpan.FromHours(hours), Now), State().RangeFor(key));

    [Fact] public void RangeFor_UnknownKey_Is24Hours() =>
        Assert.Equal(TimeRange.Last(TimeSpan.FromHours(24), Now), State().RangeFor("nonsense"));

    [Fact] public void NewQuery_UsesDefaultsAndText()
    {
        var q = State(admin: true).NewQuery("disk");
        Assert.Equal("disk", q.Text);
        Assert.Equal(["System", "Application", "Security"], q.Logs);
        Assert.Equal(TimeRange.Last(TimeSpan.FromHours(24), Now), q.Range);
        Assert.Equal(TimeSpan.FromHours(24), q.LastSpan);
        Assert.Empty(q.Validate());
    }

    [Fact] public void NewQuery_FollowsDefaultRangeSetting()
    {
        var state = State();
        state.Update(s => s with { DefaultRange = "7d" });
        Assert.Equal(TimeSpan.FromDays(7), state.NewQuery().LastSpan);
    }
}
