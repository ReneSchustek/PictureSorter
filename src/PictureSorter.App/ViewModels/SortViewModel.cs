using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
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
    private readonly IAnalysisJournal _analysisJournal;
    private readonly SortMemoryRecovery _recovery;
    private readonly IPhotoSource _photoSource;
    private readonly ITripDetector _tripDetector;
    private readonly ICategoryTrainer _trainer;
    private readonly ICategoryRepository _categoryRepository;
    private readonly IFolderPicker _folderPicker;
    private readonly IConfirmationService _confirmationService;
    private readonly StatusBarViewModel _status;
    private readonly SortingOptions _options;
    private readonly ILocalizer _localizer;
    private readonly ILogger<SortViewModel> _logger;

    private readonly ExampleGatheringViewModel _gathering;

    private Category? _category;
    private CancellationTokenSource? _cancellation;

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
    /// Erster Tag des Zeitraums, auf den die Analyse beschränkt wird; leer für „ohne
    /// Anfang". Fotos außerhalb kommen der KI gar nicht erst vor — bei einem Ordner mit
    /// tausend Bildern verkürzt das den Lauf um ein Vielfaches.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ClearDateRangeCommand))]
    public partial DateTimeOffset? DateFrom { get; set; }

    /// <summary>
    /// Letzter Tag des Zeitraums (eingeschlossen); leer für „ohne Ende".
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ClearDateRangeCommand))]
    public partial DateTimeOffset? DateTo { get; set; }

    /// <summary>
    /// <see langword="true"/>, wenn allein nach dem Aufnahmedatum sortiert wird — ohne
    /// Anlernen und ohne einen einzigen KI-Aufruf.
    ///
    /// Der Weg für „alles aus diesem Urlaub in einen Ordner": Dort entscheidet der
    /// Zeitraum, nicht das Motiv. Beispiele zu sammeln und jedes Foto von der KI bewerten
    /// zu lassen wäre reine Wartezeit ohne besseres Ergebnis.
    /// </summary>
    [ObservableProperty]
    public partial bool SortByDateOnly { get; set; }

    /// <summary>
    /// Erkannte Zeiträume, in denen sich die Aufnahmen ballen — im Alltag Urlaube,
    /// Feiern, Ausflüge. Ein Klick übernimmt einen davon als Zeitraum.
    /// </summary>
    public ObservableCollection<TripSuggestionViewModel> TripSuggestions { get; } = [];

    /// <summary>
    /// Hinweis zur Urlaubssuche (etwa „nichts gefunden"); leer, solange nichts zu sagen ist.
    /// </summary>
    [ObservableProperty]
    public partial string TripHint { get; set; }

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
    /// <param name="analysisJournal">Protokoll der Analyseläufe (Fortsetzen und Zurückholen).</param>
    /// <param name="recovery">Holt frühere Vorschläge aus dem Sortier-Gedächtnis zurück.</param>
    /// <param name="photoSource">Quelle der Fotos (Beispiele und Urlaubssuche).</param>
    /// <param name="tripDetector">Erkennt Zeiträume, in denen sich Aufnahmen ballen.</param>
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
        IAnalysisJournal analysisJournal,
        SortMemoryRecovery recovery,
        IPhotoSource photoSource,
        ITripDetector tripDetector,
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
        ArgumentNullException.ThrowIfNull(analysisJournal);
        ArgumentNullException.ThrowIfNull(recovery);
        ArgumentNullException.ThrowIfNull(photoSource);
        ArgumentNullException.ThrowIfNull(tripDetector);
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
        _analysisJournal = analysisJournal;
        _recovery = recovery;
        _photoSource = photoSource;
        _tripDetector = tripDetector;
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

        // Die Vorschau kennt den Ablauf nur über den Delegaten – wie der Assistent.
        Proposals = new ProposalListViewModel(localizer, () => IsInteractive, OnProposalSelectionChanged);

        // Die Beschaffung der Beispiele erfährt Ordner und Unterordner-Wahl ebenfalls
        // über Delegaten; die Eingabe bleibt damit an genau einer Stelle.
        _gathering = new ExampleGatheringViewModel(
            photoSource,
            folderPicker,
            status,
            localizer,
            logger,
            () => SourceFolder,
            () => IncludeSubfolders);

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

        // Das Angebot kennt den Ablauf nur über diese Rückrufe (SRP), wie der Assistent.
        Resume = new AnalysisResumeViewModel(
            analysisJournal,
            localizer,
            logger,
            () => IsInteractive,
            RunResumeAsync,
            RecoverFromMemoryGuardedAsync,
            ConfirmDiscardAsync);

        State = SortState.Idle;
        SourceFolder = string.Empty;
        CategoryName = string.Empty;
        CategoryDescription = string.Empty;
        UndoSummary = string.Empty;
        TripHint = string.Empty;
    }

    /// <summary>
    /// Navigation und Darstellung des Schritt-für-Schritt-Assistenten.
    /// </summary>
    public SortWizardViewModel Wizard { get; }

    /// <summary>
    /// Das Angebot, einen protokollierten Analyselauf fortzusetzen, sein Ergebnis
    /// zurückzuholen oder ihn zu verwerfen.
    /// </summary>
    public AnalysisResumeViewModel Resume { get; }

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
    /// Die erzeugten Sortiervorschläge (Vorschau) samt Auswahl.
    /// </summary>
    public ProposalListViewModel Proposals { get; }

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
    /// Nach Datum sortiert werden kann, wenn Ordner, Zielordnername und ein brauchbarer
    /// Zeitraum vorliegen.
    ///
    /// Der Zeitraum ist hier Pflicht, anders als bei der Analyse: Ohne ihn gäbe es kein
    /// einziges Kriterium, und jedes Foto des Ordners stünde als Vorschlag zum
    /// Verschieben bereit.
    /// </summary>
    public bool CanSortByDate =>
        IsInteractive
        && !string.IsNullOrWhiteSpace(SourceFolder)
        && !string.IsNullOrWhiteSpace(CategoryName)
        && HasDateRange
        && !IsDateRangeReversed;

    // Bewusst NICHT über SelectedRange geprüft: Das verwirft einen verdrehten Zeitraum
    // bereits und liefert „ohne Grenze" zurück. Für die Analyse ist das richtig — dort
    // entscheidet die Kategorie, und ohne Zeitraum werden eben alle Fotos geprüft. Hier
    // wäre die Prüfung damit wirkungslos gewesen: Wer sich beim Tippen vertut, bekäme
    // einen freigegebenen Knopf und danach wortlos „kein Foto gefunden".
    private bool IsDateRangeReversed => new DateRange(ToDay(DateFrom), ToDay(DateTo)).IsReversed;

    /// <summary>
    /// Anwenden ist möglich, wenn mindestens ein Vorschlag ausgewählt ist.
    /// </summary>
    public bool CanApply => State is SortState.Preview && Proposals.SelectedCount > 0;

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
        OnPropertyChanged(nameof(CanRecoverFromMemory));

        // Der Anhalten-Knopf steht auf der Seite und nicht nur in der Statusleiste; ohne
        // diese Meldung erschiene er erst beim nächsten Anlass, sich neu zu zeichnen.
        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(CanSuggestTrips));
        OnPropertyChanged(nameof(CanSortByDate));
        SuggestTripsCommand.NotifyCanExecuteChanged();
        SortByDateCommand.NotifyCanExecuteChanged();
        UndoCommand.NotifyCanExecuteChanged();
        Resume.NotifyStateChanged();
        Wizard.NotifyStateChanged();
        Proposals.NotifyStateChanged();
    }

    // Inhalt oder Auswahl der Vorschau haben sich geändert: Davon hängt ab, ob
    // „Sortieren" bedienbar ist, und der Assistent zeigt es in seinem letzten Schritt.
    private void OnProposalSelectionChanged()
    {
        ApplyCommand.NotifyCanExecuteChanged();
        Wizard.NotifyStateChanged();
    }

    partial void OnHasUndoableRunChanged(bool value) => OnPropertyChanged(nameof(CanUndo));

    partial void OnSourceFolderChanged(string value)
    {
        // Bei Ordnerwechsel die alten Beispiele verwerfen: Sie stammen aus dem vorigen
        // Ordner und würden dort weiterwirken, wo sie niemand mehr erwartet. Der
        // Startpunkt beginnt wieder vorn – sonst überspränge der neue Ordner ohne
        // Grund seine ersten Bilder.
        _gathering.ResetOffsets();
        if (!PositiveExamples.IsEmpty || !NegativeExamples.IsEmpty)
        {
            PositiveExamples.Clear();
            NegativeExamples.Clear();
            LearnCommand.NotifyCanExecuteChanged();
        }

        // Die Vorschläge stammen aus dem vorigen Ordner. Blieben sie stehen, führten sie
        // im neuen zu einem Zeitraum, in dem dort womöglich kein einziges Foto liegt.
        TripSuggestions.Clear();
        TripHint = string.Empty;

        OnPropertyChanged(nameof(CanSuggestTrips));
        OnPropertyChanged(nameof(CanSortByDate));
        OnPropertyChanged(nameof(CanRecoverFromMemory));
        SuggestTripsCommand.NotifyCanExecuteChanged();
        SortByDateCommand.NotifyCanExecuteChanged();
        Resume.NotifyStateChanged();
        Wizard.NotifyStateChanged();
    }

    partial void OnCategoryNameChanged(string value)
    {
        // Der Name ist beim Sortieren nach Datum der Name des Zielordners und damit
        // Vorbedingung des Laufs. Er ist zugleich der Schlüssel, unter dem im Gedächtnis
        // nach früheren Vorschlägen gesucht wird.
        OnPropertyChanged(nameof(CanSortByDate));
        OnPropertyChanged(nameof(CanRecoverFromMemory));
        SortByDateCommand.NotifyCanExecuteChanged();
        Resume.NotifyStateChanged();
        Wizard.NotifyStateChanged();
    }

    // ── Anbindung des Assistenten (Delegaten) ──────────────────────────────────

    // Vorbedingung des Aktionsknopfs je Schritt.
    private bool CanRunWizardStep(int step) => step switch
    {
        0 => IsInteractive && !string.IsNullOrWhiteSpace(SourceFolder),
        1 => IsInteractive && !string.IsNullOrWhiteSpace(CategoryName),
        2 => IsInteractive && PositiveExamples.Items.Count > 0,
        3 => CanLearn,
        4 => SortByDateOnly ? CanSortByDate : CanAnalyze,
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
                if (SortByDateOnly)
                {
                    await SortByDateAsync().ConfigureAwait(true);
                }
                else
                {
                    await AnalyzeAsync().ConfigureAwait(true);
                }

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
        DateFrom = null;
        DateTo = null;
        TripSuggestions.Clear();
        TripHint = string.Empty;
        PositiveExamples.Clear();
        NegativeExamples.Clear();
        _gathering.ResetOffsets();
        Proposals.Clear();
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
    private Task SuggestPositivesAsync() => _gathering.SuggestAsync(PositiveExamples, NegativeExamples, isPositive: true);

    /// <summary>Holt einen Schwung Vorschläge für die Gegenbeispiele.</summary>
    [RelayCommand(CanExecute = nameof(CanLoadExamples))]
    private Task SuggestNegativesAsync() => _gathering.SuggestAsync(NegativeExamples, PositiveExamples, isPositive: false);

    /// <summary>
    /// Öffnet den Auswahldialog für eigene passende Bilder. Anders als die Vorschläge
    /// braucht die eigene Auswahl keinen Quellordner – die Bilder dürfen von überall
    /// kommen –, wohl aber einen ruhenden Ablauf.
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsInteractive))]
    private Task PickPositivesAsync() => _gathering.PickAsync(PositiveExamples);

    /// <summary>Öffnet den Auswahldialog für eigene Gegenbeispiele.</summary>
    [RelayCommand(CanExecute = nameof(IsInteractive))]
    private Task PickNegativesAsync() => _gathering.PickAsync(NegativeExamples);

    /// <summary>
    /// Nimmt hereingezogene Bilddateien auf einer der beiden Seiten auf.
    /// </summary>
    /// <param name="isPositive"><see langword="true"/> für die passenden Bilder.</param>
    /// <param name="paths">Die Pfade der hereingezogenen Dateien.</param>
    public void AddDroppedImages(bool isPositive, IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        ExampleSetViewModel set = isPositive ? PositiveExamples : NegativeExamples;
        _gathering.AddDropped(set, paths);
    }

    [RelayCommand(CanExecute = nameof(CanLearn))]
    private async Task LearnAsync()
    {
        using IDisposable? logScope = _logger.BeginScope("Lernen {CorrelationId}", NewCorrelationId());
        State = SortState.Learning;
        _status.Begin(_localizer.Get("Sort_Learning"), Cancel);
        _cancellation = new CancellationTokenSource();

        await RunGuardedAsync(
            LearnCoreAsync,
            SortState.Idle,
            "Sort_LearnCanceled",
            "Sort_LearnFailed",
            SortViewModelLog.LearnFailed).ConfigureAwait(true);
    }

    private async Task LearnCoreAsync()
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
        string name = CategoryName.Trim();
        string description = CategoryDescription.Trim();
        CancellationToken token = _cancellation!.Token;

        // Jedes Beispiel ist ein vollständiger Aufruf des Bild-Modells. Ohne den
        // Wechsel auf einen Hintergrund-Thread bliebe die Oberfläche währenddessen
        // stehen und der Stopp-Knopf ohne Wirkung.
        Category category = await Task.Run(
            () => _trainer.TrainAsync(name, description, kind, examples, progress, token),
            token).ConfigureAwait(true);

        await PersistCategoryAsync(category, token).ConfigureAwait(true);
        SetActiveCategory(category);
        State = SortState.Idle;
        _status.Finish(_localizer.Format("Sort_CategoryLearned", category.Name), StatusSeverity.Success);
    }

    /// <summary>
    /// Der eingestellte Zeitraum. Aus den beiden Datumsfeldern; ein verdrehter Zeitraum
    /// (Anfang nach Ende) wird verworfen, statt wortlos nichts zu finden.
    /// </summary>
    private DateRange SelectedRange
    {
        get
        {
            DateRange bereich = new(ToDay(DateFrom), ToDay(DateTo));
            return bereich.IsReversed ? DateRange.Unbounded : bereich;
        }
    }

    private static DateOnly? ToDay(DateTimeOffset? wert) =>
        wert is { } vorhanden ? DateOnly.FromDateTime(vorhanden.LocalDateTime) : null;

    /// <summary>
    /// Nimmt die Beschränkung auf einen Zeitraum wieder zurück.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasDateRange))]
    private void ClearDateRange()
    {
        DateFrom = null;
        DateTo = null;
        _status.Report(_localizer.Get("Sort_DateRangeCleared"));
    }

    /// <summary>
    /// <see langword="true"/>, wenn mindestens eine Grenze gesetzt ist.
    /// </summary>
    public bool HasDateRange => DateFrom is not null || DateTo is not null;

    partial void OnDateFromChanged(DateTimeOffset? value) => NotifyDateRangeChanged();

    partial void OnDateToChanged(DateTimeOffset? value) => NotifyDateRangeChanged();

    // Beim Sortieren nach Datum ist der Zeitraum das einzige Kriterium: Ohne ihn bleibt
    // der Aktionsknopf gesperrt, mit ihm wird er frei. Der Assistent muss das sofort
    // mitbekommen – sonst tippt die Nutzerin ein Datum ein und der Knopf bleibt grau.
    private void NotifyDateRangeChanged()
    {
        OnPropertyChanged(nameof(HasDateRange));
        OnPropertyChanged(nameof(CanSortByDate));
        SortByDateCommand.NotifyCanExecuteChanged();
        Wizard.NotifyStateChanged();
    }

    partial void OnSortByDateOnlyChanged(bool value)
    {
        // Der Assistent blendet daraufhin die beiden Schritte zur Beispielauswahl aus.
        Wizard.SkipsExampleSteps = value;
        OnPropertyChanged(nameof(CanSortByDate));
        SortByDateCommand.NotifyCanExecuteChanged();
        Wizard.NotifyStateChanged();
    }

    /// <summary>
    /// Durchsucht den Ordner nach Zeiträumen, in denen sich die Aufnahmen ballen, und
    /// bietet sie zur Auswahl an.
    ///
    /// Dafür müssen die Aufnahmedaten aller Bilder gelesen werden — dieselbe Arbeit wie
    /// der Ladeteil einer Analyse, nur ohne KI danach. Der Fortschrittsbalken zeigt sie an.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSuggestTrips))]
    private async Task SuggestTripsAsync()
    {
        using CancellationTokenSource abbruch = new();
        _status.Begin(_localizer.Get("Sort_SearchingTrips"), abbruch.Cancel);
        TripSuggestions.Clear();
        TripHint = string.Empty;

        try
        {
            Progress<PhotoScanProgress> fortschritt = new(stand => _status.ReportProgress(
                _localizer.Format("Common_GatherProgress", stand.Processed, stand.Total),
                stand.Total == 0 ? 0 : stand.Processed * 100d / stand.Total));

            IReadOnlyList<Photo> photos = await _photoSource
                .GetPhotosAsync(SourceFolder, IncludeSubfolders, skip: 0, maxCount: null, fortschritt, abbruch.Token)
                .ConfigureAwait(true);

            foreach (TripSuggestion vorschlag in _tripDetector.Detect(photos))
            {
                TripSuggestions.Add(new TripSuggestionViewModel(vorschlag, _localizer));
            }

            if (TripSuggestions.Count == 0)
            {
                // Ohne Erklärung stünde die Nutzerin vor einer leeren Liste und wüsste
                // nicht, ob die Suche gelaufen ist.
                TripHint = _localizer.Get("Sort_NoTripsFound");
                _status.Finish(_localizer.Get("Sort_NoTripsFound"), StatusSeverity.Warning);
                return;
            }

            _status.Finish(
                _localizer.Format("Sort_TripsFound", TripSuggestions.Count),
                StatusSeverity.Success);
        }
        catch (OperationCanceledException)
        {
            _status.Finish(_localizer.Get("Sort_TripSearchCanceled"), StatusSeverity.Warning);
        }
        catch (DirectoryNotFoundException ex)
        {
            SortViewModelLog.TripSearchFailed(_logger, ex);
            _status.Finish(_localizer.Get("Sort_FolderNotFound"), StatusSeverity.Error);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SortViewModelLog.TripSearchFailed(_logger, ex);
            _status.Finish(_localizer.Get("Sort_FolderUnreadable"), StatusSeverity.Error);
        }
    }

    /// <summary>
    /// Urlaube lassen sich suchen, wenn ein Ordner gewählt ist und nichts läuft.
    /// </summary>
    public bool CanSuggestTrips => IsInteractive && !string.IsNullOrWhiteSpace(SourceFolder);

    /// <summary>
    /// Übernimmt einen Vorschlag als Zeitraum.
    /// </summary>
    /// <param name="suggestion">Der gewählte Vorschlag.</param>
    [RelayCommand]
    private void UseTripSuggestion(TripSuggestionViewModel? suggestion)
    {
        if (suggestion is null)
        {
            return;
        }

        DateFrom = ToOffset(suggestion.Range.From);
        DateTo = ToOffset(suggestion.Range.To);
        _status.Report(_localizer.Format("Sort_DateRangeTaken", suggestion.Label), StatusSeverity.Success);
    }

    private static DateTimeOffset? ToOffset(DateOnly? tag) =>
        tag is { } vorhanden ? new DateTimeOffset(vorhanden.ToDateTime(TimeOnly.MinValue)) : null;

    [RelayCommand(CanExecute = nameof(CanAnalyze))]
    private async Task AnalyzeAsync()
    {
        using IDisposable? logScope = _logger.BeginScope("Analyse {CorrelationId}", NewCorrelationId());
        State = SortState.Analyzing;
        _status.Begin(_localizer.Get("Sort_Analyzing"), Cancel);
        _analyzeProgress = default;
        _analyzeThrottle.Reset();
        _cancellation = new CancellationTokenSource();

        await RunGuardedAsync(
            AnalyzeCoreAsync,
            SortState.Idle,
            "Sort_AnalyzePaused",
            "Sort_AnalyzeFailed",
            SortViewModelLog.AnalyzeFailed).ConfigureAwait(true);
    }

    private async Task AnalyzeCoreAsync()
    {
        Progress<SortProgress> progress = new(OnAnalyzeProgress);

        // Task.Run ist hier kein Zierrat: Der Aufruf läuft bis zum ersten echten
        // Wartepunkt auf dem Oberflächen-Thread, und dazu gehört das rekursive
        // Durchsuchen des Ordners. Bei einem großen oder in der Cloud liegenden
        // Ordner steht die Anwendung so lange still — der Stopp-Knopf lässt sich
        // zwar drücken, wird aber erst bearbeitet, wenn das Durchsuchen fertig ist.
        // Die Werte werden vorher gelesen, damit im Hintergrund keine
        // Oberflächen-Eigenschaften mehr angefasst werden.
        string folder = SourceFolder;
        bool withSubfolders = IncludeSubfolders;
        DateRange range = SelectedRange;
        Category category = _category!;
        CancellationToken token = _cancellation!.Token;

        IReadOnlyList<SortProposal> proposals = await Task.Run(
            () => _sorter.CreateProposalsAsync(
                folder, category, withSubfolders, range, progress, token),
            token).ConfigureAwait(true);

        ShowProposals(proposals, "Sort_NoMatchingPhotos");
    }

    // Der Zwilling von AnalyzeAsync ohne KI: Statt jedes Foto zu bewerten, entscheidet
    // allein das Aufnahmedatum. Bewusst ein eigener Befehl statt eines Schalters in
    // AnalyzeAsync — die beiden Wege teilen sich zwar den Ablauf, aber keine einzige
    // Vorbedingung, und ein „if" mitten im Analyse-Pfad hätte beide unklar gemacht.
    [RelayCommand(CanExecute = nameof(CanSortByDate))]
    private async Task SortByDateAsync()
    {
        using IDisposable? logScope = _logger.BeginScope("Datums-Sortierung {CorrelationId}", NewCorrelationId());
        State = SortState.Analyzing;
        _status.Begin(_localizer.Get("Sort_DateScanning"), Cancel);
        _analyzeProgress = default;
        _analyzeThrottle.Reset();
        _cancellation = new CancellationTokenSource();

        await RunGuardedAsync(
            SortByDateCoreAsync,
            SortState.Idle,
            "Sort_AnalyzePaused",
            "Sort_AnalyzeFailed",
            SortViewModelLog.AnalyzeFailed).ConfigureAwait(true);
    }

    private async Task SortByDateCoreAsync()
    {
        Progress<SortProgress> progress = new(OnAnalyzeProgress);

        // Wie bei der Analyse: erst herunter vom Oberflächen-Thread, sonst blockiert
        // das Durchsuchen des Ordners die Bedienung samt Stopp-Knopf.
        string folder = SourceFolder;
        string targetFolder = CategoryName.Trim();
        bool withSubfolders = IncludeSubfolders;
        DateRange range = SelectedRange;
        CancellationToken token = _cancellation!.Token;

        IReadOnlyList<SortProposal> proposals = await Task.Run(
            () => _sorter.CreateDateProposalsAsync(
                folder, targetFolder, withSubfolders, range, progress, token),
            token).ConfigureAwait(true);

        ShowProposals(proposals, "Sort_NoPhotosInRange");
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
            await RunGuardedAsync(
                ApplyCoreAsync,
                SortState.Preview,
                "Sort_ApplyCanceled",
                "Sort_ApplyFailed",
                SortViewModelLog.ApplyFailed).ConfigureAwait(true);
        }
        finally
        {
            // Erst jetzt steht der Lauf im Protokoll – der Hinweis „rückgängig machen"
            // erscheint unmittelbar nach dem Sortieren.
            await RefreshUndoStateAsync().ConfigureAwait(true);
        }
    }

    private async Task ApplyCoreAsync()
    {
        IReadOnlyList<SortProposal> selected = Proposals.Selected;
        IReadOnlyList<SortProposal> rejected = Proposals.Rejected;

        FileOperationMode operation = CopyInsteadOfMove
            ? FileOperationMode.Copy
            : FileOperationMode.Move;

        // Auch das Verschieben selbst gehört nicht auf den Oberflächen-Thread: Es
        // fasst jede Datei einzeln an und schreibt dabei ins Protokoll.
        CancellationToken token = _cancellation!.Token;
        int moved = await Task.Run(
            () => _sorter.ApplyProposalsAsync(selected, operation, dryRun: false, token),
            token).ConfigureAwait(true);

        // Abgewählte Vorschläge dauerhaft merken, damit sie nicht erneut erscheinen.
        if (rejected.Count > 0)
        {
            await _sorter.IgnoreProposalsAsync(rejected, token).ConfigureAwait(true);
        }

        Proposals.Clear();
        State = SortState.Completed;
        _status.Finish(
            rejected.Count == 0
                ? _localizer.Format("Sort_FilesSorted", moved)
                : _localizer.Format("Sort_FilesSortedWithRejected", moved, rejected.Count),
            StatusSeverity.Success);
    }

    /// <summary>
    /// Hält den laufenden Vorgang an.
    ///
    /// Bei einer Analyse ist das ausdrücklich ein Anhalten und kein Verwerfen: Jedes
    /// bereits gefällte Urteil steht im Protokoll, der Lauf setzt später genau dort
    /// wieder an. Wer einen Ordner mit tausenden Bildern sortiert, muss den Rechner
    /// dafür nicht tagelang durchlaufen lassen.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        bool analyzing = State is SortState.Analyzing;
        _cancellation?.Cancel();
        _status.Report(_localizer.Get(analyzing ? "Sort_Pausing" : "Common_CancelRequested"));
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

    // Der Ablauf eines fortgesetzten Laufs. Das Angebot dazu — Erkennen, Beschriften,
    // Verwerfen — liegt in AnalysisResumeViewModel und ruft hierher zurück.
    private async Task RunResumeAsync(AnalysisRun run)
    {
        using IDisposable? logScope = _logger.BeginScope("Fortsetzen {CorrelationId}", NewCorrelationId());
        State = SortState.Analyzing;
        _status.Begin(_localizer.Get("Sort_Resuming"), Cancel);
        _analyzeProgress = default;
        _analyzeThrottle.Reset();
        _cancellation = new CancellationTokenSource();

        await RunGuardedAsync(
            () => ResumeCoreAsync(run),
            SortState.Idle,
            "Sort_AnalyzePaused",
            "Sort_AnalyzeFailed",
            SortViewModelLog.AnalyzeFailed).ConfigureAwait(true);
    }

    private async Task ResumeCoreAsync(AnalysisRun run)
    {
        // Die Angaben des Laufs gelten, nicht die der Oberfläche: Ein fortgesetzter Lauf
        // muss dieselbe Frage beantworten wie der unterbrochene. Sie werden übernommen,
        // damit die Nutzerin auch sieht, worum es geht.
        SourceFolder = run.SourceFolder;
        IncludeSubfolders = run.IncludeSubfolders;
        CategoryName = run.CategoryName;
        SortByDateOnly = run.ByDateOnly;
        DateFrom = ToOffset(run.RangeFrom);
        DateTo = ToOffset(run.RangeTo);

        Category? category = run.ByDateOnly
            ? null
            : await FindCategoryAsync(run.CategoryName).ConfigureAwait(true);

        if (!run.ByDateOnly && category is null)
        {
            // Die Kategorie ist weg — ohne ihre Beispiele wäre jedes Urteil geraten.
            State = SortState.Idle;
            _status.Finish(
                _localizer.Format("Sort_ResumeCategoryMissing", run.CategoryName),
                StatusSeverity.Warning);
            return;
        }

        if (category is not null)
        {
            SetActiveCategory(category);
        }

        Progress<SortProgress> progress = new(OnAnalyzeProgress);
        CancellationToken token = _cancellation!.Token;

        IReadOnlyList<SortProposal> proposals = await Task.Run(
            () => _sorter.ResumeAsync(run, category, progress, token),
            token).ConfigureAwait(true);

        ShowProposals(proposals, "Sort_NoMatchingPhotos");

        // Direkt in den Schritt mit der Vorschau: Die Nutzerin hat um das Ergebnis
        // gebeten, nicht um den Weg dorthin.
        Wizard.MaxReachedStep = SortWizardViewModel.StepCount - 1;
        Wizard.GoToStep(SortWizardViewModel.StepCount - 1);
    }

    /// <summary>
    /// Holt die Vorschläge einer früheren Analyse aus dem Sortier-Gedächtnis zurück —
    /// ohne einen einzigen KI-Aufruf.
    ///
    /// Der Weg für Läufe, zu denen es kein Protokoll gibt: Das Gedächtnis wurde schon
    /// immer nach jedem Foto beschrieben, nur genutzt wurde bisher allein die Ablehnung.
    /// Bei einem Ordner, dessen Bewertung Tage dauert, ist das der Unterschied zwischen
    /// „nichts verloren" und „alles noch einmal".
    /// </summary>
    private async Task RunRecoverFromMemoryAsync()
    {
        using IDisposable? logScope = _logger.BeginScope("Wiederherstellen {CorrelationId}", NewCorrelationId());
        State = SortState.Analyzing;
        _status.Begin(_localizer.Get("Sort_Recovering"), Cancel);
        _cancellation = new CancellationTokenSource();

        await RunGuardedAsync(
            RecoverCoreAsync,
            SortState.Idle,
            "Sort_AnalyzeCanceled",
            "Sort_AnalyzeFailed",
            SortViewModelLog.AnalyzeFailed).ConfigureAwait(true);
    }

    private async Task RecoverCoreAsync()
    {
        string folder = SourceFolder;
        string category = CategoryName.Trim();
        CategoryKind kind = IsEventCategory ? CategoryKind.Event : CategoryKind.Topic;
        CancellationToken token = _cancellation!.Token;

        IReadOnlyList<SortProposal> proposals = await Task.Run(
            () => _recovery.RecoverAsync(folder, category, kind, token),
            token).ConfigureAwait(true);

        if (proposals.Count == 0)
        {
            State = SortState.Idle;
            _status.Finish(_localizer.Format("Sort_NothingRemembered", category), StatusSeverity.Warning);
            return;
        }

        ShowProposals(proposals, "Sort_NothingRemembered");
        Wizard.MaxReachedStep = SortWizardViewModel.StepCount - 1;
        Wizard.GoToStep(SortWizardViewModel.StepCount - 1);
    }

    /// <summary>
    /// Wiederherstellen ist möglich, sobald Ordner und Gruppenname feststehen — angelernt
    /// sein muss dafür nichts: Es wird nichts berechnet, nur nachgeschlagen.
    /// </summary>
    public bool CanRecoverFromMemory =>
        IsInteractive
        && !string.IsNullOrWhiteSpace(SourceFolder)
        && !string.IsNullOrWhiteSpace(CategoryName);

    // Das Angebot kennt Ordner und Gruppenname nicht; es fragt hier nach, bevor es den
    // Knopf freigibt.
    private Task RecoverFromMemoryGuardedAsync() =>
        CanRecoverFromMemory ? RunRecoverFromMemoryAsync() : Task.CompletedTask;

    // Die Rückfrage vor dem Verwerfen: Sie braucht den Bestätigungsdienst und bleibt
    // deshalb hier. Das Angebot fragt sie über einen Delegaten ab.
    private async Task<bool> ConfirmDiscardAsync(AnalysisRun run)
    {
        bool confirmed = await _confirmationService.ConfirmAsync(
            _localizer.Get("Sort_DiscardRunTitle"),
            _localizer.Format("Sort_DiscardRunMessage", run.CategoryName, run.DecidedPhotos),
            _localizer.Get("Sort_DiscardRunPrimary"),
            _localizer.Get("Common_Cancel")).ConfigureAwait(true);

        if (confirmed)
        {
            _status.Report(_localizer.Get("Sort_RunDiscarded"));
        }

        return confirmed;
    }

    private async Task<Category?> FindCategoryAsync(string name)
    {
        IReadOnlyList<Category> categories =
            await _categoryRepository.LoadAllAsync(CancellationToken.None).ConfigureAwait(true);
        return categories.FirstOrDefault(item =>
            string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
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
            // Ein Abbruch führt hier zurück in den Ruhezustand: Bereits zurückgeholte
            // Fotos bleiben zurückgeholt, der Lauf gilt weiter als offen und kann erneut
            // zurückgenommen werden.
            await RunGuardedAsync(
                UndoCoreAsync,
                SortState.Idle,
                "Sort_UndoCanceled",
                "Sort_UndoFailed",
                SortViewModelLog.UndoFailed).ConfigureAwait(true);
        }
        finally
        {
            await RefreshUndoStateAsync().ConfigureAwait(true);
        }
    }

    private async Task UndoCoreAsync()
    {
        UndoResult? result = await _undo.UndoLastRunAsync(_cancellation!.Token).ConfigureAwait(true);
        State = SortState.Idle;

        // Nicht zurückgeholte Fotos werden ausgewiesen statt verschwiegen: Sie
        // liegen weiterhin im Kategorie-Ordner, und die Nutzerin muss das erfahren.
        _status.Finish(
            result is null || result.Skipped == 0
                ? _localizer.Format("Sort_UndoDone", result?.Restored ?? 0)
                : _localizer.Format("Sort_UndoDoneWithSkipped", result.Restored, result.Skipped),
            result is { Skipped: > 0 } ? StatusSeverity.Warning : StatusSeverity.Success);
    }

    // ── Hilfsfunktionen ────────────────────────────────────────────────────────

    /// <summary>
    /// Führt einen langen Vorgang aus und stellt sicher, dass die Oberfläche danach in
    /// jedem Fall wieder bedienbar ist.
    ///
    /// Ein Lauf über tausende Fotos dauert Stunden bis Tage. Er darf deshalb unter keinen
    /// Umständen stumm stehenbleiben: Wurden nur der Abbruch und zwei Datei-Ausnahmen
    /// gefangen, ließ jede andere den Ablaufzustand auf „läuft" stehen — jeder Knopf
    /// gesperrt, die Statusleiste beim letzten Zählstand hängend, der Stopp-Knopf ohne
    /// Wirkung, weil das Abbruch-Objekt bereits verworfen war. Von einem Stillstand war
    /// das nicht zu unterscheiden, und es half nur noch das Beenden des Programms.
    /// </summary>
    /// <param name="operation">Der auszuführende Vorgang.</param>
    /// <param name="canceledState">Zustand nach einem Abbruch durch die Nutzerin.</param>
    /// <param name="canceledMessageKey">Textschlüssel der Abbruchmeldung.</param>
    /// <param name="failedMessageKey">Textschlüssel der allgemeinen Fehlermeldung.</param>
    /// <param name="logFailure">Schreibt den Fehler ins Protokoll.</param>
    [SuppressMessage(
        "Design",
        "CA1031:Keine allgemeinen Ausnahmetypen abfangen",
        Justification = "Genau das ist der Zweck: Es gibt keine Ausnahme, die die Oberfläche unbedienbar zurücklassen darf. Sie wird nicht geschluckt, sondern protokolliert und der Nutzerin gemeldet.")]
    private async Task RunGuardedAsync(
        Func<Task> operation,
        SortState canceledState,
        string canceledMessageKey,
        string failedMessageKey,
        Action<ILogger, Exception> logFailure)
    {
        try
        {
            await operation().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            State = canceledState;
            _status.Finish(_localizer.Get(canceledMessageKey), StatusSeverity.Warning);
        }
        catch (Exception ex)
        {
            logFailure(_logger, ex);
            State = SortState.Error;
            _status.Finish(DescribeFailure(ex, failedMessageKey), StatusSeverity.Error);
        }
        finally
        {
            DisposeCancellation();
        }
    }

    // Bekannte Störungen bekommen den Text, der die Nutzerin weiterbringt; alles Übrige
    // die allgemeine Meldung des Vorgangs. Vorher stand bei einem gelöschten Ordner
    // dasselbe da wie bei einem Fehler in der KI — „Analyse fehlgeschlagen", und die
    // Nutzerin wusste weder, was los war, noch, was sie tun könnte.
    private string DescribeFailure(Exception exception, string fallbackKey) => exception switch
    {
        AiUnavailableException => _localizer.Get("Sort_AiUnavailable"),
        DirectoryNotFoundException => _localizer.Get("Sort_FolderNotFound"),
        UnauthorizedAccessException => _localizer.Get("Sort_FolderUnreadable"),
        _ => _localizer.Get(fallbackKey),
    };

    // Übernimmt ein Ergebnis in die Vorschau. Gemeinsam für Analyse, Datumssuche und das
    // Zurückholen eines protokollierten Laufs — drei Wege, ein Ergebnis.
    private void ShowProposals(IReadOnlyList<SortProposal> proposals, string emptyMessageKey)
    {
        Proposals.Replace(proposals);
        State = SortState.Preview;
        _status.Finish(
            proposals.Count == 0
                ? _localizer.Get(emptyMessageKey)
                : _localizer.Format("Sort_ProposalsFound", proposals.Count),
            proposals.Count == 0 ? StatusSeverity.Warning : StatusSeverity.Success);
    }

    // Laden und Bewerten laufen gleichzeitig; jeder Abschnitt meldet für sich. Die beiden
    // Stände werden deshalb hier gehalten und bei jeder Meldung gemeinsam an die
    // Statusleiste gegeben – sonst setzte die eine Meldung den Balken der anderen zurück.
    private ScanProgressPair _analyzeProgress;

    // Nicht jede Meldung wird gezeichnet: Bei tausend Bildern kämen zweitausend an, und
    // seit beide Abschnitte gleichzeitig laufen, kommen sie auch gleichzeitig. Ungefiltert
    // bringt das den Oberflächen-Faden zum Erliegen.
    private readonly ProgressThrottle _analyzeThrottle = new(TimeSpan.FromMilliseconds(100));

    private void OnAnalyzeProgress(SortProgress progress)
    {
        // Der Stand wird immer mitgeschrieben, auch wenn er nicht gezeichnet wird: Sonst
        // zeigte die nächste durchgelassene Meldung einen veralteten Wert des jeweils
        // anderen Abschnitts.
        _analyzeProgress = _analyzeProgress.With(
            progress.Phase, progress.Processed, progress.Total, progress.IsFinal);

        // Die Abschlussmeldung kommt immer durch — und sie wird ausdrücklich als solche
        // gemeldet, nicht daraus erschlossen, dass der Zählstand die Gesamtzahl erreicht.
        // Fällt beim Einlesen eine Datei aus, träfe diese Bedingung nie zu, und die
        // Anzeige bliebe kurz vor dem Ende stehen — von einem Stillstand nicht zu
        // unterscheiden.
        if (!_analyzeThrottle.ShouldReport(progress.IsFinal))
        {
            return;
        }

        // Der Text nennt die Bewertung, sobald sie läuft: Sie bestimmt die Gesamtdauer.
        // Nur solange noch kein Bild bewertet ist, steht dort das Laden – sonst stünde in
        // den ersten Sekunden „Bild 0 von 1100 analysiert".
        string message = _analyzeProgress.HasAnalyzed
            ? _localizer.Format("Sort_AnalyzeProgress", _analyzeProgress.Analyzed, _analyzeProgress.Total)
            : _localizer.Format("Common_GatherProgress", _analyzeProgress.Gathered, _analyzeProgress.Total);

        _status.ReportPipelineProgress(message, _analyzeProgress.GatherPercent, _analyzeProgress.AnalyzePercent);
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
        _gathering.ResetOffsets();
        Proposals.Clear();
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

    [LoggerMessage(EventId = 3105, Level = LogLevel.Error, Message = "Die Suche nach Urlaubs-Zeiträumen ist fehlgeschlagen.")]
    public static partial void TripSearchFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 3106, Level = LogLevel.Warning, Message = "Auf das Protokoll der Analyseläufe konnte nicht zugegriffen werden; es wird kein Lauf zum Fortsetzen angeboten.")]
    public static partial void AnalysisJournalUnreadable(ILogger logger, Exception exception);
}
