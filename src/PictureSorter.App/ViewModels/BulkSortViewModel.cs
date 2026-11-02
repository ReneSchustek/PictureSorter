using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PictureSorter.App.Services;
using PictureSorter.Application.Services;
using PictureSorter.Application.Sorting;
using PictureSorter.Core.Enums;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.App.ViewModels;

/// <summary>
/// Sortiert einen ganzen Ordner in einem Durchlauf in seine Unterordner und weitere benannte Ziele. Jede
/// Korrektur der Nutzerin passt die übrigen Vorschläge an, ohne ein Bild neu zu berechnen.
/// </summary>
internal sealed partial class BulkSortViewModel : ObservableObject, IDisposable
{
    private readonly IBulkSortService _service;
    private readonly IProposalApplier _applier;
    private readonly IFolderPicker _folderPicker;
    private readonly IConfirmationService _confirmationService;
    private readonly StatusBarViewModel _status;
    private readonly ILocalizer _localizer;
    private readonly ILogger<BulkSortViewModel> _logger;

    private CancellationTokenSource? _cancellation;
    private BulkSortSession? _session;
    private Task _recalculation = Task.CompletedTask;
    private bool _recalculationPending;

    /// <summary>Ablaufzustand der Ansicht.</summary>
    [ObservableProperty]
    public partial SortState State { get; set; }

    /// <summary>Der Ordner, dessen Bilder einsortiert werden.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AnalyzeCommand))]
    [NotifyPropertyChangedFor(nameof(CanAnalyze))]
    public partial string SourceFolder { get; set; } = string.Empty;

    /// <summary>Weitere Ziele, durch Komma oder Zeilenumbruch getrennt.</summary>
    [ObservableProperty]
    public partial string NewFolderNames { get; set; } = string.Empty;

    /// <summary><see langword="true"/>, wenn die Bilder kopiert statt verschoben werden.</summary>
    [ObservableProperty]
    public partial bool CopyInsteadOfMove { get; set; }

    /// <summary>Ort, unter dem die Zielordner entstehen; leer für „im Quellordner".</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApply))]
    [NotifyPropertyChangedFor(nameof(HasInvalidTargetRoot))]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    public partial string TargetRootFolder { get; set; } = string.Empty;

    /// <summary>Zusammenfassung der Vorschau.</summary>
    [ObservableProperty]
    public partial string Summary { get; set; } = string.Empty;

    /// <summary>
    /// Was die Nutzerin zu den Vorschlägen wissen muss: Grenzen, Ordner ohne Beispiele, nicht gelesene Dateien.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotes))]
    public partial string Notes { get; set; } = string.Empty;

    /// <summary><see langword="true"/>, wenn es Hinweise gibt.</summary>
    public bool HasNotes => Notes.Length > 0;

    /// <summary>
    /// Initialisiert das ViewModel.
    /// </summary>
    /// <param name="service">Führt den Durchlauf aus.</param>
    /// <param name="applier">Wendet die Vorschläge an.</param>
    /// <param name="folderPicker">Die Ordnerauswahl.</param>
    /// <param name="confirmationService">Die Rückfrage vor dem Anwenden.</param>
    /// <param name="status">Die gemeinsame Statusleiste.</param>
    /// <param name="localizer">Die Textquelle.</param>
    /// <param name="logger">Der Logger.</param>
    public BulkSortViewModel(
        IBulkSortService service,
        IProposalApplier applier,
        IFolderPicker folderPicker,
        IConfirmationService confirmationService,
        StatusBarViewModel status,
        ILocalizer localizer,
        ILogger<BulkSortViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(applier);
        ArgumentNullException.ThrowIfNull(folderPicker);
        ArgumentNullException.ThrowIfNull(confirmationService);
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(logger);

        _service = service;
        _applier = applier;
        _folderPicker = folderPicker;
        _confirmationService = confirmationService;
        _status = status;
        _localizer = localizer;
        _logger = logger;

        State = SortState.Idle;
    }

    /// <summary>Die Fotos mit ihrem Vorschlag.</summary>
    public ObservableCollection<BulkItemViewModel> Items { get; } = [];

