using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PictureSorter.App.Services;
using PictureSorter.Application.Services;
using PictureSorter.Core.Enums;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.App.ViewModels;

/// <summary>
/// ViewModel der Ablage nach Aufnahmedatum: Quellordner wählen, Stufe bestimmen,
/// Zielort angeben, Vorschau durchsehen, anwenden.
///
/// Eigene Ansicht statt eines dritten Weges im Sortier-Assistenten: Von dessen sechs
/// Schritten entfallen hier vier. Es gibt keine Kategorie, keine Beispiele, nichts
/// anzulernen und keinen Zeitraum — nur das Datum im Dateikopf entscheidet.
/// </summary>
internal sealed partial class CalendarSortViewModel : ObservableObject, IDisposable
{
    private readonly IPhotoAnalyzer _analyzer;
    private readonly IProposalApplier _applier;
    private readonly IFolderPicker _folderPicker;
    private readonly IConfirmationService _confirmationService;
    private readonly StatusBarViewModel _status;
    private readonly ILocalizer _localizer;
    private readonly ILogger<CalendarSortViewModel> _logger;

    private CancellationTokenSource? _cancellation;

    /// <summary>Ablaufzustand der Ansicht.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AnalyzeCommand))]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    [NotifyCanExecuteChangedFor(nameof(PickSourceFolderCommand))]
    [NotifyCanExecuteChangedFor(nameof(PickTargetRootCommand))]
    public partial SortState State { get; set; }

    /// <summary>Der Ordner, dessen Bilder abgelegt werden.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AnalyzeCommand))]
    public partial string SourceFolder { get; set; }

    /// <summary>
    /// Der Ort, unter dem die Kalender-Ordner entstehen. Er darf außerhalb des
    /// Quellordners liegen — etwa auf einer zweiten Platte.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AnalyzeCommand))]
    public partial string TargetRoot { get; set; }

    /// <summary><see langword="true"/>, um Unterordner einzubeziehen.</summary>
    [ObservableProperty]
    public partial bool IncludeSubfolders { get; set; }

    /// <summary>
    /// <see langword="true"/>, wenn die Bilder kopiert statt verschoben werden.
    /// </summary>
    [ObservableProperty]
    public partial bool CopyInsteadOfMove { get; set; }

    /// <summary>Wie fein unterteilt wird.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsYear))]
    [NotifyPropertyChangedFor(nameof(IsMonth))]
    [NotifyPropertyChangedFor(nameof(IsDay))]
    [NotifyPropertyChangedFor(nameof(FolderExample))]
    public partial CalendarGranularity Granularity { get; set; }

    /// <summary>
    /// Initialisiert das ViewModel.
    /// </summary>
    /// <param name="analyzer">Erzeugt die Vorschläge.</param>
    /// <param name="applier">Wendet die Vorschläge an.</param>
    /// <param name="folderPicker">Die Ordnerauswahl.</param>
    /// <param name="confirmationService">Die Rückfrage vor dem Anwenden.</param>
    /// <param name="status">Die gemeinsame Statusleiste.</param>
    /// <param name="localizer">Die Textquelle.</param>
    /// <param name="logger">Der Logger.</param>
    public CalendarSortViewModel(
        IPhotoAnalyzer analyzer,
        IProposalApplier applier,
        IFolderPicker folderPicker,
        IConfirmationService confirmationService,
        StatusBarViewModel status,
        ILocalizer localizer,
        ILogger<CalendarSortViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(analyzer);
        ArgumentNullException.ThrowIfNull(applier);
        ArgumentNullException.ThrowIfNull(folderPicker);
        ArgumentNullException.ThrowIfNull(confirmationService);
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(logger);

        _analyzer = analyzer;
        _applier = applier;
        _folderPicker = folderPicker;
        _confirmationService = confirmationService;
        _status = status;
        _localizer = localizer;
        _logger = logger;

        State = SortState.Idle;
        SourceFolder = string.Empty;
        TargetRoot = string.Empty;
        IncludeSubfolders = true;
        Granularity = CalendarGranularity.Month;

        Proposals = new ProposalListViewModel(localizer, () => IsInteractive, OnSelectionChanged);
    }

    /// <summary>Die Vorschau der Vorschläge samt Auswahl.</summary>
    public ProposalListViewModel Proposals { get; }

    /// <summary><see langword="true"/>, wenn gerade kein Vorgang läuft.</summary>
    public bool IsInteractive => State is not (SortState.Analyzing or SortState.Sorting);

    /// <summary>Die Stufe „Jahr" ist gewählt.</summary>
    public bool IsYear
    {
        get => Granularity is CalendarGranularity.Year;
        set
        {
            if (value)
            {
                Granularity = CalendarGranularity.Year;
            }
        }
    }

