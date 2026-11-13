using CommunityToolkit.Mvvm.ComponentModel;
using HvViewer.App.Services;
using HvViewer.App.Shell;
using HvViewer.Core.Model;
using HvViewer.Core.Settings;
using Xunit;

namespace HvViewer.App.Tests;

public sealed class ShellViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "evt-shell-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private sealed class Page(string key) : ObservableObject
    {
        public string Key { get; } = key;
    }

    private sealed class EventsPage() : ObservableObject, IQueryTarget
    {
        public EventQuery? Loaded { get; private set; }

        public void LoadQuery(EventQuery query) => Loaded = query;
    }

    private (ShellViewModel Shell, AppState State, Dictionary<string, ObservableObject> Pages) Create(bool admin = false)
    {
        var state = new AppState(new SettingsStore(_dir), admin, () => new DateTime(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc));
        var pages = new Dictionary<string, ObservableObject>();
        var shell = new ShellViewModel(state, "PC-01", admin, key =>
        {
            if (!pages.TryGetValue(key, out var page)) pages[key] = page = key == "events" ? new EventsPage() : new Page(key);
            return page;
        });
        return (shell, state, pages);
    }

    [Fact] public void StartsOnHome()
    {
        var (shell, _, _) = Create();
        Assert.Equal("home", shell.CurrentKey);
        Assert.Equal("home", Assert.IsType<Page>(shell.CurrentPage).Key);
    }

    [Fact] public void Navigate_SetsCurrentPage()
    {
        var (shell, _, _) = Create();
        shell.NavigateCommand.Execute("saved");
        Assert.Equal("saved", shell.CurrentKey);
        Assert.Equal("saved", Assert.IsType<Page>(shell.CurrentPage).Key);
    }

    [Fact] public void Navigate_ReusesPageInstances()
    {
        var (shell, _, _) = Create();
        var home = shell.CurrentPage;
        shell.NavigateCommand.Execute("settings");
        shell.NavigateCommand.Execute("home");
        Assert.Same(home, shell.CurrentPage);
    }

    [Fact] public void Navigate_MarksOnlyTheCurrentItemSelected()
    {
        var (shell, _, _) = Create();
        shell.NavigateCommand.Execute("saved");
        Assert.Equal(["saved"], shell.NavItems.Where(i => i.IsSelected).Select(i => i.Key));
    }

    [Fact] public void NavItems_AreTheFourScreensInOrder() =>
        Assert.Equal(["home", "events", "saved", "settings"], Create().Shell.NavItems.Select(i => i.Key));

    [Fact] public void Navigate_UnknownKey_IsIgnored()
    {
        var (shell, _, _) = Create();
        shell.NavigateCommand.Execute("nowhere");
        Assert.Equal("home", shell.CurrentKey);
    }

    [Fact] public void IsAdmin_AndBadge_FromConstructor()
    {
        var admin = Create(admin: true).Shell;
        var user = Create(admin: false).Shell;
        Assert.True(admin.IsAdmin);
        Assert.Equal("Administrator", admin.AccessBadge);
        Assert.False(user.IsAdmin);
        Assert.Equal("Standard user", user.AccessBadge);
    }

    [Fact] public void MachineName_IsShown() => Assert.Equal("PC-01", Create().Shell.MachineName);

    [Fact] public void ShowEvents_NavigatesAndLoadsTheQuery()
    {
        var (shell, state, pages) = Create();
        var query = state.NewQuery("disk");

        shell.ShowEvents(query);

        Assert.Equal("events", shell.CurrentKey);
        Assert.Same(query, ((EventsPage)pages["events"]).Loaded);
    }

    [Fact] public void SubmitSearch_OpensEventsWithTheTypedText()
    {
        var (shell, _, pages) = Create();
        shell.SearchText = "Disk 3";
        shell.SubmitSearchCommand.Execute(null);
        Assert.Equal("events", shell.CurrentKey);
        Assert.Equal("Disk 3", ((EventsPage)pages["events"]).Loaded?.Text);
    }

    [Fact] public void SubmitSearch_EmptyText_DoesNothing()
    {
        var (shell, _, _) = Create();
        shell.SearchText = "   ";
        shell.SubmitSearchCommand.Execute(null);
        Assert.Equal("home", shell.CurrentKey);
    }

    [Fact] public void Time_AndTheme_AreSavedThroughAppState()
    {
        var (shell, state, _) = Create();
        shell.Time = TimeMode.Utc;
        shell.Theme = ThemeMode.Dark;
        Assert.Equal(TimeMode.Utc, state.Settings.Time);
        Assert.Equal(ThemeMode.Dark, state.Settings.Theme);
        Assert.Equal(TimeMode.Utc, new SettingsStore(_dir).Load().Time);
    }

    [Fact] public void Time_FollowsSettingsChangedElsewhere()
    {
        var (shell, state, _) = Create();
        state.Update(s => s with { Time = TimeMode.Utc });
        Assert.Equal(TimeMode.Utc, shell.Time);
    }
}