    /// <summary><see langword="true"/>, wenn gerade kein Vorgang läuft.</summary>
    public bool IsInteractive => State is not (SortState.Analyzing or SortState.Sorting);

    /// <summary>Der Durchlauf ist möglich, sobald ein Ordner genannt ist.</summary>
    public bool CanAnalyze => IsInteractive && !string.IsNullOrWhiteSpace(SourceFolder);

    /// <summary>Einsortieren ist möglich, wenn mindestens ein Foto einen Ordner hat.</summary>
    public bool CanApply =>
        State is SortState.Preview && !HasInvalidTargetRoot && Items.Any(static item => item.HasFolder);

    /// <summary>
    /// <see langword="true"/>, wenn ein Zielort eingetragen ist, der kein vollständiger Pfad ist. Ein
    /// relativer Pfad hinge vom Arbeitsverzeichnis des Programms ab.
    /// </summary>
    public bool HasInvalidTargetRoot =>
        !string.IsNullOrWhiteSpace(TargetRootFolder) && !Path.IsPathFullyQualified(TargetRootFolder);

    /// <summary>Abbrechen ist möglich, solange ein Vorgang läuft.</summary>
    public bool CanCancel => !IsInteractive;

    /// <summary><see langword="true"/>, wenn eine Vorschau vorliegt.</summary>
    public bool HasPreview => State is SortState.Preview && Items.Count > 0;

    /// <summary>
    /// Der Stand der letzten Neuberechnung. Für die Tests: Die Berechnung läuft im Hintergrund, damit
    /// die Liste beim Korrigieren nicht stockt.
    /// </summary>
    internal Task Recalculation => _recalculation;

    /// <inheritdoc />
    public void Dispose()
    {
        _cancellation?.Dispose();
        _cancellation = null;
    }

