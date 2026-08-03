using PictureSorter.App.Tests.Fakes;
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
        StatusBarViewModel status = new(new ReswLocalizer());

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
        StatusBarViewModel status = new(new ReswLocalizer());
        status.Begin("Start", () => { });

        status.ReportProgress("3 von 4", 150.0);

        Assert.False(status.IsIndeterminate);
        Assert.Equal(100, status.ProgressValue);
        Assert.Equal("3 von 4", status.Message);
    }

    [Fact]
    public void ReportPipelineProgress_ShowsBothBarsWithTheirOwnValues()
    {
        // Zwei Balken sind nur dann eine Auskunft, wenn beide ihren eigenen Stand tragen:
        // Bei einem Ordner aus der Cloud ist genau das die Frage — bremst die Leitung
        // oder die KI?
        StatusBarViewModel status = new(new ReswLocalizer());
        status.Begin("Start", () => { });

        status.ReportPipelineProgress("Bild 40 von 1100 analysiert…", 55.0, 4.0);

        Assert.True(status.ShowsBothPhases);
        Assert.False(status.IsIndeterminate);
        Assert.Equal(55.0, status.GatherValue);
        Assert.Equal(4.0, status.ProgressValue);
        Assert.Equal("Bild 40 von 1100 analysiert…", status.Message);
    }

    [Fact]
    public void ReportProgress_HidesTheSecondBarAgain()
    {
        // Läufe mit nur einem Abschnitt (Anlernen, Löschen, Zurückholen) behalten den
        // einzelnen Balken. Bliebe der zweite stehen, zeigte er dort dauerhaft den
        // veralteten Stand des vorigen Laufs.
        StatusBarViewModel status = new(new ReswLocalizer());
        status.Begin("Start", () => { });
        status.ReportPipelineProgress("Kette", 55.0, 4.0);

        status.ReportProgress("Einzelner Abschnitt", 30.0);

        Assert.False(status.ShowsBothPhases);
        Assert.Equal(30.0, status.ProgressValue);
    }

    [Fact]
    public void Finish_ResetsBothBars()
    {
        StatusBarViewModel status = new(new ReswLocalizer());
        status.Begin("Start", () => { });
        status.ReportPipelineProgress("Kette", 55.0, 4.0);

        status.Finish("Fertig", StatusSeverity.Success);

        Assert.False(status.IsBusy);
        Assert.False(status.ShowsBothPhases);
        Assert.Equal(0, status.GatherValue);
        Assert.Equal(0, status.ProgressValue);
    }

    [Fact]
    public void Stop_InvokesRegisteredCancelAction()
    {
        StatusBarViewModel status = new(new ReswLocalizer());
        bool cancelled = false;
        status.Begin("Start", () => cancelled = true);

        status.StopCommand.Execute(parameter: null);

        Assert.True(cancelled);
    }

    [Fact]
    public void Finish_ClearsBusyAndAppliesSeverity()
    {
        StatusBarViewModel status = new(new ReswLocalizer());
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
        StatusBarViewModel status = new(new ReswLocalizer());
        int cancelCount = 0;
        status.Begin("Start", () => cancelCount++);
        status.Finish("Fertig.");

        status.StopCommand.Execute(parameter: null);

        Assert.Equal(0, cancelCount);
    }
}
