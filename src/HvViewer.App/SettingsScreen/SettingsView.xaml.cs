using System.Windows;
using System.Windows.Controls;

namespace HvViewer.App.SettingsScreen;

public partial class SettingsView : UserControl
{
    public SettingsView() => InitializeComponent();

    private void OnLoaded(object sender, RoutedEventArgs e) => (DataContext as SettingsViewModel)?.RefreshAbout();
}
