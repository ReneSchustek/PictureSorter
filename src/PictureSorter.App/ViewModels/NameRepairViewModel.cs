using System.Collections.ObjectModel;
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
/// ViewModel der Namensreparatur: Ordner wählen, Vorschau durchsehen, umbenennen
/// und im Zweifel zurücknehmen.
///
/// Der Anlass steht in einem gemessenen Bestand: 159 von 1507 aus iCloud geladenen
/// Dateien trugen Namen aus Ersatzzeichen. Die Bilder waren in Ordnung, nur ihre
/// Namen nicht, und damit war weder zu suchen noch zu sortieren noch weiterzugeben.
///
/// Die Vorschau ist hier kein Beiwerk, sondern der Kern: Ein Name ist alles, was die
/// Nutzerin im Explorer von einem Foto sieht. Ungefragt geändert nähme er ihr die
/// Orientierung, und ein Vorschlag aus einer laufenden Nummer ist nicht in jedem Fall
/// besser als das, was dasteht. Sie entscheidet, Zeile für Zeile.
/// </summary>
internal sealed partial class NameRepairViewModel : ObservableObject, IDisposable
{
    private readonly IFileNameRepairService _repair;
    private readonly IRenameUndoService _undo;
    private readonly IFolderPicker _folderPicker;
    private readonly IFolderLookup _folderLookup;
    private readonly IConfirmationService _confirmationService;
    private readonly StatusBarViewModel _status;
    private readonly ILocalizer _localizer;
    private readonly ILogger<NameRepairViewModel> _logger;

    private CancellationTokenSource? _cancellation;

    /// <summary>Ablaufzustand der Ansicht.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    [NotifyCanExecuteChangedFor(nameof(PickFolderCommand))]
    [NotifyCanExecuteChangedFor(nameof(UndoCommand))]
    public partial SortState State { get; set; }

    /// <summary>Der Ordner, dessen Namen geprüft werden.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    public partial string SourceFolder { get; set; }

    /// <summary><see langword="true"/>, um Unterordner einzubeziehen.</summary>
    [ObservableProperty]
    public partial bool IncludeSubfolders { get; set; }

    /// <summary>Zusammenfassung des Laufs, der sich zurücknehmen lässt.</summary>
    [ObservableProperty]
    public partial string UndoSummary { get; set; }

    /// <summary><see langword="true"/>, wenn es einen Lauf zum Zurücknehmen gibt.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UndoCommand))]
    public partial bool HasUndoableRun { get; set; }

    /// <summary>
    /// Initialisiert das ViewModel.
    /// </summary>
    /// <param name="repair">Findet unlesbare Namen und benennt um.</param>
    /// <param name="undo">Nimmt einen Lauf zurück.</param>
    /// <param name="folderPicker">Die Ordnerauswahl.</param>
    /// <param name="folderLookup">Findet den Ordner, der gemeint war, wenn der Pfad ins Leere geht.</param>
    /// <param name="confirmationService">Die Rückfrage vor dem Umbenennen.</param>
    /// <param name="status">Die gemeinsame Statusleiste.</param>
    /// <param name="localizer">Die Textquelle.</param>
    /// <param name="logger">Der Logger.</param>
    public NameRepairViewModel(
        IFileNameRepairService repair,
        IRenameUndoService undo,
        IFolderPicker folderPicker,
        IFolderLookup folderLookup,
        IConfirmationService confirmationService,
        StatusBarViewModel status,
        ILocalizer localizer,
        ILogger<NameRepairViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(repair);
        ArgumentNullException.ThrowIfNull(undo);
        ArgumentNullException.ThrowIfNull(folderPicker);
        ArgumentNullException.ThrowIfNull(folderLookup);
        ArgumentNullException.ThrowIfNull(confirmationService);
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(logger);

        _repair = repair;
        _undo = undo;
        _folderPicker = folderPicker;
        _folderLookup = folderLookup;
        _confirmationService = confirmationService;
        _status = status;
        _localizer = localizer;
        _logger = logger;

        State = SortState.Idle;
        SourceFolder = string.Empty;
        UndoSummary = string.Empty;
        IncludeSubfolders = true;
    }