    [RelayCommand(CanExecute = nameof(IsInteractive))]
    private async Task PickSourceFolderAsync()
    {
        string? folder = await _folderPicker.PickFolderAsync(CancellationToken.None).ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(folder))
        {
            SourceFolder = folder;
        }
    }

    // Nicht an CanApply gebunden: Steht ein ungültiger Pfad im Feld, ist dieser Knopf der Weg, ihn zu ersetzen.
    [RelayCommand(CanExecute = nameof(IsInteractive))]
    private async Task PickTargetRootAsync()
    {
        string? folder = await _folderPicker.PickFolderAsync(CancellationToken.None).ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(folder))
        {
            TargetRootFolder = folder;
        }
    }

    [RelayCommand(CanExecute = nameof(CanAnalyze))]
    private async Task AnalyzeAsync()
    {
        State = SortState.Analyzing;
        _status.Begin(_localizer.Get("Bulk_Preparing"), Cancel);
        _cancellation = new CancellationTokenSource();

        await RunGuardedAsync(AnalyzeCoreAsync, BulkSortUiLog.AnalyzeFailed, "Bulk_AnalyzeFailed", SortState.Idle).ConfigureAwait(true);
    }

    private async Task AnalyzeCoreAsync()
    {
        Progress<BulkSortProgress> progress = new(OnProgress);

        // Hier gelesen, nicht im Hintergrund: Dort dürfen keine Oberflächen-Eigenschaften mehr angefasst werden.
        string folder = SourceFolder;
        string[] names = ParseNames(NewFolderNames);
        CancellationToken token = _cancellation!.Token;

        BulkSortSession session = await Task.Run(
            () => _service.AnalyzeAsync(folder, names, progress, token),
            token).ConfigureAwait(true);
        IReadOnlyList<BulkSuggestion> suggestions = await Task.Run(session.Calculate, token).ConfigureAwait(true);

        _session = session;
        ShowInitial(suggestions, session.CategoryNames);

        if (Items.Count == 0 || session.CategoryNames.Count == 0)
        {
            State = SortState.Completed;
            _status.Finish(_localizer.Get(session.CategoryNames.Count == 0 ? "Bulk_NoFolders" : "Bulk_NothingToDo"), StatusSeverity.Warning);
            return;
        }

        State = SortState.Preview;
        _status.Finish(Summary);
    }

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        int count = Items.Count(static item => item.HasFolder);
        string question = _localizer.Format(CopyInsteadOfMove ? "Bulk_ConfirmCopy" : "Bulk_ConfirmMove", count);

        if (!await _confirmationService
            .ConfirmAsync(
                _localizer.Get("Bulk_ConfirmTitle"),
                question,
                _localizer.Get("Bulk_ConfirmAccept"),
                _localizer.Get("Bulk_ConfirmReject"))
            .ConfigureAwait(true))
        {
            _status.Report(_localizer.Get("Bulk_ApplyCanceled"));
            return;
        }

        State = SortState.Sorting;
        _status.Begin(_localizer.Get("Bulk_Applying"), Cancel);
        _cancellation = new CancellationTokenSource();

        await RunGuardedAsync(ApplyCoreAsync, BulkSortUiLog.ApplyFailed, "Bulk_ApplyFailed", SortState.Preview).ConfigureAwait(true);
    }

    private async Task ApplyCoreAsync()
    {
        // Eine laufende Neuberechnung darf die Liste nicht mehr ändern, während sie abgearbeitet wird.
        await _recalculation.ConfigureAwait(true);

        BulkSortSession session = _session!;
        string targetRoot = TargetRootFolder.Trim();
        List<SortProposal> proposals = [.. Items
            .Where(static item => item.HasFolder)
            .Select(item => session.ToProposal(item.ToSuggestion()))
            .Select(proposal => targetRoot.Length == 0 ? proposal : TargetFolderNaming.WithTargetRoot(proposal, targetRoot))];

        FileOperationMode operation = CopyInsteadOfMove ? FileOperationMode.Copy : FileOperationMode.Move;
        CancellationToken token = _cancellation!.Token;

        int moved = await Task.Run(
            () => _applier.ApplyProposalsAsync(proposals, operation, dryRun: false, token),
            token).ConfigureAwait(true);

        Items.Clear();
        _session = null;
        Summary = string.Empty;
        Notes = string.Empty;
        State = SortState.Completed;
        _status.Finish(_localizer.Format("Bulk_FilesSorted", moved), StatusSeverity.Success);
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        _cancellation?.Cancel();
        _status.Report(_localizer.Get("Bulk_Canceling"));
    }

    [SuppressMessage(
        "Design",
        "CA1031:Keine allgemeinen Ausnahmetypen abfangen",
        Justification = "Ein Fehler in einem langen Lauf darf die Anwendung nicht beenden, sondern gehört als Meldung in die Statusleiste.")]
    private async Task RunGuardedAsync(
        Func<Task> operation,
        Action<ILogger, Exception> logFailure,
        string failedMessageKey,
        SortState stateAfterCancel)
    {
        try
        {
            await operation().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            State = stateAfterCancel;
            _status.Finish(_localizer.Get("Bulk_Canceled"), StatusSeverity.Warning);
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

    private void OnProgress(BulkSortProgress progress)
    {
        string key = progress.Phase switch
        {
            BulkSortPhase.Downloading => "Bulk_Downloading",
            BulkSortPhase.Examples => "Bulk_ReadingFolders",
            _ => "Bulk_ReadingPhotos",
        };

        if (progress.Total <= 0)
        {
            _status.ReportIndeterminate(_localizer.Get("Bulk_Preparing"));
            return;
        }

        // Beim Laden sind die Zahlen Bytes; Megabyte liest sich besser.
        (long done, long total) = progress.Phase is BulkSortPhase.Downloading
            ? (progress.Processed / (1024 * 1024), progress.Total / (1024 * 1024))
            : (progress.Processed, progress.Total);

        _status.ReportProgress(_localizer.Format(key, done, total), 100.0 * progress.Processed / progress.Total);
    }

    private void ShowInitial(IReadOnlyList<BulkSuggestion> suggestions, IReadOnlyList<string> categories)
    {
        string noFolder = _localizer.Get("Bulk_NoFolderChoice");
        IReadOnlyList<string> options = [noFolder, .. categories];

        Items.Clear();

        // Sichere Vorschläge zuerst: Dort ist die Trefferquote am höchsten, und die Nutzerin sieht gleich,
        // was stimmt. Nach Korrekturen bleibt die Reihenfolge stehen, sonst spränge die Liste unter dem Finger.
        foreach (BulkSuggestion suggestion in suggestions
            .OrderBy(static suggestion => suggestion.CategoryName is null)
            .ThenBy(static suggestion => suggestion.CategoryName, StringComparer.CurrentCulture)
            .ThenByDescending(static suggestion => suggestion.Score))
        {
            Items.Add(new BulkItemViewModel(suggestion, options, noFolder, OnUserDecided));
        }

        UpdateSummary();
    }

    private void OnUserDecided(BulkItemViewModel item, string? previousFolder)
    {
        _session?.Decide(item.Photo, item.Folder, previousFolder);
        UpdateSummary();
        NotifyStateChanged();
        ScheduleRecalculation();
    }

    // Korrekturen kommen schneller, als gerechnet wird. Deshalb läuft höchstens eine Berechnung, und
    // danach genau eine weitere, falls inzwischen etwas dazukam.
    private void ScheduleRecalculation()
    {
        if (!_recalculation.IsCompleted)
        {
            _recalculationPending = true;
            return;
        }

        _recalculation = RecalculateAsync();
    }

    private async Task RecalculateAsync()
    {
        do
        {
            _recalculationPending = false;
            BulkSortSession? session = _session;
            if (session is null)
            {
                return;
            }

            IReadOnlyList<BulkSuggestion> suggestions = await Task.Run(session.Calculate).ConfigureAwait(true);

            Dictionary<string, BulkSuggestion> byPath = suggestions.ToDictionary(static suggestion => suggestion.Photo.FullPath, StringComparer.Ordinal);
            foreach (BulkItemViewModel item in Items)
            {
                if (byPath.TryGetValue(item.Photo.FullPath, out BulkSuggestion? updated))
                {
                    item.Update(updated);
                }
            }

            UpdateSummary();
            NotifyStateChanged();
        }
        while (_recalculationPending);
    }

    private void UpdateSummary()
    {
        int withFolder = Items.Count(static item => item.HasFolder);
        Summary = _localizer.Format("Bulk_Summary", withFolder, Items.Count - withFolder);
        Notes = BuildNotes();
    }

    private string BuildNotes()
    {
        if (_session is not BulkSortSession session)
        {
            return string.Empty;
        }

        List<string> notes = [];
        if (session.YearLimitedCategoryNames.Count > 0)
        {
            notes.Add(_localizer.Format("Bulk_YearLimitNote", string.Join(", ", session.YearLimitedCategoryNames)));
        }

        if (session.CategoryNamesWithoutExamples.Count > 0)
        {
            notes.Add(_localizer.Format("Bulk_NoExamplesNote", string.Join(", ", session.CategoryNamesWithoutExamples)));
        }

        if (session.UnsupportedFileCount > 0)
        {
            notes.Add(_localizer.Format("Bulk_UnsupportedNote", session.UnsupportedFileCount));
        }

        if (session.UnreadablePhotoCount > 0)
        {
            notes.Add(_localizer.Format("Bulk_UnreadableNote", session.UnreadablePhotoCount));
        }

        return string.Join(Environment.NewLine, notes);
    }

    private static string[] ParseNames(string names) =>
        names.Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

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

    partial void OnStateChanged(SortState value) => NotifyStateChanged();
}

/// <summary>
/// Quellgenerierte Logmeldungen des Sammeldurchlaufs in der Oberfläche.
/// </summary>
internal static partial class BulkSortUiLog
{
    [LoggerMessage(EventId = 5260, Level = LogLevel.Error, Message = "Der Sammeldurchlauf ist gescheitert.")]
    public static partial void AnalyzeFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 5261, Level = LogLevel.Error, Message = "Die Bilder des Sammeldurchlaufs konnten nicht einsortiert werden.")]
    public static partial void ApplyFailed(ILogger logger, Exception exception);
}
