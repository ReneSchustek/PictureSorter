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

    // Startpunkt des nächsten Vorschlags-Schwungs, für beide Seiten getrennt: Sie
    // schöpfen aus demselben Ordner, sollen aber nicht dieselben Bilder vorschlagen.
    private int _positiveOffset;
    private int _negativeOffset;

    /// <summary>Expliziter Ablaufzustand der Sortier-Ansicht.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BrowseCommand))]
    [NotifyCanExecuteChangedFor(nameof(SuggestPositivesCommand))]
    [NotifyCanExecuteChangedFor(nameof(SuggestNegativesCommand))]
    [NotifyCanExecuteChangedFor(nameof(PickPositivesCommand))]
    [NotifyCanExecuteChangedFor(nameof(PickNegativesCommand))]
    [NotifyCanExecuteChangedFor(nameof(LearnCommand))]
    [NotifyCanExecuteChangedFor(nameof(AnalyzeCommand))]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    public partial SortState State { get; set; }

    /// <summary>Der gewählte Quellordner.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SuggestPositivesCommand))]
    [NotifyCanExecuteChangedFor(nameof(SuggestNegativesCommand))]
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

        // Beide Seiten der Beispielauswahl mit eigener Obergrenze. Sie stehen vor dem
        // Assistenten, weil dessen Vorbedingungen ihren Inhalt lesen.
        PositiveExamples = new ExampleSetViewModel(_options.MaxExamplesPerSide, localizer, OnExampleSetChanged);
        NegativeExamples = new ExampleSetViewModel(_options.MaxExamplesPerSide, localizer, OnExampleSetChanged);

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
    public ExampleSetViewModel PositiveExamples { get; }

    /// <summary>
    /// Die Bilder, die ausdrücklich NICHT zur Gruppe gehören. Eigene Seite mit eigener
    /// Obergrenze: In einer gemeinsamen Liste teilten sich beide Seiten das Kontingent,
    /// und wer zuerst passende Bilder sammelte, hatte für die Gegenbeispiele keinen
    /// Platz mehr.
    /// </summary>
    public ExampleSetViewModel NegativeExamples { get; }

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
        && PositiveExamples.Items.Count > 0;

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
        // Bei Ordnerwechsel die alten Beispiele verwerfen: Sie stammen aus dem vorigen
        // Ordner und würden dort weiterwirken, wo sie niemand mehr erwartet. Der
        // Startpunkt beginnt wieder vorn – sonst überspränge der neue Ordner ohne
        // Grund seine ersten Bilder.
        _positiveOffset = 0;
        _negativeOffset = 0;
        if (!PositiveExamples.IsEmpty || !NegativeExamples.IsEmpty)
        {
            PositiveExamples.Clear();
            NegativeExamples.Clear();
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
        2 => IsInteractive && PositiveExamples.Items.Count > 0,
        3 => CanLearn,
        4 => CanAnalyze,
        5 => CanApply,
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
            case 2:
                return true;
            case 3:
                await LearnAsync().ConfigureAwait(true);
                return _category is not null;
            case 4:
                await AnalyzeAsync().ConfigureAwait(true);
                return State is SortState.Preview;
            case 5:
                await ApplyAsync().ConfigureAwait(true);
                return false;
            default:
                return false;
        }
    }

    // Bewusst ohne automatisches Laden: Beide Seiten beginnen leer. Vorher standen
    // dreißig zufällige Bilder des Ordners bereits drin, von denen zu einem bestimmten
    // Thema oft kaum eines passte – der Platz war trotzdem belegt.
    private void OnWizardStepEntered(int step) => Wizard.NotifyStateChanged();

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
        PositiveExamples.Clear();
        NegativeExamples.Clear();
        _positiveOffset = 0;
        _negativeOffset = 0;
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

    /// <summary>
    /// Holt einen Schwung Vorschläge für die passenden Bilder. Jeder Aufruf greift
    /// weiter hinten im Ordner: Wer ein bestimmtes Thema sucht, findet unter den ersten
    /// Bildern eines gemischten Ordners oft kaum eines, das passt.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanLoadExamples))]
    private Task SuggestPositivesAsync() => SuggestAsync(PositiveExamples, isPositive: true);

    /// <summary>Holt einen Schwung Vorschläge für die Gegenbeispiele.</summary>
    [RelayCommand(CanExecute = nameof(CanLoadExamples))]
    private Task SuggestNegativesAsync() => SuggestAsync(NegativeExamples, isPositive: false);

    /// <summary>
    /// Öffnet den Auswahldialog für eigene passende Bilder. Anders als die Vorschläge
    /// braucht die eigene Auswahl keinen Quellordner – die Bilder dürfen von überall
    /// kommen –, wohl aber einen ruhenden Ablauf.
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsInteractive))]
    private Task PickPositivesAsync() => PickAsync(PositiveExamples);

    /// <summary>Öffnet den Auswahldialog für eigene Gegenbeispiele.</summary>
    [RelayCommand(CanExecute = nameof(IsInteractive))]
    private Task PickNegativesAsync() => PickAsync(NegativeExamples);

    /// <summary>
    /// Nimmt hereingezogene Bilddateien auf einer der beiden Seiten auf.
    /// </summary>
    /// <param name="isPositive"><see langword="true"/> für die passenden Bilder.</param>
    /// <param name="paths">Die Pfade der hereingezogenen Dateien.</param>
    public void AddDroppedImages(bool isPositive, IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        ExampleSetViewModel set = isPositive ? PositiveExamples : NegativeExamples;
        ReportAdded(set, set.AddPaths(paths));
    }

    private async Task PickAsync(ExampleSetViewModel set)
    {
        // Voll ist voll: Erst gar keinen Dialog öffnen, statt den Nutzer auswählen zu
        // lassen und die Auswahl anschließend wortlos zu verwerfen.
        if (set.IsFull)
        {
            _status.Report(_localizer.Format("Examples_AlreadyFull", set.Capacity), StatusSeverity.Warning);
            return;
        }

        IReadOnlyList<string> paths = await _folderPicker.PickImagesAsync(CancellationToken.None).ConfigureAwait(true);
        if (paths.Count == 0)
        {
            return;
        }

        ReportAdded(set, set.AddPaths(paths));
    }

    private async Task SuggestAsync(ExampleSetViewModel set, bool isPositive)
    {
        if (set.IsFull)
        {
            _status.Report(_localizer.Format("Examples_AlreadyFull", set.Capacity), StatusSeverity.Warning);
            return;
        }

        // Begin statt Report: Nur Begin schaltet den Fortschrittsbalken ein. Mit bloßem
        // Text sah das Laden aus einem großen (oder aus der Cloud geholten) Ordner aus,
        // als sei die Anwendung stehengeblieben.
        using CancellationTokenSource cancellation = new();
        _status.Begin(_localizer.Get("Sort_LoadingExamples"), cancellation.Cancel);
        try
        {
            // Nur so viele Bilder einlesen, wie noch Platz haben. Das Ermitteln der
            // Aufnahmedaten öffnet jede Datei einzeln; bei einem Ordner, dessen Bilder
            // erst aus der Cloud geholt werden (iCloud-Fotos unter Windows), zieht jedes
            // Öffnen einen vollständigen Download nach sich.
            int offset = isPositive ? _positiveOffset : _negativeOffset;
            IReadOnlyList<Photo> photos = await _photoSource
                .GetPhotosAsync(SourceFolder, IncludeSubfolders, offset, set.RemainingSlots, cancellation.Token)
                .ConfigureAwait(true);

            // Am Ende des Ordners wieder von vorn: Sonst führte wiederholtes Nachfordern
            // in eine leere Auswahl, aus der nur ein Ordnerwechsel führte.
            if (photos.Count == 0 && offset > 0)
            {
                offset = 0;
                photos = await _photoSource
                    .GetPhotosAsync(SourceFolder, IncludeSubfolders, 0, set.RemainingSlots, cancellation.Token)
                    .ConfigureAwait(true);
            }

            offset += photos.Count;
            if (isPositive)
            {
                _positiveOffset = offset;
            }
            else
            {
                _negativeOffset = offset;
            }

            // Bilder, die auf der anderen Seite schon liegen, gehören nicht in die
            // Vorschläge – sonst stünde dasselbe Foto als passend und als Gegenbeispiel.
            ExampleSetViewModel otherSide = isPositive ? NegativeExamples : PositiveExamples;
            ExampleSetViewModel.AddResult result =
                set.Add(photos.Where(photo => !otherSide.Contains(photo.FullPath)));

            if (result.Added == 0)
            {
                // „Keine Bilder im Ordner" nur, wenn der Ordner wirklich keines mehr
                // hergab. Wurden welche gefunden und bloß übergangen – schon gewählt
                // oder auf der anderen Seite –, muss die Meldung das sagen, sonst sucht
                // die Nutzerin den Fehler im Ordner statt in ihrer Auswahl.
                _status.Finish(
                    photos.Count == 0
                        ? _localizer.Get("Sort_NoImagesInFolder")
                        : _localizer.Get("Examples_AllKnown"),
                    StatusSeverity.Warning);
                return;
            }

            _status.Finish(_localizer.Format("Examples_Added", result.Added, set.RemainingSlots), StatusSeverity.Success);
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

    // Eine Meldung, die den Grund nennt: „nichts übernommen" ohne Erklärung wäre für
    // die Zielnutzerin nicht von einem Fehler zu unterscheiden.
    private void ReportAdded(ExampleSetViewModel set, ExampleSetViewModel.AddResult result)
    {
        if (result.Added > 0)
        {
            _status.Report(
                _localizer.Format("Examples_Added", result.Added, set.RemainingSlots),
                StatusSeverity.Success);
            return;
        }

        string reason = result switch
        {
            { RejectedBecauseFull: > 0 } => _localizer.Format("Examples_AlreadyFull", set.Capacity),
            { Unusable: > 0 } => _localizer.Get("Examples_NoImageFiles"),
            _ => _localizer.Get("Examples_AllKnown"),
        };

        _status.Report(reason, StatusSeverity.Warning);
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
                [.. PositiveExamples.Items.Select(item => new TrainingExample(item.Photo, IsPositive: true)),
                 .. NegativeExamples.Items.Select(item => new TrainingExample(item.Photo, IsPositive: false))];

            // Für jedes Beispiel läuft ein vollständiger Aufruf des Bild-Modells. Ohne
            // Zählstand stünde die Oberfläche minutenlang bei derselben unveränderten
            // Zeile und wäre nicht von einem Absturz zu unterscheiden.
            Progress<TrainingProgress> progress = new(stand => _status.ReportProgress(
                _localizer.Format("Sort_LearningProgress", stand.Processed, stand.Total),
                stand.Total == 0 ? 0 : stand.Processed * 100d / stand.Total));

            CategoryKind kind = IsEventCategory ? CategoryKind.Event : CategoryKind.Topic;
            Category category = await _trainer
                .TrainAsync(CategoryName.Trim(), CategoryDescription.Trim(), kind, examples, progress, _cancellation.Token)
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

    // Beide Seiten melden jede Änderung hierher: Ob gelernt werden kann und ob der
    // Assistent weiterblättern darf, hängt allein an ihrem Inhalt.
    private void OnExampleSetChanged()
    {
        LearnCommand.NotifyCanExecuteChanged();
        Wizard.NotifyStateChanged();
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
        PositiveExamples.Clear();
        NegativeExamples.Clear();
        _positiveOffset = 0;
        _negativeOffset = 0;
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