    /// <summary>Die Stufe „Monat" ist gewählt.</summary>
    public bool IsMonth
    {
        get => Granularity is CalendarGranularity.Month;
        set
        {
            if (value)
            {
                Granularity = CalendarGranularity.Month;
            }
        }
    }

    /// <summary>Die Stufe „Tag" ist gewählt.</summary>
    public bool IsDay
    {
        get => Granularity is CalendarGranularity.Day;
        set
        {
            if (value)
            {
                Granularity = CalendarGranularity.Day;
            }
        }
    }

    /// <summary>
    /// Ein Beispielordner zur gewählten Stufe. Er nimmt der Entscheidung das Raten ab:
    /// Man sieht vor dem Lauf, wie die Ordner heißen werden.
    /// </summary>
    public string FolderExample => Granularity switch
    {
        CalendarGranularity.Year => "2021",
        CalendarGranularity.Day => "2021-07-15",
        _ => "2021-07",
    };

    /// <summary>Analysieren ist möglich, wenn Quellordner und Zielort vorliegen.</summary>
    public bool CanAnalyze =>
        IsInteractive
        && !string.IsNullOrWhiteSpace(SourceFolder)
        && !string.IsNullOrWhiteSpace(TargetRoot);

    /// <summary>Anwenden ist möglich, wenn mindestens ein Vorschlag ausgewählt ist.</summary>
    public bool CanApply => State is SortState.Preview && Proposals.SelectedCount > 0;

    /// <summary>Abbrechen ist möglich, solange ein Vorgang läuft.</summary>
    public bool CanCancel => State is SortState.Analyzing or SortState.Sorting;

    /// <summary>
    /// <see langword="true"/>, wenn eine Vorschau vorliegt.
    /// </summary>
    public bool HasPreview => State is SortState.Preview && Proposals.Items.Count > 0;

