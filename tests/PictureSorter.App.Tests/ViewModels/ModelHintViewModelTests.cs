using PictureSorter.App.Tests.Fakes;
using PictureSorter.App.ViewModels;

namespace PictureSorter.App.Tests.ViewModels;

/// <summary>
/// Tests des KI-Hinweises auf der Sortierseite. Er ist die einzige Stelle, an der die
/// Zielnutzerin erfährt, warum das Sortieren nicht funktioniert – erscheint er zu
/// Unrecht, verunsichert er; fehlt er, sucht sie den Fehler bei sich.
/// </summary>
public sealed class ModelHintViewModelTests
{
    [Fact]
    public async Task Check_WhenTheAiIsReady_ShowsNoHint()
    {
        ModelHintViewModel sut = new(FakeModelAvailabilityChecker.Ready(), new ReswLocalizer());

        await sut.CheckCommand.ExecuteAsync(parameter: null);

        Assert.False(sut.IsHintVisible);
    }

    [Fact]
    public async Task Check_WithAMissingModel_PointsToTheSetupButton()
    {
        // Bewusst ohne Kommandozeilen-Befehl: Die Zielnutzerin soll klicken, nicht tippen.
        ModelHintViewModel sut = new(FakeModelAvailabilityChecker.Missing("llava"), new ReswLocalizer());

        await sut.CheckCommand.ExecuteAsync(parameter: null);

        Assert.True(sut.IsHintVisible);
        Assert.Contains("Jetzt einrichten", sut.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("ollama pull", sut.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Check_WhenOllamaIsUnreachable_ShowsTheHint()
    {
        ModelHintViewModel sut = new(FakeModelAvailabilityChecker.Unreachable(), new ReswLocalizer());

        await sut.CheckCommand.ExecuteAsync(parameter: null);

        Assert.True(sut.IsHintVisible);
        Assert.NotEmpty(sut.Message);
    }
}
