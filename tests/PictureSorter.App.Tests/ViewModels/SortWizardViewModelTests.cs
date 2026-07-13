using PictureSorter.App.Tests.Fakes;
using PictureSorter.App.ViewModels;

namespace PictureSorter.App.Tests.ViewModels;

/// <summary>
/// Tests der Schritt-Zustandsmaschine des Sortier-Assistenten. Da das ViewModel die
/// Use-Cases nur über Delegaten kennt, ist es ohne UI vollständig testbar.
/// </summary>
public sealed class SortWizardViewModelTests
{
    [Fact]
    public void NewWizard_StartsOnFirstStep()
    {
        SortWizardViewModel wizard = Create();

        Assert.Equal(0, wizard.CurrentStep);
        Assert.Equal(0, wizard.MaxReachedStep);
        Assert.True(wizard.IsStep1);
        Assert.False(wizard.IsStep2);
        Assert.False(wizard.CanGoBack);
    }

    [Fact]
    public async Task PrimaryAction_OnSuccess_AdvancesAndTracksMaxReached()
    {
        SortWizardViewModel wizard = Create(run: _ => Task.FromResult(true));

        await wizard.PrimaryActionCommand.ExecuteAsync(parameter: null);

        Assert.Equal(1, wizard.CurrentStep);
        Assert.Equal(1, wizard.MaxReachedStep);
        Assert.True(wizard.IsStep2);
        Assert.True(wizard.CanGoBack);
    }

    [Fact]
    public async Task PrimaryAction_OnFailure_StaysOnStep()
    {
        SortWizardViewModel wizard = Create(run: _ => Task.FromResult(false));

        await wizard.PrimaryActionCommand.ExecuteAsync(parameter: null);

        Assert.Equal(0, wizard.CurrentStep);
    }

    [Fact]
    public async Task GoToStep_AllowsReachedStepsOnly()
    {
        SortWizardViewModel wizard = Create(run: _ => Task.FromResult(true));
        await wizard.PrimaryActionCommand.ExecuteAsync(parameter: null); // 0 -> 1
        await wizard.PrimaryActionCommand.ExecuteAsync(parameter: null); // 1 -> 2

        wizard.GoToStep(0);
        Assert.Equal(0, wizard.CurrentStep);

        wizard.GoToStep(4); // noch nicht erreicht -> ignoriert
        Assert.Equal(0, wizard.CurrentStep);

        wizard.GoToStep(2); // bereits erreicht -> erlaubt
        Assert.Equal(2, wizard.CurrentStep);
    }

    [Fact]
    public async Task GoToStep_WhenNotInteractive_IsIgnored()
    {
        bool interactive = true;
        SortWizardViewModel wizard = Create(isInteractive: () => interactive, run: _ => Task.FromResult(true));
        await wizard.PrimaryActionCommand.ExecuteAsync(parameter: null); // 0 -> 1

        interactive = false;
        wizard.GoToStep(0);

        Assert.Equal(1, wizard.CurrentStep);
    }

    [Fact]
    public async Task Restart_ResetsPositionAndInvokesDataReset()
    {
        bool resetCalled = false;
        SortWizardViewModel wizard = Create(run: _ => Task.FromResult(true), reset: () => resetCalled = true);
        await wizard.PrimaryActionCommand.ExecuteAsync(parameter: null);
        await wizard.PrimaryActionCommand.ExecuteAsync(parameter: null);

        wizard.RestartCommand.Execute(parameter: null);

        Assert.True(resetCalled);
        Assert.Equal(0, wizard.CurrentStep);
        Assert.Equal(0, wizard.MaxReachedStep);
    }

    [Fact]
    public void StandardMode_ShowsAllStepsAndStandardActions()
    {
        SortWizardViewModel wizard = Create();

        wizard.IsGuided = false;

        Assert.True(wizard.ShowStandardActions);
        Assert.True(wizard.ShowStep1);
        Assert.True(wizard.ShowStep5);
    }

    [Fact]
    public async Task StepEntered_IsCalledWithTargetStep()
    {
        int enteredStep = -1;
        SortWizardViewModel wizard = Create(run: _ => Task.FromResult(true), onStepEntered: step => enteredStep = step);

        await wizard.PrimaryActionCommand.ExecuteAsync(parameter: null);

        Assert.Equal(1, enteredStep);
    }

    private static SortWizardViewModel Create(
        Func<bool>? isInteractive = null,
        Func<int, bool>? canRun = null,
        Func<int, Task<bool>>? run = null,
        Action? reset = null,
        Action<int>? onStepEntered = null) =>
        new(
            isInteractive ?? (static () => true),
            canRun ?? (static _ => true),
            run ?? (static _ => Task.FromResult(true)),
            reset ?? (static () => { }),
            onStepEntered ?? (static _ => { }),
            new ReswLocalizer());
}
