using HvViewer.App.States;
using Xunit;

namespace HvViewer.App.Tests;

public class ErrorReportTests
{
    private static readonly DateTime Now = new(2026, 11, 11, 12, 30, 0, DateTimeKind.Utc);

    private static Exception Thrown(Exception ex)
    {
        try { throw ex; }
        catch (Exception caught) { return caught; }
    }

    [Fact] public void Summary_IsTheMessage() =>
        Assert.Equal("The log could not be read.", new ErrorReport(Thrown(new InvalidOperationException("The log could not be read.")), "1.0.0", Now).Summary);

    [Fact] public void Details_ContainVersionTimeTypeMessageAndStackTrace()
    {
        var report = new ErrorReport(Thrown(new InvalidOperationException("boom")), "1.2.3", Now);

        Assert.Contains("HvViewer 1.2.3", report.Details);
        Assert.Contains("2026-11-11 12:30:00 UTC", report.Details);
        Assert.Contains("System.InvalidOperationException", report.Details);
        Assert.Contains("boom", report.Details);
        Assert.Contains("   at ", report.Details); // a stack trace line
    }

    [Fact] public void Details_IncludeInnerExceptions()
    {
        var outer = Thrown(new InvalidOperationException("outer", new ArgumentException("inner cause")));
        var report = new ErrorReport(outer, "1.0.0", Now);
        Assert.Contains("inner cause", report.Details);
        Assert.Contains("System.ArgumentException", report.Details);
    }

    [Fact] public void Details_FlattenAggregateExceptions()
    {
        var aggregate = new AggregateException(new IOException("first"), new TimeoutException("second"));
        var report = new ErrorReport(aggregate, "1.0.0", Now);
        Assert.Contains("first", report.Details);
        Assert.Contains("second", report.Details);
    }

    [Fact] public void Details_SayNothingWasWrittenToDisk() =>
        Assert.Contains("nothing was written to disk", new ErrorReport(new Exception("x"), "1.0.0", Now).Details, StringComparison.OrdinalIgnoreCase);

    [Fact] public void Details_DoNotAddThePersonalNameOrMachineName()
    {
        // The report is meant to be pasted into a ticket: it adds only the version, time and Windows version.
        // (A thrown exception's stack trace may contain file paths, so this uses one that was never thrown.)
        var report = new ErrorReport(new Exception("x"), "1.0.0", Now);
        Assert.DoesNotContain(Environment.UserName, report.Details, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Environment.MachineName, report.Details, StringComparison.OrdinalIgnoreCase);
    }

    [Fact] public void ExceptionWithoutAMessage_StillHasASummary() =>
        Assert.False(string.IsNullOrWhiteSpace(new ErrorReport(new Exception(""), "1.0.0", Now).Summary));
}

public class ErrorWindowViewModelTests
{
    [Fact] public void CopyDetails_PutsTheFullReportOnTheClipboard()
    {
        var copied = new List<string>();
        var report = new ErrorReport(new InvalidOperationException("boom"), "1.0.0", DateTime.UtcNow);
        var vm = new ErrorWindowViewModel(report, copied.Add, canContinue: true);

        vm.CopyDetailsCommand.Execute(null);

        Assert.Equal([report.Details], copied);
        Assert.Equal("Copied", vm.CopyStatus);
    }

    [Fact] public void Fatal_HasNoContinueOption()
    {
        var vm = new ErrorWindowViewModel(new ErrorReport(new Exception("x"), "1.0.0", DateTime.UtcNow), _ => { }, canContinue: false);
        Assert.False(vm.CanContinue);
        Assert.Equal("Close HvViewer", vm.CloseText);
    }

    [Fact] public void Recoverable_OffersToKeepGoing()
    {
        var vm = new ErrorWindowViewModel(new ErrorReport(new Exception("x"), "1.0.0", DateTime.UtcNow), _ => { }, canContinue: true);
        Assert.True(vm.CanContinue);
        Assert.Equal("Close", vm.CloseText);
    }

    [Fact] public void Headline_IsPlainEnglish()
    {
        var vm = new ErrorWindowViewModel(new ErrorReport(new Exception("x"), "1.0.0", DateTime.UtcNow), _ => { }, canContinue: true);
        Assert.Equal("Something went wrong", vm.Headline);
        Assert.Contains("only reads", vm.Reassurance);
    }
}
