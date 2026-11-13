using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using HvViewer.App.Services;
using Wpf.Ui.Controls;

namespace HvViewer.App.Shell;

public partial class MainWindow : FluentWindow
{
    private readonly ShellViewModel _viewModel;
    private readonly FootprintMonitor _footprint;
    private readonly DispatcherTimer _footprintTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly Process _process = Process.GetCurrentProcess();

    public MainWindow(ShellViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        _footprint = new FootprintMonitor(
            () => { _process.Refresh(); return _process.TotalProcessorTime; },
            () => { _process.Refresh(); return _process.WorkingSet64; },
            () => DateTime.UtcNow,
            Environment.ProcessorCount);
        _footprintTimer.Tick += (_, _) => _viewModel.Footprint = _footprint.Sample();

        InputBindings.Add(new KeyBinding(new RelayCommand(() => SearchBox.Focus()), Key.K, ModifierKeys.Control));

        // The footprint timer is UI only and runs just while the window is visible.
        Loaded += (_, _) => UpdateTimer();
        StateChanged += (_, _) => UpdateTimer();
        IsVisibleChanged += (_, _) => UpdateTimer();
    }

    private void UpdateTimer()
    {
        if (IsVisible && WindowState != WindowState.Minimized)
        {
            _viewModel.Footprint = _footprint.Sample();
            _footprintTimer.Start();
        }
        else
        {
            _footprintTimer.Stop();
        }
    }
}
