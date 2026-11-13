using System.Windows;
using System.Windows.Controls;

namespace HvViewer.App.Home;

public partial class HomeView : UserControl
{
    public HomeView() => InitializeComponent();

    /// <summary>The first scan starts when Home is first shown, so the window itself opens quickly.</summary>
    private void OnLoaded(object sender, RoutedEventArgs e) => (DataContext as HomeViewModel)?.StartIfNeeded();
}
