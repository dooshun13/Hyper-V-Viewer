using HvViewer.Core.Settings;
using Xunit;

namespace HvViewer.Tests.Core;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "evt-settings-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
        else if (File.Exists(_root)) File.Delete(_root);
    }

    private string Dir => Path.Combine(_root, "HvViewer");

    [Fact] public void Load_NoFile_ReturnsDefaults_AndCreatesNothing()
    {
        var settings = new SettingsStore(Dir).Load();

        Assert.Equal(ThemeMode.System, settings.Theme);
        Assert.Equal(TimeMode.Local, settings.Time);
        Assert.Equal("24h", settings.DefaultRange);
        Assert.Empty(settings.SavedViews);
        Assert.False(Directory.Exists(_root));
    }

    [Fact] public void Save_ThenLoad_RoundTrips()
    {
        var store = new SettingsStore(Dir);
        var saved = new AppSettings
        {
            Theme = ThemeMode.Dark,
            Time = TimeMode.Utc,
            DefaultRange = "7d",
            SavedViews = [new SavedView("Disk problems", "disk last:24h"), new SavedView("Logons", "id:4625")],
        };

        Assert.True(store.Save(saved));
        var loaded = new SettingsStore(Dir).Load();

        Assert.Equal(ThemeMode.Dark, loaded.Theme);
        Assert.Equal(TimeMode.Utc, loaded.Time);
        Assert.Equal("7d", loaded.DefaultRange);
        Assert.Equal(saved.SavedViews, loaded.SavedViews);
    }

    [Fact] public void Save_WritesOnlyTheSettingsFile()
    {
        new SettingsStore(Dir).Save(new AppSettings());
        Assert.Equal(["HvViewer.settings.json"], Directory.GetFiles(Dir).Select(Path.GetFileName));
    }

    [Fact] public void Save_Twice_Overwrites()
    {
        var store = new SettingsStore(Dir);
        store.Save(new AppSettings { DefaultRange = "1h" });
        store.Save(new AppSettings { DefaultRange = "7d" });
        Assert.Equal("7d", store.Load().DefaultRange);
    }

    [Fact] public void Load_CorruptFile_ReturnsDefaults_AndLeavesFileAlone()
    {
        Directory.CreateDirectory(Dir);
        var path = Path.Combine(Dir, "HvViewer.settings.json");
        File.WriteAllText(path, "{ this is not json");

        Assert.Equal("24h", new SettingsStore(Dir).Load().DefaultRange);
        Assert.Equal("{ this is not json", File.ReadAllText(path));
    }

    [Fact] public void Load_UnknownFields_AreIgnored()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(Path.Combine(Dir, "HvViewer.settings.json"), """{ "theme": "dark", "futureThing": 5 }""");
        Assert.Equal(ThemeMode.Dark, new SettingsStore(Dir).Load().Theme);
    }

    [Fact] public void Save_WhenFolderCannotBeCreated_ReturnsFalse()
    {
        File.WriteAllText(_root, "a file where the folder should go");
        Assert.False(new SettingsStore(Path.Combine(_root, "HvViewer")).Save(new AppSettings()));
    }

    [Fact] public void DefaultDirectory_IsHvViewerUnderAppData() =>
        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HvViewer"), SettingsStore.DefaultDirectory);
}
