using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PictureSorter.App.Services;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.App.ViewModels;

/// <summary>
/// Das Angebot, einen protokollierten Analyselauf fortzusetzen, sein Ergebnis
/// zurückzuholen oder ihn zu verwerfen.
///
/// Eigenes Anzeige-Modell wie der Assistent und die Vorschlagsliste: Es kennt das
/// Protokoll und die Beschriftung, aber nicht den Ablauf. Was danach geschieht —
/// Zustandswechsel, Statusleiste, Vorschau — erfährt es nur über Delegaten. So bleibt
/// die Zuständigkeit an einer Stelle, ohne dass zwei Anzeige-Modelle sich kennen müssen.
/// </summary>
internal sealed partial class AnalysisResumeViewModel : ObservableObject
{
    private readonly IAnalysisJournal _journal;
    private readonly ILocalizer _localizer;
    private readonly ILogger _logger;
    private readonly Func<bool> _isInteractive;
    private readonly Func<AnalysisRun, Task> _onResume;
    private readonly Func<Task> _onRecoverFromMemory;
    private readonly Func<AnalysisRun, Task<bool>> _confirmDiscard;

    private AnalysisRun? _run;

    /// <summary>
    /// Initialisiert das Anzeige-Modell.
    /// </summary>
    /// <param name="journal">Das Protokoll der Analyseläufe.</param>
    /// <param name="localizer">Die Textquelle.</param>
    /// <param name="logger">Der Logger des übergeordneten Anzeige-Modells.</param>
    /// <param name="isInteractive">Meldet, ob gerade kein Vorgang läuft.</param>
    /// <param name="onResume">Setzt den Lauf fort beziehungsweise holt sein Ergebnis zurück.</param>
    /// <param name="onRecoverFromMemory">Holt frühere Vorschläge aus dem Sortier-Gedächtnis.</param>
    /// <param name="confirmDiscard">Fragt vor dem Verwerfen nach; liefert die Antwort.</param>
    public AnalysisResumeViewModel(
        IAnalysisJournal journal,
        ILocalizer localizer,
        ILogger logger,
        Func<bool> isInteractive,
        Func<AnalysisRun, Task> onResume,
        Func<Task> onRecoverFromMemory,
        Func<AnalysisRun, Task<bool>> confirmDiscard)
    {
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(isInteractive);
        ArgumentNullException.ThrowIfNull(onResume);
        ArgumentNullException.ThrowIfNull(onRecoverFromMemory);
        ArgumentNullException.ThrowIfNull(confirmDiscard);

        _journal = journal;
        _localizer = localizer;
        _logger = logger;
        _isInteractive = isInteractive;
        _onResume = onResume;
        _onRecoverFromMemory = onRecoverFromMemory;
        _confirmDiscard = confirmDiscard;

        Summary = string.Empty;
        ActionLabel = string.Empty;
    }

    /// <summary>Zusammenfassung des protokollierten Laufs (leer, wenn keiner vorliegt).</summary>
    [ObservableProperty]
    public partial string Summary { get; set; }

    /// <summary>
    /// Beschriftung des Knopfs: „Fortsetzen" bei einem stehengebliebenen Lauf,
    /// „Ergebnis zurückholen" bei einem abgeschlossenen. „Fortsetzen" bei einem fertigen
    /// Lauf wäre eine Falschaussage.
    /// </summary>
    [ObservableProperty]
    public partial string ActionLabel { get; set; }

    /// <summary><see langword="true"/>, wenn ein protokollierter Lauf vorliegt.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ResumeCommand))]
    [NotifyCanExecuteChangedFor(nameof(DiscardCommand))]
    public partial bool HasRun { get; set; }

    /// <summary>
    /// Fortsetzen und Verwerfen sind möglich, wenn ein Lauf vorliegt und gerade nichts läuft.
    /// </summary>
    public bool CanAct => _isInteractive() && HasRun;

