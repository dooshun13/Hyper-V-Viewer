using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace HvViewer.App.Events;

public partial class EventsView : UserControl
{
    public EventsView() => InitializeComponent();

    private EventsViewModel? ViewModel => DataContext as EventsViewModel;

    /// <summary>The search panel may use at most about half the height, so results always get room; it scrolls if it needs more.</summary>
    /// <summary>The first time the screen is shown it runs the default search, so there is something to look at.</summary>
    private void OnLoaded(object sender, RoutedEventArgs e) => ViewModel?.StartIfNeeded();

    /// <summary>The log tree is read from Windows only when the dropdown is first opened.</summary>
    private void OnLogsOpened(object sender, RoutedEventArgs e) => ViewModel?.Logs.EnsureLoaded();

    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) ViewModel?.SearchCommand.Execute(null);
    }

    private void OnQueryKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        ViewModel?.ApplyQueryLineCommand.Execute(null);
        ViewModel?.SearchCommand.Execute(null);
        e.Handled = true;
    }

    private void OnCopyQuery(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(ViewModel?.QueryLine ?? ""); }
        catch (System.Runtime.InteropServices.COMException) { /* the clipboard was busy; nothing to do */ }
    }
}