    /// <summary>Die Vorschau: je Zeile ein alter und ein neuer Name.</summary>
    public ObservableCollection<RenameProposalViewModel> Items { get; } = [];

    /// <summary><see langword="true"/>, wenn gerade kein Vorgang läuft.</summary>
    public bool IsInteractive => State is not (SortState.Analyzing or SortState.Sorting);

    /// <summary>Zahl der angehakten Zeilen.</summary>
    public int SelectedCount => Items.Count(item => item.IsSelected);

    /// <summary>Suchen ist möglich, wenn ein Ordner angegeben ist.</summary>
    public bool CanScan => IsInteractive && !string.IsNullOrWhiteSpace(SourceFolder);

    /// <summary>Umbenennen ist möglich, wenn mindestens eine Zeile angehakt ist.</summary>
    public bool CanApply => State is SortState.Preview && SelectedCount > 0;

    /// <summary>Abbrechen ist möglich, solange ein Vorgang läuft.</summary>
    public bool CanCancel => State is SortState.Analyzing or SortState.Sorting;

    /// <summary>Zurücknehmen ist möglich, wenn ein Lauf vorliegt und nichts läuft.</summary>
    public bool CanUndo => IsInteractive && HasUndoableRun;

    /// <summary><see langword="true"/>, wenn eine Vorschau vorliegt.</summary>
    public bool HasPreview => State is SortState.Preview && Items.Count > 0;

    /// <summary>
    /// <see langword="true"/>, solange es nichts zu zeigen gibt. Eine eigene
    /// Eigenschaft statt eines umkehrenden Konverters: Die Frage „ist die Seite noch
    /// leer" gehört ins Anzeige-Modell, nicht ins Markup.
    /// </summary>
    public bool ShowsEmptyState => !HasPreview;

    /// <summary>Wie viele Zeilen ausgewählt sind, als Satz.</summary>
    public string SelectionSummary => _localizer.Format("NameRepair_SelectionSummary", SelectedCount, Items.Count);

    /// <summary>
    /// Sieht nach, ob ein Lauf zum Zurücknehmen bereitliegt. Beim Betreten der Seite
    /// aufzurufen: Das Angebot muss auch nach einem Neustart noch stehen.
    /// </summary>
    /// <returns>Der Vorgang.</returns>
    [RelayCommand]
    public async Task RefreshUndoOfferAsync()
    {
        RenameRun? run = await _undo.GetUndoableRunAsync(CancellationToken.None).ConfigureAwait(true);
        HasUndoableRun = run is not null;
        UndoSummary = run is null
            ? string.Empty
            : _localizer.Format("NameRepair_UndoSummary", run.Items.Count, run.Folder);
    }

