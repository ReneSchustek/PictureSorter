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

    /// <summary>
    /// Initialisiert das ViewModel.
    /// </summary>
    /// <param name="fileLogger">Quelle der Protokollzeilen.</param>
    /// <param name="modelChecker">Prüft die Verfügbarkeit der KI-Modelle.</param>
    /// <param name="localizer">Die Textquelle.</param>
    public SettingsViewModel(
        FileLoggerProvider fileLogger,
        IModelAvailabilityChecker modelChecker,
        ILocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(fileLogger);
        ArgumentNullException.ThrowIfNull(modelChecker);
        ArgumentNullException.ThrowIfNull(localizer);

        _fileLogger = fileLogger;
        _modelChecker = modelChecker;
        _localizer = localizer;

        LogText = localizer.Get("About_LogEmpty");
        LogSummary = string.Empty;
        SearchText = string.Empty;
        AiStatusText = localizer.Get("About_KiChecking");
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