    /// <summary>
    /// Meldet, dass sich der Ablaufzustand außerhalb geändert hat.
    /// </summary>
    public void NotifyStateChanged()
    {
        OnPropertyChanged(nameof(CanAct));
        ResumeCommand.NotifyCanExecuteChanged();
        DiscardCommand.NotifyCanExecuteChanged();
        RecoverFromMemoryCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Sieht nach, ob ein Lauf offen ist oder sich sein Ergebnis zurückholen lässt, und
    /// beschriftet den Hinweis entsprechend.
    ///
    /// Das Protokoll ist dauerhaft: Auch ein Lauf, der vor drei Tagen mitten in der Nacht
    /// stehengeblieben ist, wird nach dem nächsten Start wieder angeboten.
    /// </summary>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        _run = await LoadLatestAsync().ConfigureAwait(true);

        if (_run is not { } run)
        {
            HasRun = false;
            Summary = string.Empty;
            ActionLabel = string.Empty;
            return;
        }

        bool resumable = run.IsResumable;
        HasRun = true;
        ActionLabel = _localizer.Get(resumable ? "Sort_ResumeAction" : "Sort_RestoreAction");
        Summary = _localizer.Format(
            resumable ? "Sort_ResumeSummary" : "Sort_RestoreSummary",
            run.CategoryName,
            run.DecidedPhotos,
            run.TotalPhotos,
            run.LastProgressAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture));
    }

    /// <summary>Setzt den Lauf fort beziehungsweise holt sein Ergebnis zurück.</summary>
    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task ResumeAsync()
    {
        if (_run is not { } run)
        {
            return;
        }

        try
        {
            await _onResume(run).ConfigureAwait(true);
        }
        finally
        {
            await RefreshAsync().ConfigureAwait(true);
        }
    }

    /// <summary>Verwirft den Lauf, damit er nicht weiter angeboten wird.</summary>
    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task DiscardAsync()
    {
        if (_run is not { } run || !await _confirmDiscard(run).ConfigureAwait(true))
        {
            return;
        }

        await DiscardCoreAsync(run.Id).ConfigureAwait(true);
        await RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>Holt frühere Vorschläge aus dem Sortier-Gedächtnis zurück.</summary>
    [RelayCommand(CanExecute = nameof(CanRecoverFromMemory))]
    private Task RecoverFromMemoryAsync() => _onRecoverFromMemory();

    /// <summary>
    /// Aus dem Gedächtnis lässt sich auch ohne protokollierten Lauf etwas holen — das ist
    /// gerade der Weg für Läufe aus älteren Fassungen. Ob Ordner und Gruppe feststehen,
    /// weiß das übergeordnete Anzeige-Modell.
    /// </summary>
    public bool CanRecoverFromMemory => _isInteractive();

    // Das Protokoll ist eine Zugabe, kein Kernbestandteil: Ist die Datenbank gesperrt oder
    // beschädigt, wird eben kein Lauf angeboten. Ein Fehler beim Nachsehen darf nicht die
    // ganze Sortierseite mitreißen — sie wird beim Anzeigen der Seite aufgerufen.
    [SuppressMessage(
        "Design",
        "CA1031:Keine allgemeinen Ausnahmetypen abfangen",
        Justification = "Das Nachsehen im Protokoll darf das Öffnen der Seite unter keinen Umständen scheitern lassen; der Fehler wird protokolliert.")]
    private async Task<AnalysisRun?> LoadLatestAsync()
    {
        try
        {
            return await _journal.GetLatestAsync(CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            AnalysisResumeLog.JournalUnreadable(_logger, ex);
            return null;
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031:Keine allgemeinen Ausnahmetypen abfangen",
        Justification = "Wie beim Lesen: Ein Fehler im Protokoll darf die Bedienung nicht abbrechen; er wird protokolliert und der Hinweis bleibt stehen.")]
    private async Task DiscardCoreAsync(Guid runId)
    {
        try
        {
            await _journal.DiscardAsync(runId, CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            AnalysisResumeLog.JournalUnreadable(_logger, ex);
        }
    }

    partial void OnHasRunChanged(bool value) => OnPropertyChanged(nameof(CanAct));
}

/// <summary>
/// Quellgenerierte Logmeldungen des Fortsetzen-Angebots.
/// </summary>
internal static partial class AnalysisResumeLog
{
    [LoggerMessage(EventId = 5220, Level = LogLevel.Warning, Message = "Auf das Protokoll der Analyseläufe konnte nicht zugegriffen werden; es wird kein Lauf zum Fortsetzen angeboten.")]
    public static partial void JournalUnreadable(ILogger logger, Exception exception);
}
