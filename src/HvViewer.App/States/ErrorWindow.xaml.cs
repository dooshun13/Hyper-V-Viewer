using System.Windows;
using Wpf.Ui.Controls;

namespace HvViewer.App.States;

public partial class ErrorWindow : FluentWindow
{
    public ErrorWindow(ErrorWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    /// <summary>Copies text to the clipboard; a busy clipboard is ignored.</summary>
    public static void CopyToClipboard(string text)
    {
        try { Clipboard.SetText(text); }
        catch (System.Runtime.InteropServices.COMException) { /* the clipboard was busy */ }
    }
}