    [RelayCommand(CanExecute = nameof(IsInteractive))]
    private async Task PickSourceFolderAsync()
    {
        string? folder = await _folderPicker.PickFolderAsync(CancellationToken.None).ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(folder))
        {
            SourceFolder = folder;

            // Der Zielort ist meist derselbe Ordner: Die Kalender-Ordner entstehen
            // darin, die Bilder rutschen eine Ebene tiefer. Vorgeschlagen, nicht
            // erzwungen — wer anderswohin will, ändert es.
            if (string.IsNullOrWhiteSpace(TargetRoot))
            {
                TargetRoot = folder;
            }
        }
    }

    [RelayCommand(CanExecute = nameof(IsInteractive))]
    private async Task PickTargetRootAsync()
    {
        string? folder = await _folderPicker.PickFolderAsync(CancellationToken.None).ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(folder))
        {
            TargetRoot = folder;
        }
    }

    [RelayCommand(CanExecute = nameof(CanAnalyze))]
    private async Task AnalyzeAsync()
    {
        State = SortState.Analyzing;
        _status.Begin(_localizer.Get("Calendar_Scanning"), Cancel);
        _cancellation = new CancellationTokenSource();

        await RunGuardedAsync(AnalyzeCoreAsync, CalendarSortLog.AnalyzeFailed, "Calendar_ScanFailed", SortState.Idle)
            .ConfigureAwait(true);
    }

    private async Task AnalyzeCoreAsync()
    {
        Progress<SortProgress> progress = new(OnProgress);

        // Die Werte werden hier gelesen, nicht im Hintergrund: Dort dürfen keine
        // Oberflächen-Eigenschaften mehr angefasst werden. Und Task.Run ist Pflicht,
        // weil das Durchsuchen des Ordners sonst den Oberflächen-Strang festhält —
        // samt Stopp-Knopf.
        string folder = SourceFolder;
        string root = TargetRoot;
        CalendarGranularity granularity = Granularity;
        bool withSubfolders = IncludeSubfolders;
        CancellationToken token = _cancellation!.Token;

        IReadOnlyList<SortProposal> proposals = await Task.Run(
            () => _analyzer.CreateCalendarProposalsAsync(
                folder, root, granularity, withSubfolders, progress, token),
            token).ConfigureAwait(true);

        Proposals.Replace(proposals);
        OnPropertyChanged(nameof(HasPreview));

        if (proposals.Count == 0)
        {
            State = SortState.Completed;
            _status.Finish(_localizer.Get("Calendar_NothingToDo"));
            return;
        }

        State = SortState.Preview;
        _status.Finish(_localizer.Format("Calendar_ProposalsReady", proposals.Count));
    }

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        IReadOnlyList<SortProposal> selected = Proposals.Selected;
        string question = _localizer.Format(
            CopyInsteadOfMove ? "Calendar_ConfirmCopy" : "Calendar_ConfirmMove",
            selected.Count,
            TargetRoot);

        if (!await _confirmationService
            .ConfirmAsync(
                _localizer.Get("Calendar_ConfirmTitle"),
                question,
                _localizer.Get("Calendar_ConfirmAccept"),
                _localizer.Get("Calendar_ConfirmReject"))
            .ConfigureAwait(true))
        {
            _status.Report(_localizer.Get("Calendar_ApplyCanceled"));
            return;
        }

        State = SortState.Sorting;
        _status.Begin(_localizer.Get("Calendar_Applying"), Cancel);
        _cancellation = new CancellationTokenSource();

        await RunGuardedAsync(ApplyCoreAsync, CalendarSortLog.ApplyFailed, "Calendar_ApplyFailed", SortState.Preview)
            .ConfigureAwait(true);
    }

    private async Task ApplyCoreAsync()
    {
        IReadOnlyList<SortProposal> selected = Proposals.Selected;
        FileOperationMode operation = CopyInsteadOfMove
            ? FileOperationMode.Copy
            : FileOperationMode.Move;
        CancellationToken token = _cancellation!.Token;

        int moved = await Task.Run(
            () => _applier.ApplyProposalsAsync(selected, operation, dryRun: false, token),
            token).ConfigureAwait(true);

        Proposals.Clear();
        OnPropertyChanged(nameof(HasPreview));
        State = SortState.Completed;
        _status.Finish(_localizer.Format("Calendar_FilesSorted", moved));
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        _cancellation?.Cancel();
        _status.Report(_localizer.Get("Calendar_Canceling"));
    }

    // Eine Hülle für beide langen Vorgänge. Ohne sie stünde derselbe Block aus
    // Abbruch-Behandlung, Fehlermeldung und Zustandsrückgabe zweimal da — und beim
    // zweiten Mal irgendwann anders.
    [SuppressMessage(
        "Design",
        "CA1031:Keine allgemeinen Ausnahmetypen abfangen",
        Justification = "Genau das ist der Zweck: Ein Fehler in einem langen Lauf darf die Anwendung nicht beenden, sondern gehört als Meldung in die Statusleiste.")]
    private async Task RunGuardedAsync(
        Func<Task> operation,
        Action<ILogger, Exception> logFailure,
        string failedMessageKey,
        SortState stateAfterFailure)
    {
        try
        {
            await operation().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            State = stateAfterFailure;
            _status.Finish(_localizer.Get("Calendar_Canceled"), StatusSeverity.Warning);
        }
        catch (Exception ex)
        {
            logFailure(_logger, ex);
            State = SortState.Error;
            _status.Finish(_localizer.Format(failedMessageKey, ex.Message), StatusSeverity.Error);
        }
        finally
        {
            _cancellation?.Dispose();
            _cancellation = null;
            NotifyStateChanged();
        }
    }

    private void OnProgress(SortProgress progress)
    {
        if (progress.Total <= 0)
        {
            _status.ReportIndeterminate(_localizer.Get("Calendar_Scanning"));
            return;
        }

        double percent = 100.0 * progress.Processed / progress.Total;
        _status.ReportProgress(
            _localizer.Format("Calendar_ScanProgress", progress.Processed, progress.Total),
            percent);
    }

    private void OnSelectionChanged()
    {
        OnPropertyChanged(nameof(CanApply));
        ApplyCommand.NotifyCanExecuteChanged();
    }

    private void NotifyStateChanged()
    {
        OnPropertyChanged(nameof(IsInteractive));
        OnPropertyChanged(nameof(CanAnalyze));
        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(HasPreview));
        AnalyzeCommand.NotifyCanExecuteChanged();
        ApplyCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        PickSourceFolderCommand.NotifyCanExecuteChanged();
        PickTargetRootCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Gibt die Abbruchquelle frei.
    /// </summary>
    public void Dispose()
    {
        _cancellation?.Dispose();
        _cancellation = null;
        GC.SuppressFinalize(this);
    }

    partial void OnStateChanged(SortState value) => NotifyStateChanged();

    partial void OnSourceFolderChanged(string value) => OnPropertyChanged(nameof(CanAnalyze));

    partial void OnTargetRootChanged(string value) => OnPropertyChanged(nameof(CanAnalyze));
}

/// <summary>
/// Quellgenerierte Logmeldungen der Ablage nach Aufnahmedatum.
/// </summary>
internal static partial class CalendarSortLog
{
    [LoggerMessage(EventId = 5230, Level = LogLevel.Error, Message = "Die Ablage nach Aufnahmedatum konnte nicht vorbereitet werden.")]
    public static partial void AnalyzeFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 5231, Level = LogLevel.Error, Message = "Die Bilder konnten nicht abgelegt werden.")]
    public static partial void ApplyFailed(ILogger logger, Exception exception);
}
