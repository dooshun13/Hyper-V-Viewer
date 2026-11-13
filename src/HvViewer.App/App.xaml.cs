using System.Diagnostics;
using System.Security.Principal;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using HvViewer.App.Detail;
using HvViewer.App.Events;
using HvViewer.App.Home;
using HvViewer.App.Saved;
using HvViewer.App.Services;
using HvViewer.App.SettingsScreen;
using HvViewer.App.Shell;
using HvViewer.App.States;
using HvViewer.App.Theme;
using HvViewer.Core.Settings;

namespace HvViewer.App;

public partial class App : Application
{
    private readonly Stopwatch _startup = Stopwatch.StartNew();

    /// <summary>Time from process start-up code to the first rendered frame; shown in Settings > About.</summary>
    public static TimeSpan? StartupTime { get; private set; }

    private bool _showingError;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Unexpected errors get a window with copyable details; nothing is written to disk.
        DispatcherUnhandledException += (_, args) =>
        {
            args.Handled = true;
            ShowError(args.Exception, canContinue: true);
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            args.SetObserved();
            Dispatcher.BeginInvoke(() => ShowError(args.Exception, canContinue: true));
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex) ShowError(ex, canContinue: false);
        };

        base.OnStartup(e);

        var isAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
        var state = new AppState(new SettingsStore(SettingsStore.DefaultDirectory), isAdmin, () => DateTime.UtcNow);
        ThemeService.Apply(state.Settings.Theme);
        state.Changed += () => ThemeService.Apply(state.Settings.Theme);

        var services = AppServices.Create(state, isAdmin);
        var navigator = new LateNavigator();
        var shell = new ShellViewModel(state, Environment.MachineName, isAdmin, key => CreatePage(key, services, navigator), RestartAsAdministrator);
        navigator.Target = shell;

        var window = new MainWindow(shell);
        window.ContentRendered += (_, _) => StartupTime = _startup.Elapsed;
        window.Show();
    }

    private void ShowError(Exception exception, bool canContinue)
    {
        if (_showingError) return; // one window at a time; a second error while it is open adds nothing
        _showingError = true;
        try
        {
            var version = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
            var viewModel = new ErrorWindowViewModel(new ErrorReport(exception, version, DateTime.UtcNow), ErrorWindow.CopyToClipboard, canContinue);
            var window = new ErrorWindow(viewModel);
            if (MainWindow is { IsLoaded: true } main) window.Owner = main;
            window.ShowDialog();
        }
        catch (Exception)
        {
            // If even the error window fails there is nothing safe left to do; the original error is not hidden.
        }
        finally
        {
            _showingError = false;
        }
    }

    /// <summary>Relaunches HvViewer with administrator rights (Windows asks the owner to confirm), then closes this copy.</summary>
    private static void RestartAsAdministrator()
    {
        try
        {
            var path = Environment.ProcessPath;
            if (path is null) return;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true, Verb = "runas" });
            Current.Shutdown();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // The owner declined the Windows prompt: stay as a standard user.
        }
    }

    private static ObservableObject CreatePage(string key, AppServices services, INavigator navigator) => key switch
    {
        "home" => new HomeViewModel(services.Dashboard, () => services.Health.RunAll(), services.SystemInfo, services.Hardware,
            services.Rules, navigator, services.State, store: services.Store),
        "events" => new EventsViewModel(services.Search, services.Source, services.Rules, services.State, services.SystemInfo.BootTimeUtc, new ExportService(),
            e =>
            {
                var detail = new DetailViewModel(e, services.Source, services.Cache, services.Rules, services.Search, services.State);
                _ = detail.LoadAsync();
                return detail;
            }),
        "saved" => new SavedViewsViewModel(services.State, navigator),
        _ => new SettingsViewModel(services.State, services.Rules, () => StartupTime,
            Path.Combine(SettingsStore.DefaultDirectory, SettingsStore.FileName),
            typeof(App).Assembly.GetName().Version?.ToString(3) ?? "1.0.0"),
    };
}
