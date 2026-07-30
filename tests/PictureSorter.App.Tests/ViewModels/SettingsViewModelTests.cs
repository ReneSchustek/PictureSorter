using Microsoft.Extensions.Logging;
using PictureSorter.App.Logging;
using PictureSorter.App.Tests.Fakes;
using PictureSorter.App.ViewModels;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.App.Tests.ViewModels;

/// <summary>
/// Tests der Einstellungsseite. Das Protokoll ist die einzige Stelle, an der die
/// Nutzerin nachsehen kann, warum etwas nicht funktioniert hat – Filter und Suche
/// entscheiden darüber, ob sie den einen Fehlereintrag zwischen hunderten
/// Routinemeldungen überhaupt findet.
/// </summary>
public sealed class SettingsViewModelTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "PictureSorterTests", Guid.NewGuid().ToString("N"));

    private readonly FileLoggerProvider _provider;

    /// <summary>Legt ein eigenes Protokollverzeichnis je Test an.</summary>
    public SettingsViewModelTests() => _provider = new FileLoggerProvider(_directory, TestClock.Fixed);

    /// <summary>Räumt Provider und Verzeichnis wieder ab.</summary>
    public void Dispose()
    {
        _provider.Dispose();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void RefreshLog_WithoutEntries_ShowsTheEmptyHint()
    {
        SettingsViewModel viewModel = CreateViewModel();

        viewModel.RefreshLog();

        Assert.Equal(new ReswLocalizer().Get("About_LogEmpty"), viewModel.LogText);
        Assert.Empty(viewModel.LogSummary);
    }

    [Fact]
    public void RefreshLog_ShowsEveryEntry()
    {
        WriteLog();
        SettingsViewModel viewModel = CreateViewModel();

        viewModel.RefreshLog();

        Assert.Contains("Alles in Ordnung", viewModel.LogText, StringComparison.Ordinal);
        Assert.Contains("Etwas ging schief", viewModel.LogText, StringComparison.Ordinal);
        Assert.NotEmpty(viewModel.LogSummary);
    }

    [Fact]
    public void ProblemsOnly_HidesRoutineEntries()
    {
        WriteLog();
        SettingsViewModel viewModel = CreateViewModel();
        viewModel.RefreshLog();

        viewModel.ProblemsOnly = true;

        Assert.DoesNotContain("Alles in Ordnung", viewModel.LogText, StringComparison.Ordinal);
        Assert.Contains("Etwas ging schief", viewModel.LogText, StringComparison.Ordinal);
    }

    [Fact]
    public void SearchText_NarrowsTheViewImmediately()
    {
        // Die Suche greift beim Tippen, ohne dass erst „Aktualisieren" nötig wäre.
        WriteLog();
        SettingsViewModel viewModel = CreateViewModel();
        viewModel.RefreshLog();

        viewModel.SearchText = "Ordnung";

        Assert.Contains("Alles in Ordnung", viewModel.LogText, StringComparison.Ordinal);
        Assert.DoesNotContain("Etwas ging schief", viewModel.LogText, StringComparison.Ordinal);
    }

    [Fact]
    public void SearchText_WithoutMatch_SaysSoInsteadOfShowingNothing()
    {
        // Ein leeres Feld ließe die Nutzerin im Unklaren, ob gefiltert wurde oder das
        // Protokoll leer ist.
        WriteLog();
        SettingsViewModel viewModel = CreateViewModel();
        viewModel.RefreshLog();

        viewModel.SearchText = "kommtnichtvor";

        Assert.Equal(new ReswLocalizer().Get("About_LogNoMatch"), viewModel.LogText);
    }

    [Fact]
    public async Task CheckAiAsync_WhenReady_ReportsReady()
    {
        SettingsViewModel viewModel = CreateViewModel(FakeModelAvailabilityChecker.Ready());

        await viewModel.CheckAiAsync();

        Assert.True(viewModel.IsAiReady);
        Assert.Equal(new ReswLocalizer().Get("About_KiReady"), viewModel.AiStatusText);
    }

    [Fact]
    public async Task CheckAiAsync_WhenUnreachable_ReportsNotSetUp()
    {
        SettingsViewModel viewModel = CreateViewModel(FakeModelAvailabilityChecker.Unreachable());

        await viewModel.CheckAiAsync();

        Assert.False(viewModel.IsAiReady);
        Assert.Equal(new ReswLocalizer().Get("About_KiNotSetUp"), viewModel.AiStatusText);
    }

    [Fact]
    public async Task CheckAiAsync_WithMissingModel_NamesIt()
    {
        SettingsViewModel viewModel = CreateViewModel(FakeModelAvailabilityChecker.Missing("llava"));

        await viewModel.CheckAiAsync();

        Assert.False(viewModel.IsAiReady);
        Assert.Contains("llava", viewModel.AiStatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_WhenTheCheckIsNotPossible_SaysSoInsteadOfClaimingItIsUpToDate()
    {
        // „Aktuell" und „nicht nachsehbar" sind zweierlei. Wer offline ist, soll nicht
        // in dem Glauben gelassen werden, er habe die neueste Fassung.
        SettingsViewModel viewModel = CreateViewModel(updates: new FakeUpdateCoordinator(info: null));

        await viewModel.CheckForUpdatesAsync();

        Assert.Equal(StatusSeverity.Warning, viewModel.UpdateSeverity);
        Assert.False(viewModel.CanInstallUpdate);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_WithANewerVersion_OffersTheInstallation()
    {
        UpdateInfo info = new()
        {
            CurrentVersion = "1.3.1",
            LatestVersion = "1.4.0",
            IsUpdateAvailable = true,
        };
        SettingsViewModel viewModel = CreateViewModel(updates: new FakeUpdateCoordinator(info));

        await viewModel.CheckForUpdatesAsync();

        Assert.True(viewModel.CanInstallUpdate);
        Assert.Contains("1.4.0", viewModel.UpdateStatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InstallUpdateAsync_WhenTheHelperStarts_EndsTheApplication()
    {
        // Solange die Anwendung läuft, sind ihre Dateien gesperrt – ohne das Beenden
        // käme das Update nie an.
        FakeApplicationShutdown shutdown = new();
        SettingsViewModel viewModel = CreateViewModel(
            updates: new FakeUpdateCoordinator(launchSucceeds: true),
            shutdown: shutdown);

        await viewModel.InstallUpdateAsync();

        Assert.True(shutdown.WasRequested);
    }

    [Fact]
    public async Task InstallUpdateAsync_WhenTheHelperDoesNotStart_KeepsTheApplicationRunning()
    {
        FakeApplicationShutdown shutdown = new();
        SettingsViewModel viewModel = CreateViewModel(
            updates: new FakeUpdateCoordinator(launchSucceeds: false),
            shutdown: shutdown);

        await viewModel.InstallUpdateAsync();

        Assert.False(shutdown.WasRequested);
        Assert.Equal(StatusSeverity.Error, viewModel.UpdateSeverity);
    }

    private SettingsViewModel CreateViewModel(
        IModelAvailabilityChecker? checker = null,
        FakeUpdateCoordinator? updates = null,
        FakeApplicationShutdown? shutdown = null) =>
        new(
            _provider,
            checker ?? FakeModelAvailabilityChecker.Ready(),
            updates ?? new FakeUpdateCoordinator(),
            shutdown ?? new FakeApplicationShutdown(),
            new ReswLocalizer());

    private void WriteLog()
    {
        ILogger logger = _provider.CreateLogger("Test");
        logger.LogInformation("Alles in Ordnung.");
        logger.LogError("Etwas ging schief.");
    }
}
