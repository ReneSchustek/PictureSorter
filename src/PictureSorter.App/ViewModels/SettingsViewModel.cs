using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PictureSorter.App.Logging;
using PictureSorter.App.Services;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.App.ViewModels;

/// <summary>
/// ViewModel der Einstellungsseite. Trägt die Logik hinter dem Protokoll-Bereich
/// (lesen, filtern, durchsuchen) und der Zustandsanzeige der lokalen KI.
///
/// Das Protokoll ist die einzige Stelle, an der die Nutzerin nachsehen kann, warum
/// etwas nicht funktioniert hat. Ohne Filter und Suche geht der eine Fehlereintrag
/// zwischen hunderten Routinemeldungen unter.
/// </summary>
internal sealed partial class SettingsViewModel : ObservableObject
{
    private const int ReadLineCount = 500;

    private readonly FileLoggerProvider _fileLogger;
    private readonly IModelAvailabilityChecker _modelChecker;
    private readonly IUpdateCoordinator _updateService;
    private readonly IApplicationShutdown _shutdown;
    private readonly ILocalizer _localizer;

    private IReadOnlyList<string> _allLines = [];

    /// <summary>Der angezeigte Protokolltext.</summary>
    [ObservableProperty]
    public partial string LogText { get; set; }

    /// <summary>Suchbegriff für das Protokoll.</summary>
    [ObservableProperty]
    public partial string SearchText { get; set; }

    /// <summary>Ob nur Warnungen und Fehler gezeigt werden.</summary>
    [ObservableProperty]
    public partial bool ProblemsOnly { get; set; }

    /// <summary>Zusammenfassung, wie viele Einträge die Auswahl übrig lässt.</summary>
    [ObservableProperty]
    public partial string LogSummary { get; set; }

    /// <summary>Kurztext zum Zustand der lokalen KI.</summary>
    [ObservableProperty]
    public partial string AiStatusText { get; set; }

    /// <summary><see langword="true"/>, wenn die KI einsatzbereit ist.</summary>
    [ObservableProperty]
    public partial bool IsAiReady { get; set; }

    /// <summary>Kurztext zum Stand der Update-Prüfung.</summary>
    [ObservableProperty]
    public partial string UpdateStatusText { get; set; }

    /// <summary>Gewicht der Update-Meldung.</summary>
    [ObservableProperty]
    public partial StatusSeverity UpdateSeverity { get; set; }

    /// <summary><see langword="true"/>, wenn eine neue Fassung bereitsteht.</summary>
    [ObservableProperty]
    public partial bool CanInstallUpdate { get; set; }

    /// <summary>
    /// Initialisiert das ViewModel.
    /// </summary>
    /// <param name="fileLogger">Quelle der Protokollzeilen.</param>
    /// <param name="modelChecker">Prüft die Verfügbarkeit der KI-Modelle.</param>
    /// <param name="updateService">Prüfung und Einspielen einer neuen Fassung.</param>
    /// <param name="shutdown">Beendet die Anwendung, damit das Update greifen kann.</param>
    /// <param name="localizer">Die Textquelle.</param>
    public SettingsViewModel(
        FileLoggerProvider fileLogger,
        IModelAvailabilityChecker modelChecker,
        IUpdateCoordinator updateService,
        IApplicationShutdown shutdown,
        ILocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(fileLogger);
        ArgumentNullException.ThrowIfNull(modelChecker);
        ArgumentNullException.ThrowIfNull(updateService);
        ArgumentNullException.ThrowIfNull(shutdown);
        ArgumentNullException.ThrowIfNull(localizer);

        _fileLogger = fileLogger;
        _modelChecker = modelChecker;
        _updateService = updateService;
        _shutdown = shutdown;
        _localizer = localizer;

        LogText = localizer.Get("About_LogEmpty");
        LogSummary = string.Empty;
        SearchText = string.Empty;
        AiStatusText = localizer.Get("About_KiChecking");
        UpdateStatusText = string.Empty;
        UpdateSeverity = StatusSeverity.Informational;
    }

    /// <summary>Verzeichnis der Protokolldateien (für „Ordner öffnen").</summary>
    public string LogDirectory => _fileLogger.LogDirectory;

    /// <summary>
    /// Liest das Protokoll neu ein und wendet die aktuelle Auswahl an.
    /// </summary>
    [RelayCommand]
    public void RefreshLog()
    {
        _allLines = _fileLogger.ReadRecent(ReadLineCount);
        ApplyFilter();
    }

