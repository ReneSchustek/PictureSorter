using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PictureSorter.App.Services;
using PictureSorter.Application.Services;
using PictureSorter.Application.Sorting;
using PictureSorter.Core.Entities;
using PictureSorter.Core.Enums;
using PictureSorter.Core.Exceptions;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.App.ViewModels;

/// <summary>
/// ViewModel der Sortier-Ansicht. Bündelt die fachlichen Use-Cases (Ordner wählen,
/// Kategorie beschreiben, Beispiele lernen, analysieren, sortieren) und den
/// KI-Verfügbarkeitshinweis. Die reine Schritt-Navigation und -Darstellung liegt im
/// komponierten <see cref="Wizard"/> (<see cref="SortWizardViewModel"/>), das die
/// Aktionen nur über Delegaten anstößt. Der Ablauf wird über den expliziten
/// <see cref="SortState"/> gesteuert.
/// </summary>
internal sealed partial class SortViewModel : ObservableObject, IDisposable
{
    private const int ExampleLimit = 30;

    private readonly IPhotoSorter _sorter;
    private readonly ISortUndoService _undo;
    private readonly IPhotoSource _photoSource;
    private readonly ICategoryTrainer _trainer;
    private readonly ICategoryRepository _categoryRepository;
    private readonly IFolderPicker _folderPicker;
    private readonly IConfirmationService _confirmationService;
    private readonly StatusBarViewModel _status;
    private readonly SortingOptions _options;
    private readonly ILocalizer _localizer;
    private readonly ILogger<SortViewModel> _logger;

    private Category? _category;
    private CancellationTokenSource? _cancellation;

    /// <summary>Expliziter Ablaufzustand der Sortier-Ansicht.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BrowseCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadExamplesCommand))]
    [NotifyCanExecuteChangedFor(nameof(LearnCommand))]
    [NotifyCanExecuteChangedFor(nameof(AnalyzeCommand))]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    public partial SortState State { get; set; }

    /// <summary>Der gewählte Quellordner.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadExamplesCommand))]
    [NotifyCanExecuteChangedFor(nameof(AnalyzeCommand))]
    public partial string SourceFolder { get; set; }

    /// <summary><see langword="true"/>, um Unterordner einzubeziehen.</summary>
    [ObservableProperty]
    public partial bool IncludeSubfolders { get; set; }

    /// <summary>Name der Kategorie (Basis des Zielordnernamens).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LearnCommand))]
    public partial string CategoryName { get; set; }

    /// <summary>Beschreibung der Kategorie in eigenen Worten.</summary>
    [ObservableProperty]
    public partial string CategoryDescription { get; set; }

    /// <summary><see langword="true"/> für eine Ereignis-Kategorie (Ordnername mit Datum).</summary>
    [ObservableProperty]
    public partial bool IsEventCategory { get; set; }

    /// <summary>
    /// <see langword="true"/>, wenn der Lauf die Fotos kopieren statt verschieben soll.
    /// Bewusst pro Lauf und nicht als dauerhafte Einstellung: Von der Speicherkarte
    /// will man kopieren, innerhalb des Archivs verschieben – das hängt am Fall.
    /// Voreinstellung bleibt das Verschieben, damit sich das gewohnte Verhalten nicht
    /// unbemerkt ändert.
    /// </summary>
    [ObservableProperty]
    public partial bool CopyInsteadOfMove { get; set; }

    /// <summary>
    /// Zusammenfassung des Laufs, der zurückgenommen werden kann (leer, wenn keiner
    /// vorliegt).
    /// </summary>
    [ObservableProperty]
    public partial string UndoSummary { get; set; }

    /// <summary>
    /// <see langword="true"/>, wenn ein Sortierlauf zurückgenommen werden kann.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UndoCommand))]
    public partial bool HasUndoableRun { get; set; }

