using System.Xml.Linq;
using HvViewer.App.Services;
using Xunit;

namespace HvViewer.App.Tests;

public class UrlLauncherTests
{
    [Theory]
    [InlineData("https://learn.microsoft.com/search/?terms=disk%20event%20153", true)]
    [InlineData("https://learn.microsoft.com/", true)]
    [InlineData("https://evil.example", false)]
    [InlineData("http://learn.microsoft.com/x", false)]
    [InlineData("https://learn.microsoft.com.evil.example/x", false)]
    [InlineData("https://evil.example/https://learn.microsoft.com/", false)]
    [InlineData("file:///C:/Windows/System32/cmd.exe", false)]
    [InlineData("cmd.exe", false)]
    [InlineData("", false)]
    public void IsAllowed_OnlyLearnMicrosoftHttps(string url, bool expected) =>
        Assert.Equal(expected, UrlLauncher.IsAllowed(url));

    [Fact] public void TryOpen_RejectsNonLearnUrls_WithoutLaunching() =>
        Assert.False(UrlLauncher.TryOpen("https://evil.example"));
}

public class FootprintMonitorTests
{
    [Fact] public void Sample_FormatsCpuAndMemory()
    {
        var now = new DateTime(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);
        var cpu = TimeSpan.Zero;
        var monitor = new FootprintMonitor(() => cpu, () => 84L * 1024 * 1024, () => now, cores: 4);

        Assert.Equal("CPU 0% · 84 MB", monitor.Sample());

        now = now.AddSeconds(2);
        cpu = TimeSpan.FromSeconds(0.4); // 0.4 s of CPU over 2 s on 4 cores = 5%
        Assert.Equal("CPU 5% · 84 MB", monitor.Sample());
    }

    [Fact] public void Sample_NeverExceeds100()
    {
        var now = DateTime.UtcNow;
        var cpu = TimeSpan.Zero;
        var monitor = new FootprintMonitor(() => cpu, () => 1, () => now, cores: 1);
        monitor.Sample();
        now = now.AddSeconds(1);
        cpu = TimeSpan.FromSeconds(5);
        Assert.StartsWith("CPU 100%", monitor.Sample());
    }

    [Fact] public void Sample_NoTimeElapsed_ReusesLastValue()
    {
        var now = DateTime.UtcNow;
        var monitor = new FootprintMonitor(() => TimeSpan.FromSeconds(1), () => 1024 * 1024, () => now, cores: 1);
        Assert.Equal(monitor.Sample(), monitor.Sample());
    }
}

public class ThemeTokensTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static IReadOnlySet<string> Keys(string file)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "HvViewer.App", "Theme", file);
        return XDocument.Load(path).Root!.Elements().Select(e => (string?)e.Attribute(X + "Key")).Where(k => k is not null).Select(k => k!).ToHashSet();
    }

    [Fact] public void DarkAndLight_DefineTheSameKeys()
    {
        var dark = Keys("Tokens.Dark.xaml");
        var light = Keys("Tokens.Light.xaml");
        Assert.NotEmpty(dark);
        Assert.Empty(dark.Except(light));
        Assert.Empty(light.Except(dark));
    }

    [Theory]
    [InlineData("WindowBg"), InlineData("CardBg"), InlineData("TextPrimary"), InlineData("Accent"),
     InlineData("Critical"), InlineData("Error"), InlineData("Warning"), InlineData("Info"), InlineData("Ok")]
    public void RequiredTokens_Exist(string key)
    {
        Assert.Contains(key, Keys("Tokens.Dark.xaml"));
        Assert.Contains(key, Keys("Tokens.Light.xaml"));
    }
}