    [RelayCommand(CanExecute = nameof(IsInteractive))]
    private async Task PickFolderAsync()
    {
        string? folder = await _folderPicker.PickFolderAsync(CancellationToken.None).ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(folder))
        {
            SourceFolder = folder;
        }
    }

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanAsync()
    {
        State = SortState.Analyzing;
        _status.Begin(_localizer.Get("NameRepair_Scanning"), Cancel);
        _cancellation = new CancellationTokenSource();

        await RunGuardedAsync(ScanCoreAsync, NameRepairLog.ScanFailed, "NameRepair_ScanFailed", SortState.Idle)
            .ConfigureAwait(true);
    }

    private async Task ScanCoreAsync()
    {
        Progress<PhotoScanProgress> progress = new(OnProgress);

        // Die Werte werden hier gelesen, nicht im Hintergrund: Dort dürfen keine
        // Oberflächen-Eigenschaften mehr angefasst werden. Und Task.Run ist Pflicht,
        // weil das Durchsehen des Ordners sonst den Oberflächen-Strang festhielte,
        // samt Stopp-Knopf.
        string folder = SourceFolder;
        bool withSubfolders = IncludeSubfolders;
        CancellationToken token = _cancellation!.Token;

        IReadOnlyList<RenameProposal> proposals = await Task.Run(
            () => _repair.ProposeAsync(folder, withSubfolders, progress, token),
            token).ConfigureAwait(true);

        Replace(proposals);

        if (proposals.Count == 0)
        {
            State = SortState.Completed;
            _status.Finish(_localizer.Get("NameRepair_NothingToDo"));
            return;
        }

        State = SortState.Preview;
        _status.Finish(_localizer.Format("NameRepair_ProposalsReady", proposals.Count));
    }

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        int count = SelectedCount;
        if (!await _confirmationService
            .ConfirmAsync(
                _localizer.Get("NameRepair_ConfirmTitle"),
                _localizer.Format("NameRepair_ConfirmQuestion", count),
                _localizer.Get("NameRepair_ConfirmAccept"),
                _localizer.Get("NameRepair_ConfirmReject"))
            .ConfigureAwait(true))
        {
            _status.Report(_localizer.Get("NameRepair_ApplyCanceled"));
            return;
        }

        State = SortState.Sorting;
        _status.Begin(_localizer.Get("NameRepair_Applying"), Cancel);
        _cancellation = new CancellationTokenSource();

        await RunGuardedAsync(ApplyCoreAsync, NameRepairLog.ApplyFailed, "NameRepair_ApplyFailed", SortState.Preview)
            .ConfigureAwait(true);
    }

    private async Task ApplyCoreAsync()
    {
        Progress<PhotoScanProgress> progress = new(OnProgress);
        IReadOnlyList<RenameProposal> selected = [.. Items.Where(item => item.IsSelected).Select(item => item.Proposal)];
        string folder = SourceFolder;
        CancellationToken token = _cancellation!.Token;

        RenameResult result = await Task.Run(
            () => _repair.ApplyAsync(folder, selected, progress, token),
            token).ConfigureAwait(true);

        Items.Clear();
        State = SortState.Completed;
        await RefreshUndoOfferAsync().ConfigureAwait(true);

        _status.Finish(
            result.Skipped == 0
                ? _localizer.Format("NameRepair_Renamed", result.Renamed)
                : _localizer.Format("NameRepair_RenamedWithSkips", result.Renamed, result.Skipped),
            result.Skipped == 0 ? StatusSeverity.Success : StatusSeverity.Warning);
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private async Task UndoAsync()
    {
        State = SortState.Sorting;
        _status.Begin(_localizer.Get("NameRepair_Undoing"), Cancel);
        _cancellation = new CancellationTokenSource();

        await RunGuardedAsync(UndoCoreAsync, NameRepairLog.UndoFailed, "NameRepair_UndoFailed", SortState.Idle)
            .ConfigureAwait(true);
    }

    private async Task UndoCoreAsync()
    {
        CancellationToken token = _cancellation!.Token;
        UndoResult? result = await Task.Run(() => _undo.UndoLastRunAsync(token), token).ConfigureAwait(true);

        State = SortState.Completed;
        await RefreshUndoOfferAsync().ConfigureAwait(true);

        if (result is null)
        {
            _status.Finish(_localizer.Get("NameRepair_NothingToUndo"), StatusSeverity.Warning);
            return;
        }

        _status.Finish(
            result.Skipped == 0
                ? _localizer.Format("NameRepair_Undone", result.Restored)
                : _localizer.Format("NameRepair_UndoneWithSkips", result.Restored, result.Skipped),
            result.Skipped == 0 ? StatusSeverity.Success : StatusSeverity.Warning);
    }

    [RelayCommand]
    private void ToggleAll()
    {
        // Alle oder keine: Bei 159 Zeilen ist das Häkchen einzeln zu setzen keine
        // Bedienung, sondern eine Zumutung.
        bool select = SelectedCount < Items.Count;
        foreach (RenameProposalViewModel item in Items)
        {
            item.IsSelected = select;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        _cancellation?.Cancel();
        _status.Report(_localizer.Get("NameRepair_Canceling"));
    }

    private void Replace(IReadOnlyList<RenameProposal> proposals)
    {
        Items.Clear();
        foreach (RenameProposal proposal in proposals)
        {
            Items.Add(new RenameProposalViewModel(proposal, _localizer, OnSelectionChanged));
        }

        OnSelectionChanged();
        OnPropertyChanged(nameof(HasPreview));
        OnPropertyChanged(nameof(ShowsEmptyState));
    }

    // Eine Hülle für alle drei langen Vorgänge. Ohne sie stünde derselbe Block aus
    // Abbruch-Behandlung, Fehlermeldung und Zustandsrückgabe dreimal da, und beim
    // dritten Mal irgendwann anders.
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
            _status.Finish(_localizer.Get("NameRepair_Canceled"), StatusSeverity.Warning);
        }
        catch (DirectoryNotFoundException)
        {
            State = SortState.Error;
            _status.Finish(DescribeMissingFolder(), StatusSeverity.Error);
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

    // Gerade auf dieser Seite ist der Fall wahrscheinlich: Wer unlesbare Namen sucht,
    // hat einen Bestand, in dem auch der Ordnername unsichtbare Zeichen tragen kann.
    private string DescribeMissingFolder()
    {
        if (!string.IsNullOrWhiteSpace(SourceFolder))
        {
            string? lookalike = _folderLookup.FindLookalike(SourceFolder);
            if (lookalike is not null)
            {
                return _localizer.Format("NameRepair_FolderNotFoundButLookalike", lookalike);
            }
        }

        return _localizer.Get("NameRepair_FolderNotFound");
    }

    private void OnProgress(PhotoScanProgress progress)
    {
        if (progress.Total <= 0)
        {
            _status.ReportIndeterminate(_localizer.Get("NameRepair_Scanning"));
            return;
        }

        double percent = 100.0 * progress.Processed / progress.Total;
        _status.ReportProgress(
            _localizer.Format("NameRepair_ScanProgress", progress.Processed, progress.Total),
            percent);
    }

    private void OnSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectionSummary));
        OnPropertyChanged(nameof(CanApply));
        ApplyCommand.NotifyCanExecuteChanged();
    }

    private void NotifyStateChanged()
    {
        OnPropertyChanged(nameof(IsInteractive));
        OnPropertyChanged(nameof(CanScan));
        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(HasPreview));
        OnPropertyChanged(nameof(ShowsEmptyState));
        ScanCommand.NotifyCanExecuteChanged();
        ApplyCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        UndoCommand.NotifyCanExecuteChanged();
        PickFolderCommand.NotifyCanExecuteChanged();
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

    partial void OnSourceFolderChanged(string value) => OnPropertyChanged(nameof(CanScan));

    partial void OnHasUndoableRunChanged(bool value) => OnPropertyChanged(nameof(CanUndo));
}

/// <summary>
/// Quellgenerierte Logmeldungen der Namensreparatur-Ansicht.
/// </summary>
internal static partial class NameRepairLog
{
    [LoggerMessage(EventId = 5300, Level = LogLevel.Error, Message = "Die Namen konnten nicht geprüft werden.")]
    public static partial void ScanFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 5301, Level = LogLevel.Error, Message = "Die Dateien konnten nicht umbenannt werden.")]
    public static partial void ApplyFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 5302, Level = LogLevel.Error, Message = "Der Umbenennungs-Lauf konnte nicht zurückgenommen werden.")]
    public static partial void UndoFailed(ILogger logger, Exception exception);
}
