using PictureSorter.App.ViewModels;

namespace PictureSorter.App.Tests.ViewModels;

/// <summary>
/// Tests der gemeinsamen Statusleiste: Lauf-Zustand, Fortschritt, Schweregrad und
/// Abbruch über den Stopp-Befehl.
/// </summary>
public sealed class StatusBarViewModelTests
{
    [Fact]
    public void Begin_SetsBusyIndeterminateAndMessage()
    {
        StatusBarViewModel status = new();

        status.Begin("Läuft…", () => { });

        Assert.True(status.IsBusy);
        Assert.True(status.IsIndeterminate);
        Assert.Equal(0, status.ProgressValue);
        Assert.Equal("Läuft…", status.Message);
        Assert.Equal(StatusSeverity.Informational, status.Severity);
        Assert.True(status.StopCommand.CanExecute(parameter: null));
    }

    [Fact]
    public void ReportProgress_SwitchesToDeterminateAndClampsValue()
    {
        StatusBarViewModel status = new();
        status.Begin("Start", () => { });

        status.ReportProgress("3 von 4", 150.0);

        Assert.False(status.IsIndeterminate);
        Assert.Equal(100, status.ProgressValue);
        Assert.Equal("3 von 4", status.Message);
    }

    [Fact]
    public void Stop_InvokesRegisteredCancelAction()
    {
        StatusBarViewModel status = new();
        bool cancelled = false;
        status.Begin("Start", () => cancelled = true);

        status.StopCommand.Execute(parameter: null);

        Assert.True(cancelled);
    }

    [Fact]
    public void Finish_ClearsBusyAndAppliesSeverity()
    {
        StatusBarViewModel status = new();
        status.Begin("Start", () => { });

        status.Finish("Fehlgeschlagen.", StatusSeverity.Error);

        Assert.False(status.IsBusy);
        Assert.True(status.IsIndeterminate);
        Assert.Equal("Fehlgeschlagen.", status.Message);
        Assert.Equal(StatusSeverity.Error, status.Severity);
        Assert.False(status.StopCommand.CanExecute(parameter: null));
    }

    [Fact]
    public void Stop_AfterFinish_DoesNotInvokeStaleCancelAction()
    {
        StatusBarViewModel status = new();
        int cancelCount = 0;
        status.Begin("Start", () => cancelCount++);
        status.Finish("Fertig.");

        status.StopCommand.Execute(parameter: null);

        Assert.Equal(0, cancelCount);
    }
}
