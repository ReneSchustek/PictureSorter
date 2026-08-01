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

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task EveryStep_HasATitleAndAnActionLabel(int step)
    {
        // Alle sechs Schritte müssen beschriftet und übersetzt sein. Der Sprach-Fake
        // wirft bei einem fehlenden Schlüssel — genau so blieb der letzte Schritt
        // schon einmal mit der Überschrift des vorletzten stehen.
        SortWizardViewModel wizard = Create(run: _ => Task.FromResult(true));
        for (int i = 0; i < step; i++)
        {
            await wizard.PrimaryActionCommand.ExecuteAsync(parameter: null);
        }

        Assert.Equal(step, wizard.CurrentStep);
        Assert.False(string.IsNullOrWhiteSpace(wizard.StepTitle));
        Assert.False(string.IsNullOrWhiteSpace(wizard.PrimaryActionLabel));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task InGuidedMode_ExactlyTheCurrentStepIsVisible(int step)
    {
        // Der geführte Modus zeigt genau eine Karte. Zeigte er zwei, stünde die
        // Nutzerin vor zwei Aktionsknöpfen und wüsste nicht, welcher gilt.
        SortWizardViewModel wizard = Create(run: _ => Task.FromResult(true));
        wizard.IsGuided = true;
        for (int i = 0; i < step; i++)
        {
            await wizard.PrimaryActionCommand.ExecuteAsync(parameter: null);
        }

        bool[] visibility =
        [
            wizard.ShowStep1, wizard.ShowStep2, wizard.ShowStep3,
            wizard.ShowStep4, wizard.ShowStep5, wizard.ShowStep6,
        ];

        _ = Assert.Single(visibility, visible => visible);
        Assert.True(visibility[step]);
        Assert.False(wizard.ShowStandardActions);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task EveryStep_ReportsItselfAsTheActiveOne(int step)
    {
        SortWizardViewModel wizard = Create(run: _ => Task.FromResult(true));
        for (int i = 0; i < step; i++)
        {
            await wizard.PrimaryActionCommand.ExecuteAsync(parameter: null);
        }

        bool[] activeStates =
        [
            wizard.IsStep1, wizard.IsStep2, wizard.IsStep3,
            wizard.IsStep4, wizard.IsStep5, wizard.IsStep6,
        ];

        _ = Assert.Single(activeStates, active => active);
        Assert.True(activeStates[step]);
    }

    [Fact]
    public async Task GoBack_ReturnsToThePreviousStep()
    {
        SortWizardViewModel wizard = Create(run: _ => Task.FromResult(true));
        await wizard.PrimaryActionCommand.ExecuteAsync(parameter: null);

        wizard.GoBackCommand.Execute(parameter: null);

        Assert.Equal(0, wizard.CurrentStep);
        Assert.False(wizard.CanGoBack);
    }

    [Fact]
    public void GoBack_OnTheFirstStep_ChangesNothing()
    {
        SortWizardViewModel wizard = Create();

        wizard.GoBackCommand.Execute(parameter: null);

        Assert.Equal(0, wizard.CurrentStep);
    }

    [Fact]
    public async Task DuringARun_NeitherBackNorTheStepBarIsAvailable()
    {
        // Während gelernt, analysiert oder sortiert wird, darf niemand den Schritt
        // wechseln — sonst liefe der Vorgang auf einem Stand, den es nicht mehr gibt.
        bool isRunning = false;
        SortWizardViewModel wizard = Create(
            isInteractive: () => !isRunning,
            run: _ => Task.FromResult(true));
        await wizard.PrimaryActionCommand.ExecuteAsync(parameter: null);

        isRunning = true;
        wizard.NotifyStateChanged();

        Assert.False(wizard.IsInteractive);
        Assert.False(wizard.CanGoBack);
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