    /// <summary>
    /// Prüft, ob die lokale KI erreichbar und vollständig eingerichtet ist.
    /// </summary>
    [RelayCommand]
    public async Task CheckAiAsync()
    {
        AiStatusText = _localizer.Get("About_KiChecking");

        ModelAvailability availability = await _modelChecker
            .CheckAsync(CancellationToken.None)
            .ConfigureAwait(true);

        IsAiReady = availability.IsReady;
        AiStatusText = BuildAiStatusText(availability);
    }

    /// <summary>
    /// Prüft, ob eine neuere Fassung veröffentlicht wurde.
    /// </summary>
    [RelayCommand]
    public async Task CheckForUpdatesAsync()
    {
        UpdateSeverity = StatusSeverity.Informational;
        UpdateStatusText = _localizer.Get("About_UpdateSearching");
        CanInstallUpdate = false;

        UpdateInfo? info = await _updateService.CheckAsync(CancellationToken.None).ConfigureAwait(true);

        // Kein Ergebnis heißt nicht „aktuell", sondern „nicht nachsehbar" – etwa ohne
        // Netz. Das auseinanderzuhalten erspart der Nutzerin die falsche Gewissheit,
        // auf dem neuesten Stand zu sein.
        if (info is null)
        {
            UpdateSeverity = StatusSeverity.Warning;
            UpdateStatusText = _localizer.Get("About_UpdateCheckFailed");
            return;
        }

        UpdateSeverity = StatusSeverity.Success;
        if (info.IsUpdateAvailable)
        {
            UpdateStatusText = _localizer.Format("About_UpdateAvailable", info.LatestVersion, info.CurrentVersion);
            CanInstallUpdate = true;
            return;
        }

        UpdateStatusText = _localizer.Format("About_UpToDate", info.CurrentVersion);
    }

    /// <summary>
    /// Lädt die neue Fassung, prüft sie und startet das Einspielen. Gelingt der Start,
    /// beendet sich die Anwendung – solange sie läuft, sind ihre Dateien gesperrt.
    /// </summary>
    [RelayCommand]
    public async Task InstallUpdateAsync()
    {
        UpdateSeverity = StatusSeverity.Informational;
        UpdateStatusText = _localizer.Get("Update_Preparing");

        // Der Download umfasst rund hundert Megabyte. Ohne laufende Prozentangabe wirkt
        // die Seite über Minuten hinweg, als sei nichts geschehen.
        Progress<UpdateProgress> progress = new(ReportInstallProgress);

        bool started = await _updateService
            .DownloadAndLaunchUpdaterAsync(progress, CancellationToken.None)
            .ConfigureAwait(true);

        if (started)
        {
            _shutdown.Request();
            return;
        }

        UpdateSeverity = StatusSeverity.Error;
        UpdateStatusText = _localizer.Get("Update_Failed");
    }

    // Übersetzt den Zwischenstand in einen Text. Nur der Download kennt einen echten
    // Anteil; die übrigen Abschnitte dauern kurz und werden nur benannt.
    private void ReportInstallProgress(UpdateProgress progress)
    {
        UpdateStatusText = progress.Stage switch
        {
            UpdateStage.Downloading => _localizer.Format("Update_DownloadingPercent", (int)progress.Percent),
            UpdateStage.Verifying => _localizer.Get("Update_Verifying"),
            UpdateStage.Extracting => _localizer.Get("Update_Extracting"),
            _ => _localizer.Get("Update_Starting"),
        };
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnProblemsOnlyChanged(bool value) => ApplyFilter();

    private void ApplyFilter()
    {
        IReadOnlyList<string> visible = LogEntryFilter.Apply(
            _allLines,
            ProblemsOnly ? LogFilter.ProblemsOnly : LogFilter.All,
            SearchText);

        LogText = visible.Count == 0
            ? _localizer.Get(_allLines.Count == 0 ? "About_LogEmpty" : "About_LogNoMatch")
            : string.Join(Environment.NewLine, visible);

        LogSummary = _allLines.Count == 0
            ? string.Empty
            : _localizer.Format("About_LogSummary", visible.Count, _allLines.Count);
    }

    private string BuildAiStatusText(ModelAvailability availability)
    {
        if (availability.IsReady)
        {
            return _localizer.Get("About_KiReady");
        }

        return availability.IsReachable
            ? _localizer.Format("About_KiModelsMissing", string.Join(", ", availability.MissingModels))
            : _localizer.Get("About_KiNotSetUp");
    }
}