    /// <summary>
    /// Initialisiert das ViewModel.
    /// </summary>
    /// <param name="sorter">Der Sortierdienst.</param>
    /// <param name="undo">Nimmt den letzten Sortierlauf zurück.</param>
    /// <param name="photoSource">Quelle der Beispielfotos.</param>
    /// <param name="trainer">Lernt das Kategorie-Profil aus Beispielen.</param>
    /// <param name="categoryRepository">Persistiert die gelernten Kategorien.</param>
    /// <param name="folderPicker">Der Ordnerauswahl-Dialog.</param>
    /// <param name="confirmationService">Die Rückfrage bei großen Mengen.</param>
    /// <param name="status">Die gemeinsame Statusleiste der Anwendung.</param>
    /// <param name="options">Schwellwerte der Sortierlogik.</param>
    /// <param name="localizer">Die Textquelle.</param>
    /// <param name="logger">Der Logger.</param>
    public SortViewModel(
        IPhotoSorter sorter,
        ISortUndoService undo,
        IPhotoSource photoSource,
        ICategoryTrainer trainer,
        ICategoryRepository categoryRepository,
        IFolderPicker folderPicker,
        IConfirmationService confirmationService,
        StatusBarViewModel status,
        IOptions<SortingOptions> options,
        ILocalizer localizer,
        ILogger<SortViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(sorter);
        ArgumentNullException.ThrowIfNull(undo);
        ArgumentNullException.ThrowIfNull(photoSource);
        ArgumentNullException.ThrowIfNull(trainer);
        ArgumentNullException.ThrowIfNull(categoryRepository);
        ArgumentNullException.ThrowIfNull(folderPicker);
        ArgumentNullException.ThrowIfNull(confirmationService);
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(logger);

        _sorter = sorter;
        _undo = undo;
        _photoSource = photoSource;
        _trainer = trainer;
        _categoryRepository = categoryRepository;
        _folderPicker = folderPicker;
        _confirmationService = confirmationService;
        _status = status;
        _options = options.Value;
        _localizer = localizer;
        _logger = logger;

        // Der Assistent kennt die Use-Cases nur über diese Delegaten (SRP). Er muss
        // vor den Anfangswerten stehen: deren Setter melden jede Änderung an den
        // Assistenten weiter und würden sonst auf ein noch nicht erzeugtes Objekt
        // zugreifen.
        Wizard = new SortWizardViewModel(
            () => IsInteractive,
            CanRunWizardStep,
            RunWizardStepAsync,
            ResetForRestart,
            OnWizardStepEntered,
            localizer);

        State = SortState.Idle;
        SourceFolder = string.Empty;
        CategoryName = string.Empty;
        CategoryDescription = string.Empty;
        UndoSummary = string.Empty;
    }

    /// <summary>
    /// Navigation und Darstellung des Schritt-für-Schritt-Assistenten.
    /// </summary>
    public SortWizardViewModel Wizard { get; }

    /// <summary>
    /// Die geladenen Beispiel-Kandidaten zum Anlernen der Kategorie.
    /// </summary>
    public ObservableCollection<ExampleCandidateViewModel> ExampleCandidates { get; } = [];

    /// <summary>
    /// Die erzeugten Sortiervorschläge (Vorschau). Jeder Eintrag trägt seine
    /// Auswahl; nur ausgewählte werden angewendet.
    /// </summary>
    public ObservableCollection<ProposalViewModel> Proposals { get; } = [];

    /// <summary>
    /// Anzahl der zum Sortieren ausgewählten Vorschläge.
    /// </summary>
    public int SelectedProposalCount => Proposals.Count(proposal => proposal.IsSelected);

    /// <summary>
    /// Zusammenfassung der Vorschau, z. B. „12 von 20 ausgewählt".
    /// </summary>
    public string SelectionSummary => Proposals.Count == 0
        ? string.Empty
        : _localizer.Format("Sort_SelectionSummary", SelectedProposalCount, Proposals.Count);

    /// <summary>
    /// <see langword="true"/>, wenn mindestens ein Vorschlag abgewählt ist (steuert
    /// die Beschriftung des Umschaltknopfs).
    /// </summary>
    public bool CanSelectAll => Proposals.Count > 0 && SelectedProposalCount < Proposals.Count;

    /// <summary>
    /// Name der aktuell aktiven (gelernten) Kategorie, oder leer.
    /// </summary>
    public string ActiveCategoryName => _category?.Name ?? string.Empty;

    /// <summary>
    /// <see langword="true"/>, wenn gerade keine Operation läuft.
    /// </summary>
    public bool IsInteractive => State is SortState.Idle or SortState.Preview or SortState.Completed or SortState.Error;

    /// <summary>
    /// Beispiele können geladen werden, wenn ein Ordner gewählt ist.
    /// </summary>
    public bool CanLoadExamples => IsInteractive && !string.IsNullOrWhiteSpace(SourceFolder);

