using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PictureSorter.App.Services;

namespace PictureSorter.App.ViewModels;

/// <summary>
/// Schweregrad einer Statusmeldung. Steuert die Farbgebung der Statusleiste.
/// Entspricht eins zu eins der WinUI-InfoBar-Schwere (über einen Converter
/// abgebildet, damit die Application-Schicht UI-frei bleibt).
/// </summary>
internal enum StatusSeverity
{
    /// <summary>Neutrale Information.</summary>
    Informational,

    /// <summary>Erfolgreich abgeschlossen.</summary>
    Success,

    /// <summary>Warnung (z. B. keine Bilder gefunden).</summary>
    Warning,

    /// <summary>Fehler.</summary>
    Error,
}

/// <summary>
/// Gemeinsame Statusleiste der Anwendung. Wird von den Seiten-ViewModels gefüttert
/// und vom Hauptfenster dauerhaft am unteren Rand angezeigt, sodass ein laufender
/// Vorgang auch beim Seitenwechsel sichtbar bleibt. Ein laufender Vorgang
/// registriert eine Abbruch-Aktion, die der „Stopp“-Knopf auslöst.
/// </summary>
internal sealed partial class StatusBarViewModel : ObservableObject
{
    private Action? _cancelAction;

    /// <summary>
    /// Der aktuell angezeigte Statustext.
    /// </summary>
    [ObservableProperty]
    public partial string Message { get; set; }

    /// <summary>
    /// Schweregrad der aktuellen Meldung (Farbe der Leiste).
    /// </summary>
    [ObservableProperty]
    public partial StatusSeverity Severity { get; set; }

    /// <summary>
    /// <see langword="true"/>, während ein abbrechbarer Vorgang läuft. Steuert den
    /// Fortschrittsbalken und die Sichtbarkeit des Stopp-Knopfs.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    public partial bool IsBusy { get; set; }

    /// <summary>
    /// <see langword="true"/>, wenn der Fortschritt unbestimmt ist (durchlaufender
    /// Balken); <see langword="false"/> für einen Prozentbalken (<see cref="ProgressValue"/>).
    /// </summary>
    [ObservableProperty]
    public partial bool IsIndeterminate { get; set; }

    /// <summary>
    /// Fortschritt in Prozent (0–100), wenn <see cref="IsIndeterminate"/> falsch ist.
    /// </summary>
    [ObservableProperty]
    public partial double ProgressValue { get; set; }

    /// <summary>
    /// Die laufende Programmversion, dauerhaft im Fußbereich sichtbar. Ohne sie lässt
    /// sich von außen nicht feststellen, welche Fassung gerade läuft – bei einer Meldung
    /// wie „funktioniert nicht" ist das die erste Frage.
    /// </summary>
    public string AppVersion { get; }

    /// <summary>
    /// Initialisiert die Statusleiste im Ruhezustand.
    /// </summary>
    /// <param name="localizer">Die Textquelle.</param>
    public StatusBarViewModel(ILocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        Message = localizer.Get("StatusBar_Ready");
        Severity = StatusSeverity.Informational;
        IsIndeterminate = true;
        AppVersion = localizer.Format("StatusBar_Version", UpdateService.CurrentVersion);
    }

    /// <summary>
    /// Startet die Anzeige eines laufenden Vorgangs (neutraler Schweregrad).
    /// </summary>
    /// <param name="message">Der Statustext.</param>
    /// <param name="cancel">Aktion, die den Vorgang abbricht (für den Stopp-Knopf).</param>
    public void Begin(string message, Action cancel)
    {
        ArgumentNullException.ThrowIfNull(cancel);
        _cancelAction = cancel;
        Severity = StatusSeverity.Informational;
        Message = message;
        IsIndeterminate = true;
        ProgressValue = 0;
        IsBusy = true;
    }

    /// <summary>
    /// Aktualisiert den Statustext, ohne den Lauf-Zustand zu ändern.
    /// </summary>
    /// <param name="message">Der neue Statustext.</param>
    /// <param name="severity">Der Schweregrad der Meldung.</param>
    public void Report(string message, StatusSeverity severity = StatusSeverity.Informational)
    {
        Severity = severity;
        Message = message;
    }

    /// <summary>
    /// Meldet einen bestimmten Fortschritt (Prozentbalken) samt Text.
    /// </summary>
    /// <param name="message">Der Statustext, z. B. „3 von 120 Fotos analysiert".</param>
    /// <param name="percent">Fortschritt in Prozent (0–100).</param>
    public void ReportProgress(string message, double percent)
    {
        Message = message;
        IsIndeterminate = false;
        ProgressValue = Math.Clamp(percent, 0, 100);
    }

    /// <summary>
    /// Meldet einen Abschnitt ohne bezifferbaren Anteil (durchlaufender Balken). Für
    /// Schritte, deren Dauer sich nicht vorhersagen lässt – der Balken muss trotzdem
    /// weiterlaufen, sonst wirkt der Vorgang abgebrochen.
    /// </summary>
    /// <param name="message">Der Statustext.</param>
    public void ReportIndeterminate(string message)
    {
        Message = message;
        IsIndeterminate = true;
        ProgressValue = 0;
    }

    /// <summary>
    /// Beendet die Anzeige eines laufenden Vorgangs und zeigt eine Abschlussmeldung.
    /// </summary>
    /// <param name="message">Die Abschlussmeldung.</param>
    /// <param name="severity">Der Schweregrad der Abschlussmeldung.</param>
    public void Finish(string message, StatusSeverity severity = StatusSeverity.Informational)
    {
        Severity = severity;
        Message = message;
        IsBusy = false;
        IsIndeterminate = true;
        ProgressValue = 0;
        _cancelAction = null;
    }

    private bool CanStop() => IsBusy;

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop() => _cancelAction?.Invoke();
}
