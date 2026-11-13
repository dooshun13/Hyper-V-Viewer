using HvViewer.App.Saved;
using HvViewer.App.SettingsScreen;
using HvViewer.App.Services;
using HvViewer.Core.Model;
using HvViewer.Core.Search;
using HvViewer.Core.Settings;
using HvViewer.Rules;
using Xunit;

namespace HvViewer.App.Tests;

public abstract class ScreenTestBase : IDisposable
{
    protected static readonly DateTime Now = new(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);
    protected readonly string Dir = Path.Combine(Path.GetTempPath(), "evt-screens-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(Dir)) Directory.Delete(Dir, true);
        else if (File.Exists(Dir)) File.Delete(Dir);
    }

    protected AppState State(bool admin = false) => new(new SettingsStore(Dir), admin, () => Now);

    protected static RuleEvent Rule(string provider, int id, string log) =>
        new(provider, id, log, RuleSeverity.Warning, "t", "m", "c", "n", "https://learn.microsoft.com/x");

    protected static RuleSet Rules() => new(
    [
        new Troubleshooter(1, "disk-storage", "Disk & storage", "Disk errors and retries.",
            [Rule("disk", 153, "System"), Rule("disk", 7, "System"), Rule("Microsoft-Windows-Ntfs", 55, "System")]),
        new Troubleshooter(1, "logons-accounts", "Logons & accounts", "Failed sign-ins.",
            [Rule("Microsoft-Windows-Security-Auditing", 4625, "Security")]),
        new Troubleshooter(1, "crashes-hangs", "Crashes & hangs", "Programs that crash.",
            [Rule("Application Error", 1000, "Application")]),
    ], []);
}

public sealed class SavedViewsViewModelTests : ScreenTestBase
{
    private AppState StateWith(params SavedView[] views)
    {
        var state = State();
        state.Update(s => s with { SavedViews = views });
        return state;
    }

    [Fact] public void Items_ListTheSavedViews()
    {
        var vm = new SavedViewsViewModel(StateWith(new SavedView("Disk", "disk last:24h"), new SavedView("Logons", "id:4625")), new FakeNavigator());
        Assert.Equal(["Disk", "Logons"], vm.Items.Select(i => i.Name));
        Assert.True(vm.HasItems);
    }

    [Fact] public void NoViews_IsEmpty() => Assert.False(new SavedViewsViewModel(State(), new FakeNavigator()).HasItems);

    [Fact] public void Open_ParsesTheQueryLineAndShowsEvents()
    {
        var nav = new FakeNavigator();
        var vm = new SavedViewsViewModel(StateWith(new SavedView("Disk", "disk id:153 last:7d")), nav);

        vm.OpenCommand.Execute(vm.Items[0]);

        var q = Assert.Single(nav.Queries);
        Assert.Equal("disk", q.Text);
        Assert.Equal([153], q.IncludeIds);
        Assert.Equal(TimeSpan.FromDays(7), q.LastSpan);
        Assert.Equal("", vm.Message);
    }

    [Fact] public void Open_BrokenQueryLine_ShowsAMessageAndStaysPut()
    {
        var nav = new FakeNavigator();
        var vm = new SavedViewsViewModel(StateWith(new SavedView("Bad", "foo:1")), nav);

        vm.OpenCommand.Execute(vm.Items[0]);

        Assert.Empty(nav.Queries);
        Assert.Contains("Unknown filter 'foo'", vm.Message);
    }

    [Fact] public void DeleteView_SavesSettings()
    {
        var state = StateWith(new SavedView("A", "a"), new SavedView("B", "b"));
        var vm = new SavedViewsViewModel(state, new FakeNavigator());

        vm.DeleteCommand.Execute(vm.Items[0]);

        Assert.Equal(["B"], state.Settings.SavedViews.Select(v => v.Name));
        Assert.Equal(["B"], new SettingsStore(Dir).Load().SavedViews.Select(v => v.Name));
        Assert.Equal(["B"], vm.Items.Select(i => i.Name));
    }

    [Fact] public void Rename_ChangesOnlyTheName()
    {
        var state = StateWith(new SavedView("Old", "disk last:24h"));
        var vm = new SavedViewsViewModel(state, new FakeNavigator());
        var item = vm.Items[0];

        vm.StartRenameCommand.Execute(item);
        Assert.True(item.IsEditing);
        item.EditName = "  New name ";
        vm.CommitRenameCommand.Execute(item);

        Assert.Equal([new SavedView("New name", "disk last:24h")], state.Settings.SavedViews);
        Assert.False(vm.Items[0].IsEditing);
    }

    [Fact] public void Rename_ToAnExistingName_IsRefused()
    {
        var state = StateWith(new SavedView("A", "a"), new SavedView("B", "b"));
        var vm = new SavedViewsViewModel(state, new FakeNavigator());
        var item = vm.Items[1];

        vm.StartRenameCommand.Execute(item);
        item.EditName = "a";
        vm.CommitRenameCommand.Execute(item);

        Assert.Equal(["A", "B"], state.Settings.SavedViews.Select(v => v.Name));
        Assert.Contains("already", vm.Message);
    }

