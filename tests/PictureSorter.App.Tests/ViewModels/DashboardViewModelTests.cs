using PictureSorter.App.Services;
using PictureSorter.App.Tests.Fakes;
using PictureSorter.App.ViewModels;

namespace PictureSorter.App.Tests.ViewModels;

/// <summary>
/// Tests der Startseite. Sie ist für die Zielnutzerin der Einstieg: Die drei Kacheln
/// müssen dorthin führen, wo sie draufsteht, und der KI-Zustand muss ehrlich
/// gemeldet werden – „einsatzbereit" bei fehlendem Modell würde sie in einen
/// Sortierlauf schicken, der scheitern muss.
///
/// Prüfbar ist das erst, seit die Kacheln über den Navigationsdienst gehen. Vorher
/// griff die Seite über den globalen Service-Provider auf den Fensterkontext und von
/// dort per Typumwandlung ins Hauptfenster – ohne laufende Oberfläche nicht testbar.
/// </summary>
public sealed class DashboardViewModelTests
{
    [Fact]
    public void OpenSort_NavigatesToTheSortSection()
    {
        FakeNavigationService navigation = new();
        DashboardViewModel sut = CreateSut(navigation: navigation);

        sut.OpenSortCommand.Execute(parameter: null);

        Assert.Equal(AppSection.Sort, Assert.Single(navigation.Navigations));
    }

    [Fact]
    public void OpenDuplicates_NavigatesToTheDuplicatesSection()
    {
        FakeNavigationService navigation = new();
        DashboardViewModel sut = CreateSut(navigation: navigation);

        sut.OpenDuplicatesCommand.Execute(parameter: null);

        Assert.Equal(AppSection.Duplicates, Assert.Single(navigation.Navigations));
    }

    [Fact]
    public void OpenMemory_NavigatesToTheMemorySection()
    {
        FakeNavigationService navigation = new();
        DashboardViewModel sut = CreateSut(navigation: navigation);

        sut.OpenMemoryCommand.Execute(parameter: null);

        Assert.Equal(AppSection.Memory, Assert.Single(navigation.Navigations));
    }

    [Fact]
    public async Task CheckAi_WhenEverythingIsInstalled_ReportsReady()
    {
        DashboardViewModel sut = CreateSut(FakeModelAvailabilityChecker.Ready());

        await sut.CheckAiCommand.ExecuteAsync(parameter: null);

        Assert.True(sut.IsAiReady);
        Assert.False(sut.IsChecking);
        Assert.Contains("einsatzbereit", sut.AiStatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckAi_WithAMissingModel_NamesItAndDoesNotClaimReadiness()
    {
        DashboardViewModel sut = CreateSut(FakeModelAvailabilityChecker.Missing("llava"));

        await sut.CheckAiCommand.ExecuteAsync(parameter: null);

        Assert.False(sut.IsAiReady);
        Assert.Contains("llava", sut.AiStatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckAi_WhenOllamaIsUnreachable_PointsToTheSetup()
    {
        DashboardViewModel sut = CreateSut(FakeModelAvailabilityChecker.Unreachable());

        await sut.CheckAiCommand.ExecuteAsync(parameter: null);

        Assert.False(sut.IsAiReady);
        Assert.Contains("Einstellungen", sut.AiStatusText, StringComparison.Ordinal);
    }

    private static DashboardViewModel CreateSut(
        FakeModelAvailabilityChecker? checker = null,
        FakeNavigationService? navigation = null) => new(
            checker ?? FakeModelAvailabilityChecker.Ready(),
            navigation ?? new FakeNavigationService(),
            new ReswLocalizer());
}
