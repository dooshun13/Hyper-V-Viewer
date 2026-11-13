using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace HvViewer.App.States;

/// <summary>What the error window shows. After a fatal error the only choice is to close.</summary>
public sealed partial class ErrorWindowViewModel : ObservableObject
{
    private readonly ErrorReport _report;
    private readonly Action<string> _copy;

    public ErrorWindowViewModel(ErrorReport report, Action<string> copy, bool canContinue)
    {
        _report = report;
        _copy = copy;
        CanContinue = canContinue;
        CopyDetailsCommand = new RelayCommand(CopyDetails);
    }

    public string Headline => "Something went wrong";

    public string Reassurance => "Hyper-V Viewer only reads your logs, and nothing was written to disk. You can copy the details below and send them to whoever looks after Hyper-V Viewer.";

    public string Summary => _report.Summary;

    public string Details => _report.Details;

    public bool CanContinue { get; }

    public string CloseText => CanContinue ? "Close" : "Close HvViewer";

    [ObservableProperty] private string _copyStatus = "";

    public IRelayCommand CopyDetailsCommand { get; }

    private void CopyDetails()
    {
        _copy(_report.Details);
        CopyStatus = "Copied";
    }
}