    [Fact] public void Rename_Blank_IsRefused()
    {
        var state = StateWith(new SavedView("A", "a"));
        var vm = new SavedViewsViewModel(state, new FakeNavigator());
        vm.StartRenameCommand.Execute(vm.Items[0]);
        vm.Items[0].EditName = "   ";
        vm.CommitRenameCommand.Execute(vm.Items[0]);
        Assert.Equal("A", state.Settings.SavedViews[0].Name);
        Assert.NotEqual("", vm.Message);
    }

    [Fact] public void CancelRename_LeavesTheNameAlone()
    {
        var state = StateWith(new SavedView("A", "a"));
        var vm = new SavedViewsViewModel(state, new FakeNavigator());
        vm.StartRenameCommand.Execute(vm.Items[0]);
        vm.Items[0].EditName = "changed";
        vm.CancelRenameCommand.Execute(vm.Items[0]);
        Assert.Equal("A", vm.Items[0].Name);
        Assert.False(vm.Items[0].IsEditing);
    }

    [Fact] public void ViewsSavedElsewhere_AppearWithoutReopening()
    {
        var state = State();
        var vm = new SavedViewsViewModel(state, new FakeNavigator());
        state.Update(s => s with { SavedViews = [new SavedView("New", "x")] });
        Assert.Equal(["New"], vm.Items.Select(i => i.Name));
    }

    [Fact] public void SaveFailure_IsShown()
    {
        var state = StateWith(new SavedView("A", "a"));
        File.Delete(Path.Combine(Dir, SettingsStore.FileName));
        Directory.Delete(Dir, true);
        File.WriteAllText(Dir, "a file where the folder should be");

        var vm = new SavedViewsViewModel(state, new FakeNavigator());
        vm.DeleteCommand.Execute(vm.Items[0]);

        Assert.Contains("could not be saved", vm.Message);
    }
}

public sealed class SettingsViewModelTests : ScreenTestBase
{
    private SettingsViewModel Create(AppState? state = null, RuleSet? rules = null, TimeSpan? startup = null) =>
        new(state ?? State(), rules ?? Rules(), () => startup, "C:\\Users\\me\\AppData\\Roaming\\HvViewer\\HvViewer.settings.json", "1.0.0");

    [Fact] public void Theme_Change_IsSaved()
    {
        var state = State();
        var vm = Create(state);

        vm.Theme = ThemeMode.Light;

        Assert.Equal(ThemeMode.Light, state.Settings.Theme);
        Assert.Equal(ThemeMode.Light, new SettingsStore(Dir).Load().Theme);
        Assert.True(vm.IsThemeLight);
    }

    [Fact] public void ThemeRadios_SetTheTheme()
    {
        var vm = Create();
        vm.IsThemeDark = true;
        Assert.Equal(ThemeMode.Dark, vm.Theme);
        vm.IsThemeSystem = true;
        Assert.Equal(ThemeMode.System, vm.Theme);
    }

    [Fact] public void Time_Change_IsSaved()
    {
        var state = State();
        var vm = Create(state);
        vm.IsTimeUtc = true;
        Assert.Equal(TimeMode.Utc, state.Settings.Time);
        Assert.False(vm.IsTimeLocal);
    }

    [Theory]
    [InlineData("1h", "1h")]
    [InlineData("7d", "7d")]
    [InlineData("24h", "24h")]
    [InlineData("30d", "24h")]
    [InlineData("", "24h")]
    public void DefaultRange_OnlyKnownValuesAreKept(string chosen, string expected)
    {
        var state = State();
        Create(state).DefaultRange = chosen;
        Assert.Equal(expected, state.Settings.DefaultRange);
    }

    [Fact] public void Changes_MadeElsewhere_AreReflected()
    {
        var state = State();
        var vm = Create(state);
        state.Update(s => s with { Theme = ThemeMode.Dark });
        Assert.True(vm.IsThemeDark);
    }

    [Fact] public void About_ShowsVersionStartupTimeAndTheReadOnlyStatement()
    {
        var vm = Create(startup: TimeSpan.FromMilliseconds(842));
        Assert.Equal("1.0.0", vm.Version);
        Assert.Equal("Opened in 0.8 s", vm.StartupText);
        Assert.Equal("Hyper-V Viewer only reads. It never changes logs, the registry, services or settings.", vm.ReadOnlyStatement);
        Assert.Contains("HvViewer.settings.json", vm.SettingsPath);
    }

    [Fact] public void About_StartupNotMeasuredYet() => Assert.Equal("—", Create().StartupText);

    [Fact] public void RuleErrors_AreListedWithTheirSource()
    {
        var rules = new RuleSet([], [new RuleLoadError("bad.json", "The id must use only a-z, 0-9 and '-'.")]);
        var vm = Create(rules: rules);
        Assert.Equal(["bad.json: The id must use only a-z, 0-9 and '-'."], vm.RuleErrors);
        Assert.Equal("1 rule file was skipped", vm.RulesText);
    }

    [Fact] public void RulesText_WhenAllLoaded() => Assert.Equal("3 troubleshooters loaded", Create().RulesText);

    [Fact] public void SaveFailure_IsShown()
    {
        File.WriteAllText(Dir, "blocking file");
        var vm = Create(State());
        vm.Theme = ThemeMode.Dark;
        Assert.Contains("could not be saved", vm.SaveMessage);
    }
}