    /// <summary>
    /// Gelernt werden kann, wenn ein Kategoriename und mindestens ein positives
    /// Beispiel vorliegen.
    /// </summary>
    public bool CanLearn =>
        IsInteractive
        && !string.IsNullOrWhiteSpace(CategoryName)
        && ExampleCandidates.Any(candidate => candidate.IsPositive);

    /// <summary>
    /// Analysiert werden kann, wenn Ordner und gelernte Kategorie vorliegen.
    /// </summary>
    public bool CanAnalyze =>
        IsInteractive && !string.IsNullOrWhiteSpace(SourceFolder) && _category is not null;

    /// <summary>
    /// Anwenden ist möglich, wenn mindestens ein Vorschlag ausgewählt ist.
    /// </summary>
    public bool CanApply => State is SortState.Preview && SelectedProposalCount > 0;

    /// <summary>
    /// Das Umschalten der Auswahl ist möglich, solange Vorschläge vorliegen.
    /// </summary>
    public bool CanToggleAll => IsInteractive && Proposals.Count > 0;

    /// <summary>
    /// Abbrechen ist möglich, solange ein Vorgang läuft.
    /// </summary>
    public bool CanCancel => State is SortState.Analyzing
        or SortState.Sorting
        or SortState.Learning
        or SortState.Undoing;

    /// <summary>
    /// Rückgängig ist möglich, wenn ein protokollierter Lauf vorliegt und gerade
    /// nichts läuft.
    /// </summary>
    public bool CanUndo => IsInteractive && HasUndoableRun;

    /// <summary>
    /// Eine Ordnerwahl ist möglich, solange kein Vorgang läuft.
    /// </summary>
    public bool CanBrowse => IsInteractive;

    partial void OnStateChanged(SortState value)
    {
        OnPropertyChanged(nameof(IsInteractive));
        OnPropertyChanged(nameof(CanUndo));
        UndoCommand.NotifyCanExecuteChanged();
        Wizard.NotifyStateChanged();
    }

    partial void OnHasUndoableRunChanged(bool value) => OnPropertyChanged(nameof(CanUndo));

    partial void OnSourceFolderChanged(string value)
    {
        // Bei Ordnerwechsel die alten Beispiele verwerfen, damit Schritt 3 sie für
        // den neuen Ordner automatisch neu lädt.
        if (ExampleCandidates.Count > 0)
        {
            ClearCandidates();
            LearnCommand.NotifyCanExecuteChanged();
        }

        Wizard.NotifyStateChanged();
    }

    partial void OnCategoryNameChanged(string value) => Wizard.NotifyStateChanged();

    // ── Anbindung des Assistenten (Delegaten) ──────────────────────────────────

    // Vorbedingung des Aktionsknopfs je Schritt.
    private bool CanRunWizardStep(int step) => step switch
    {
        0 => IsInteractive && !string.IsNullOrWhiteSpace(SourceFolder),
        1 => IsInteractive && !string.IsNullOrWhiteSpace(CategoryName),
        2 => CanLearn,
        3 => CanAnalyze,
        4 => CanApply,
        _ => false,
    };

    // Führt die Aktion eines Schritts aus. Schritte ohne eigene Verarbeitung
    // (Ordner, Kategorie) blättern direkt weiter; bei Lernen und Analyse wird erst
    // nach Erfolg weitergeblättert.
    private async Task<bool> RunWizardStepAsync(int step)
    {
        switch (step)
        {
            case 0:
            case 1:
                return true;
            case 2:
                await LearnAsync().ConfigureAwait(true);
                return _category is not null;
            case 3:
                await AnalyzeAsync().ConfigureAwait(true);
                return State is SortState.Preview;
            case 4:
                await ApplyAsync().ConfigureAwait(true);
                return false;
            default:
                return false;
        }
    }

    // Beim Betreten von Schritt 3 die Beispiele automatisch laden, damit der Nutzer
    // ohne Zusatzklick passende Bilder markieren kann.
    private void OnWizardStepEntered(int step)
    {
        if (step == 2
            && ExampleCandidates.Count == 0
            && !string.IsNullOrWhiteSpace(SourceFolder)
            && LoadExamplesCommand.CanExecute(parameter: null))
        {
            LoadExamplesCommand.Execute(parameter: null);
        }
    }

    // Setzt die fachlichen Daten beim Neustart zurück (der Assistent setzt die
    // Schritt-Position separat zurück). Der gewählte Ordner wird ebenfalls geleert.
    private void ResetForRestart()
    {
        _category = null;
        SourceFolder = string.Empty;
        IncludeSubfolders = false;
        CategoryName = string.Empty;
        CategoryDescription = string.Empty;
        IsEventCategory = false;
        ClearCandidates();
        ClearProposals();
        State = SortState.Idle;

        OnPropertyChanged(nameof(ActiveCategoryName));
        ApplyCommand.NotifyCanExecuteChanged();
        LearnCommand.NotifyCanExecuteChanged();
        AnalyzeCommand.NotifyCanExecuteChanged();
        _status.Report(_localizer.Get("Sort_Restarted"));
    }

    // ── Use-Cases ──────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanBrowse))]
    private async Task BrowseAsync()
    {
        string? folder = await _folderPicker.PickFolderAsync(CancellationToken.None).ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(folder))
        {
            SourceFolder = folder;
        }
    }

    [RelayCommand(CanExecute = nameof(CanLoadExamples))]
    private async Task LoadExamplesAsync()
    {
        // Begin statt Report: Nur Begin schaltet den Fortschrittsbalken ein. Mit bloßem
        // Text sah das Laden aus einem großen (oder aus der Cloud geholten) Ordner aus,
        // als sei die Anwendung stehengeblieben.
        using CancellationTokenSource cancellation = new();
        _status.Begin(_localizer.Get("Sort_LoadingExamples"), cancellation.Cancel);
        try
        {
            // Nur so viele Bilder einlesen, wie Schritt 3 überhaupt anzeigt. Vorher wurde
            // der gesamte Ordner eingelesen und erst danach abgeschnitten – bei einem
            // Ordner, dessen Dateien erst aus der Cloud geholt werden (iCloud-Fotos unter
            // Windows), lud die Auswahl von dreißig Beispielen so die ganze Mediathek.
            IReadOnlyList<Photo> photos = await _photoSource
                .GetPhotosAsync(SourceFolder, IncludeSubfolders, ExampleLimit, cancellation.Token)
                .ConfigureAwait(true);

            ClearCandidates();
            foreach (Photo photo in photos)
            {
                ExampleCandidateViewModel candidate = new(photo, _localizer);
                candidate.PropertyChanged += OnCandidateChanged;
                ExampleCandidates.Add(candidate);
            }

            LearnCommand.NotifyCanExecuteChanged();
            Wizard.NotifyStateChanged();
            if (ExampleCandidates.Count == 0)
            {
                _status.Finish(_localizer.Get("Sort_NoImagesInFolder"), StatusSeverity.Warning);
            }
            else
            {
                _status.Finish(_localizer.Format("Sort_ExamplesLoaded", ExampleCandidates.Count), StatusSeverity.Success);
            }
        }
        catch (OperationCanceledException)
        {
            _status.Finish(_localizer.Get("Sort_LoadExamplesCanceled"), StatusSeverity.Warning);
        }
        catch (DirectoryNotFoundException ex)
        {
            SortViewModelLog.LoadExamplesFailed(_logger, ex);
            _status.Finish(_localizer.Get("Sort_FolderNotFound"), StatusSeverity.Error);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SortViewModelLog.LoadExamplesFailed(_logger, ex);
            _status.Finish(_localizer.Get("Sort_FolderUnreadable"), StatusSeverity.Error);
        }
    }

    [RelayCommand(CanExecute = nameof(CanLearn))]
    private async Task LearnAsync()
    {
        using IDisposable? logScope = _logger.BeginScope("Lernen {CorrelationId}", NewCorrelationId());
        State = SortState.Learning;
        _status.Begin(_localizer.Get("Sort_Learning"), Cancel);
        _cancellation = new CancellationTokenSource();

        try
        {
            IReadOnlyList<TrainingExample> examples =
                [.. ExampleCandidates.Where(candidate => candidate.IsLabeled)
                    .Select(candidate => new TrainingExample(candidate.Photo, candidate.IsPositive))];

            CategoryKind kind = IsEventCategory ? CategoryKind.Event : CategoryKind.Topic;
            Category category = await _trainer
                .TrainAsync(CategoryName.Trim(), CategoryDescription.Trim(), kind, examples, _cancellation.Token)
                .ConfigureAwait(true);

            await PersistCategoryAsync(category, _cancellation.Token).ConfigureAwait(true);
            SetActiveCategory(category);
            State = SortState.Idle;
            _status.Finish(_localizer.Format("Sort_CategoryLearned", category.Name), StatusSeverity.Success);
        }
        catch (OperationCanceledException)
        {
            State = SortState.Idle;
            _status.Finish(_localizer.Get("Sort_LearnCanceled"), StatusSeverity.Warning);
        }
        catch (AiUnavailableException ex)
        {
            SortViewModelLog.LearnFailed(_logger, ex);
            State = SortState.Error;
            _status.Finish(_localizer.Get("Sort_AiUnavailable"), StatusSeverity.Error);
        }
        finally
        {
            DisposeCancellation();
        }
    }

    [RelayCommand(CanExecute = nameof(CanAnalyze))]
    private async Task AnalyzeAsync()
    {
        using IDisposable? logScope = _logger.BeginScope("Analyse {CorrelationId}", NewCorrelationId());
        State = SortState.Analyzing;
        _status.Begin(_localizer.Get("Sort_Analyzing"), Cancel);
        _cancellation = new CancellationTokenSource();

        try
        {
            Progress<SortProgress> progress = new(OnAnalyzeProgress);
            IReadOnlyList<SortProposal> proposals = await _sorter
                .CreateProposalsAsync(SourceFolder, _category!, IncludeSubfolders, progress, _cancellation.Token)
                .ConfigureAwait(true);

            ReplaceProposals(proposals);
            State = SortState.Preview;
            _status.Finish(
                proposals.Count == 0
                    ? _localizer.Get("Sort_NoMatchingPhotos")
                    : _localizer.Format("Sort_ProposalsFound", proposals.Count),
                proposals.Count == 0 ? StatusSeverity.Warning : StatusSeverity.Success);
        }
        catch (OperationCanceledException)
        {
            State = SortState.Idle;
            _status.Finish(_localizer.Get("Sort_AnalyzeCanceled"), StatusSeverity.Warning);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SortViewModelLog.AnalyzeFailed(_logger, ex);
            State = SortState.Error;
            _status.Finish(_localizer.Get("Sort_AnalyzeFailed"), StatusSeverity.Error);
        }
        finally
        {
            DisposeCancellation();
        }
    }

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        using IDisposable? logScope = _logger.BeginScope("Sortieren {CorrelationId}", NewCorrelationId());
        if (!await ConfirmBulkAsync().ConfigureAwait(true))
        {
            _status.Report(_localizer.Get("Sort_ApplyCanceled"));
            return;
        }

        State = SortState.Sorting;
        _status.Begin(_localizer.Get("Sort_Sorting"), Cancel);
        _cancellation = new CancellationTokenSource();

        try
        {
            IReadOnlyList<SortProposal> selected =
                [.. Proposals.Where(proposal => proposal.IsSelected).Select(proposal => proposal.Proposal)];
            IReadOnlyList<SortProposal> rejected =
                [.. Proposals.Where(proposal => !proposal.IsSelected).Select(proposal => proposal.Proposal)];

            FileOperationMode operation = CopyInsteadOfMove
                ? FileOperationMode.Copy
                : FileOperationMode.Move;

            int moved = await _sorter
                .ApplyProposalsAsync(selected, operation, dryRun: false, _cancellation.Token)
                .ConfigureAwait(true);

            // Abgewählte Vorschläge dauerhaft merken, damit sie nicht erneut erscheinen.
            if (rejected.Count > 0)
            {
                await _sorter.IgnoreProposalsAsync(rejected, _cancellation.Token).ConfigureAwait(true);
            }

            ClearProposals();
            State = SortState.Completed;
            _status.Finish(
                rejected.Count == 0
                    ? _localizer.Format("Sort_FilesSorted", moved)
                    : _localizer.Format("Sort_FilesSortedWithRejected", moved, rejected.Count),
                StatusSeverity.Success);
        }
        catch (OperationCanceledException)
        {
            State = SortState.Preview;
            _status.Finish(_localizer.Get("Sort_ApplyCanceled"), StatusSeverity.Warning);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SortViewModelLog.ApplyFailed(_logger, ex);
            State = SortState.Error;
            _status.Finish(_localizer.Get("Sort_ApplyFailed"), StatusSeverity.Error);
        }
        finally
        {
            DisposeCancellation();

            // Erst jetzt steht der Lauf im Protokoll – der Hinweis „rückgängig machen"
            // erscheint unmittelbar nach dem Sortieren.
            await RefreshUndoStateAsync().ConfigureAwait(true);
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        _cancellation?.Cancel();
        _status.Report(_localizer.Get("Common_CancelRequested"));
    }

    /// <summary>
    /// Prüft, ob ein Sortierlauf zurückgenommen werden kann, und beschriftet den
    /// Hinweis entsprechend. Das Protokoll ist dauerhaft: Auch ein Lauf von gestern
    /// lässt sich nach einem Neustart noch zurücknehmen.
    /// </summary>
    [RelayCommand]
    private async Task RefreshUndoStateAsync()
    {
        SortRun? run = await _undo.GetUndoableRunAsync(CancellationToken.None).ConfigureAwait(true);
        if (run is null)
        {
            HasUndoableRun = false;
            UndoSummary = string.Empty;
            return;
        }

        UndoSummary = _localizer.Format(
            "Sort_UndoSummary",
            run.Items.Count,
            run.CategoryName,
            run.StartedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture));
        HasUndoableRun = true;
    }

    /// <summary>
    /// Holt die Fotos des letzten Sortierlaufs an ihren Ursprungsort zurück.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanUndo))]
    private async Task UndoAsync()
    {
        using IDisposable? logScope = _logger.BeginScope("Rückgängig {CorrelationId}", NewCorrelationId());

        SortRun? run = await _undo.GetUndoableRunAsync(CancellationToken.None).ConfigureAwait(true);
        if (run is null)
        {
            await RefreshUndoStateAsync().ConfigureAwait(true);
            _status.Report(_localizer.Get("Sort_UndoNothing"), StatusSeverity.Warning);
            return;
        }

        bool confirmed = await _confirmationService.ConfirmAsync(
            _localizer.Get("Sort_UndoTitle"),
            _localizer.Format("Sort_UndoMessage", run.Items.Count),
            _localizer.Get("Sort_UndoPrimary"),
            _localizer.Get("Common_Cancel")).ConfigureAwait(true);
        if (!confirmed)
        {
            return;
        }

        State = SortState.Undoing;
        _status.Begin(_localizer.Get("Sort_Undoing"), Cancel);
        _cancellation = new CancellationTokenSource();

        try
        {
            UndoResult? result = await _undo.UndoLastRunAsync(_cancellation.Token).ConfigureAwait(true);
            State = SortState.Idle;

            // Nicht zurückgeholte Fotos werden ausgewiesen statt verschwiegen: Sie
            // liegen weiterhin im Kategorie-Ordner, und die Nutzerin muss das erfahren.
            _status.Finish(
                result is null || result.Skipped == 0
                    ? _localizer.Format("Sort_UndoDone", result?.Restored ?? 0)
                    : _localizer.Format("Sort_UndoDoneWithSkipped", result.Restored, result.Skipped),
                result is { Skipped: > 0 } ? StatusSeverity.Warning : StatusSeverity.Success);
        }
        catch (OperationCanceledException)
        {
            // Bereits zurückgeholte Fotos bleiben zurückgeholt; der Lauf gilt weiter
            // als offen und kann erneut zurückgenommen werden.
            State = SortState.Idle;
            _status.Finish(_localizer.Get("Sort_UndoCanceled"), StatusSeverity.Warning);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SortViewModelLog.UndoFailed(_logger, ex);
            State = SortState.Error;
            _status.Finish(_localizer.Get("Sort_UndoFailed"), StatusSeverity.Error);
        }
        finally
        {
            DisposeCancellation();
            await RefreshUndoStateAsync().ConfigureAwait(true);
        }
    }

    // ── Hilfsfunktionen ────────────────────────────────────────────────────────

    private void OnAnalyzeProgress(SortProgress progress)
    {
        double percent = progress.Total > 0 ? progress.Processed * 100.0 / progress.Total : 0;
        _status.ReportProgress(
            _localizer.Format("Sort_AnalyzeProgress", progress.Processed, progress.Total),
            percent);
    }

    // Kurze Korrelations-ID je Vorgang. Als Logging-Scope geöffnet, verknüpft sie
    // alle Logeinträge eines Laufs – auch die der nachgelagerten Ollama-Aufrufe.
    private static string NewCorrelationId() => Guid.NewGuid().ToString("N")[..8];

    private async Task<bool> ConfirmBulkAsync()
    {
        if (Proposals.Count < _options.BulkConfirmationThreshold)
        {
            return true;
        }

        return await _confirmationService.ConfirmAsync(
            _localizer.Get("Sort_BulkConfirmTitle"),
            _localizer.Format("Sort_BulkConfirmMessage", Proposals.Count),
            _localizer.Get("Sort_BulkConfirmPrimary"),
            _localizer.Get("Common_Cancel")).ConfigureAwait(true);
    }

    private async Task PersistCategoryAsync(Category category, CancellationToken cancellationToken)
    {
        IReadOnlyList<Category> existing = await _categoryRepository.LoadAllAsync(cancellationToken).ConfigureAwait(true);
        List<Category> merged =
            [.. existing.Where(item => !string.Equals(item.Name, category.Name, StringComparison.OrdinalIgnoreCase)), category];
        await _categoryRepository.SaveAllAsync(merged, cancellationToken).ConfigureAwait(true);
    }

    private void SetActiveCategory(Category category)
    {
        _category = category;
        OnPropertyChanged(nameof(ActiveCategoryName));
        AnalyzeCommand.NotifyCanExecuteChanged();
        Wizard.NotifyStateChanged();
    }

    /// <summary>
    /// Wählt alle Vorschläge aus bzw. hebt die Auswahl für alle auf.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanToggleAll))]
    private void ToggleAll()
    {
        bool select = CanSelectAll;
        foreach (ProposalViewModel proposal in Proposals)
        {
            proposal.IsSelected = select;
        }
    }

    private void ReplaceProposals(IReadOnlyList<SortProposal> proposals)
    {
        ClearProposals();
        foreach (SortProposal proposal in proposals)
        {
            ProposalViewModel viewModel = new(proposal, _localizer);
            viewModel.PropertyChanged += OnProposalChanged;
            Proposals.Add(viewModel);
        }

        NotifyProposalsChanged();
    }

    private void ClearProposals()
    {
        foreach (ProposalViewModel proposal in Proposals)
        {
            proposal.PropertyChanged -= OnProposalChanged;
        }

        Proposals.Clear();
        NotifyProposalsChanged();
    }

    private void OnProposalChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProposalViewModel.IsSelected))
        {
            NotifyProposalsChanged();
        }
    }

    private void NotifyProposalsChanged()
    {
        OnPropertyChanged(nameof(SelectedProposalCount));
        OnPropertyChanged(nameof(SelectionSummary));
        OnPropertyChanged(nameof(CanSelectAll));
        ApplyCommand.NotifyCanExecuteChanged();
        ToggleAllCommand.NotifyCanExecuteChanged();
        Wizard.NotifyStateChanged();
    }

    private void OnCandidateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ExampleCandidateViewModel.IsPositive) or nameof(ExampleCandidateViewModel.IsNegative))
        {
            LearnCommand.NotifyCanExecuteChanged();
            Wizard.NotifyStateChanged();
        }
    }

    private void ClearCandidates()
    {
        foreach (ExampleCandidateViewModel candidate in ExampleCandidates)
        {
            candidate.PropertyChanged -= OnCandidateChanged;
        }

        ExampleCandidates.Clear();
    }

    private void DisposeCancellation()
    {
        _cancellation?.Dispose();
        _cancellation = null;
    }

    /// <summary>
    /// Gibt Abonnements und das laufende Abbruch-Token frei.
    /// </summary>
    public void Dispose()
    {
        ClearCandidates();
        ClearProposals();
        DisposeCancellation();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Quellgenerierte Logmeldungen des Sortier-ViewModels.
/// </summary>
internal static partial class SortViewModelLog
{
    [LoggerMessage(EventId = 3100, Level = LogLevel.Error, Message = "Analyse fehlgeschlagen.")]
    public static partial void AnalyzeFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 3101, Level = LogLevel.Error, Message = "Sortierung fehlgeschlagen.")]
    public static partial void ApplyFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 3102, Level = LogLevel.Error, Message = "Beispiele konnten nicht geladen werden.")]
    public static partial void LoadExamplesFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 3103, Level = LogLevel.Error, Message = "Lernen des Kategorie-Profils fehlgeschlagen.")]
    public static partial void LearnFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 3104, Level = LogLevel.Error, Message = "Das Zurückholen der Fotos ist fehlgeschlagen.")]
    public static partial void UndoFailed(ILogger logger, Exception exception);
}
