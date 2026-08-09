using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PictureSorter.App.Controls;
using PictureSorter.App.Services;
using PictureSorter.Application.Services;
using PictureSorter.Core.Enums;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.App.ViewModels;

/// <summary>
/// ViewModel der Duplikate-Ansicht. Steuert den Ablauf Suche → Durchsicht →
/// Löschen über den expliziten <see cref="DuplicateState"/>. Das Löschen
/// verschiebt Dateien in den Papierkorb und verlangt eine Bestätigung.
/// </summary>
internal sealed partial class DuplicatesViewModel : ObservableObject, IDisposable
{
    private readonly IDuplicateScanner _scanner;
    private readonly IFileDeleter _fileDeleter;
    private readonly IFolderPicker _folderPicker;
    private readonly IConfirmationService _confirmationService;
    private readonly StatusBarViewModel _status;
    private readonly ILocalizer _localizer;
    private readonly ILogger<DuplicatesViewModel> _logger;

    private CancellationTokenSource? _cancellation;

    /// <summary>Expliziter Ablaufzustand der Duplikate-Ansicht.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    public partial DuplicateState State { get; set; }

    /// <summary>Der zu durchsuchende Ordner.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    public partial string SourceFolder { get; set; }

    /// <summary><see langword="true"/>, um Unterordner einzubeziehen.</summary>
    [ObservableProperty]
    public partial bool IncludeSubfolders { get; set; }

    /// <summary>Anzahl der zum Löschen vorgemerkten Fotos.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand))]
    public partial int MarkedCount { get; set; }

    /// <summary>
    /// Initialisiert das ViewModel.
    /// </summary>
    /// <param name="scanner">Die Duplikat-Suche.</param>
    /// <param name="fileDeleter">Das Löschen in den Papierkorb.</param>
    /// <param name="folderPicker">Der Ordnerauswahl-Dialog.</param>
    /// <param name="confirmationService">Die Rückfrage vor dem Löschen.</param>
    /// <param name="status">Die gemeinsame Statusleiste der Anwendung.</param>
    /// <param name="localizer">Die Textquelle.</param>
    /// <param name="logger">Der Logger.</param>
    public DuplicatesViewModel(
        IDuplicateScanner scanner,
        IFileDeleter fileDeleter,
        IFolderPicker folderPicker,
        IConfirmationService confirmationService,
        StatusBarViewModel status,
        ILocalizer localizer,
        ILogger<DuplicatesViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(scanner);
        ArgumentNullException.ThrowIfNull(fileDeleter);
        ArgumentNullException.ThrowIfNull(folderPicker);
        ArgumentNullException.ThrowIfNull(confirmationService);
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(logger);

        _scanner = scanner;
        _fileDeleter = fileDeleter;
        _folderPicker = folderPicker;
        _confirmationService = confirmationService;
        _status = status;
        _localizer = localizer;
        _logger = logger;

        State = DuplicateState.Idle;
        SourceFolder = string.Empty;
        IncludeSubfolders = true;
    }

    /// <summary>
    /// Die gefundenen Duplikat-Gruppen.
    /// </summary>
    // Der ganze Fund. Getrennt von der Anzeige, damit ein Filter nur bestimmt, was man
    // sieht — und nicht klammheimlich den Bestand verkleinert.
    private readonly List<DuplicateGroupViewModel> _allGroups = [];

    public ObservableCollection<DuplicateGroupViewModel> Groups { get; } = [];

    /// <summary>Die Filter: alle Gruppen, nur gleiche, nur ähnliche.</summary>
    public ObservableCollection<FilterChoice> Filters { get; } = [];

    /// <summary>
    /// Der Suchtext. Er greift über Dateiname und Ordner — wer ein bestimmtes Bild sucht,
    /// weiß meist das eine oder das andere.
    /// </summary>
    public string SearchText
    {
        get;
        set
        {
            field = value ?? string.Empty;
            OnPropertyChanged();
            ApplyFilter();
        }
    } = string.Empty;

    /// <summary><see langword="true"/>, wenn Suche oder Filter gerade etwas ausblenden.</summary>
    public bool IsFiltered => Groups.Count != _allGroups.Count;

    /// <summary>
    /// <see langword="true"/>, wenn Gruppen gefunden wurden, Suche oder Filter aber
    /// nichts durchlassen.
    /// </summary>
    public bool ShowsNoMatch => _allGroups.Count > 0 && Groups.Count == 0;

    /// <summary>
    /// Eine Suche ist möglich, wenn kein Vorgang läuft und ein Ordner gewählt ist.
    /// </summary>
    public bool CanScan =>
        State is DuplicateState.Idle or DuplicateState.Review or DuplicateState.Completed or DuplicateState.Error
        && !string.IsNullOrWhiteSpace(SourceFolder);

    /// <summary>
    /// Löschen ist möglich, wenn Duplikate vorliegen und mindestens eines vorgemerkt ist.
    /// </summary>
    public bool CanDelete => State is DuplicateState.Review && MarkedCount > 0;

    // Die Schlüssel der Filter — unabhängig von der Sprache ihrer Chips.
    private const string AllFilter = "all";
    private const string ExactFilter = "exact";
    private const string SimilarFilter = "similar";

    private string _filter = AllFilter;

    /// <summary>
    /// Wählt den Filter der Fundliste.
    /// </summary>
    /// <param name="key">Der Schlüssel des Filters.</param>
    public void Filter(string key)
    {
        _filter = key;
        ApplyFilter();
    }

    // Baut die Anzeige aus dem Fund neu auf.
    //
    // Der Zählstand der Vormerkungen richtet sich danach — und das ist Absicht: Gelöscht
    // wird nur, was man sieht. Bei einer Aktion, die Dateien in den Papierkorb legt, wäre
    // alles andere eine böse Überraschung. (In der Sortier-Vorschau ist es umgekehrt: Dort
    // würde ein Ausblenden dazu führen, dass Vorschläge als abgewählt gelten und dauerhaft
    // verschwinden — dort zählt deshalb der ganze Bestand.)
    private void ApplyFilter()
    {
        Groups.Clear();
        foreach (DuplicateGroupViewModel group in _allGroups.Where(MatchesFilter))
        {
            Groups.Add(group);
        }

        UpdateMarkedCount();
        OnPropertyChanged(nameof(IsFiltered));
        OnPropertyChanged(nameof(ShowsNoMatch));
    }

    private bool MatchesFilter(DuplicateGroupViewModel group)
    {
        bool passesKind = _filter switch
        {
            ExactFilter => group.Kind == DuplicateKind.Exact,
            SimilarFilter => group.Kind == DuplicateKind.Similar,
            _ => true,
        };

        if (!passesKind)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return true;
        }

        string gesucht = SearchText.Trim();

        return group.Photos.Any(photo =>
            photo.FileName.Contains(gesucht, StringComparison.OrdinalIgnoreCase)
            || photo.FilePath.Contains(gesucht, StringComparison.OrdinalIgnoreCase));
    }

    private void BuildFilters()
    {
        Filters.Clear();
        Filters.Add(new FilterChoice(AllFilter, _localizer.Get("Duplicates_FilterAll")) { IsSelected = true });
        Filters.Add(new FilterChoice(ExactFilter, _localizer.Get("Duplicates_FilterExact")));
        Filters.Add(new FilterChoice(SimilarFilter, _localizer.Get("Duplicates_FilterSimilar")));
    }

    /// <summary>
    /// Abbrechen ist möglich, solange eine Suche oder ein Löschlauf läuft. Beim
    /// Löschen zählt das besonders: Wer bemerkt, dass er die falsche Auswahl
    /// bestätigt hat, muss den Lauf anhalten können, bevor der Rest im
    /// Papierkorb landet.
    /// </summary>
    public bool CanCancel => State is DuplicateState.Scanning or DuplicateState.Deleting;

    [RelayCommand]
    private async Task BrowseAsync()
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
        using IDisposable? logScope = _logger.BeginScope("Duplikatsuche {CorrelationId}", NewCorrelationId());
        State = DuplicateState.Scanning;
        _status.Begin(_localizer.Get("Duplicates_Scanning"), Cancel);
        _scanProgress = default;
        _scanThrottle.Reset();
        ClearGroups();
        _cancellation = new CancellationTokenSource();

        try
        {
            Progress<DuplicateScanProgress> progress = new(OnScanProgress);
            IReadOnlyList<DuplicateGroup> groups = await _scanner
                .ScanAsync(SourceFolder, IncludeSubfolders, progress, _cancellation.Token)
                .ConfigureAwait(true);

            PopulateGroups(groups);
            State = Groups.Count > 0 ? DuplicateState.Review : DuplicateState.Completed;
            _status.Finish(Groups.Count > 0
                ? _localizer.Format("Duplicates_GroupsFound", Groups.Count, MarkedCount)
                : _localizer.Get("Duplicates_NoneFound"));
        }
        catch (OperationCanceledException)
        {
            State = DuplicateState.Idle;
            _status.Finish(_localizer.Get("Duplicates_ScanCanceled"), StatusSeverity.Warning);
        }
        catch (DirectoryNotFoundException ex)
        {
            DuplicatesLog.ScanFailed(_logger, ex);
            State = DuplicateState.Error;
            _status.Finish(_localizer.Get("Duplicates_FolderNotFound"), StatusSeverity.Error);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DuplicatesLog.ScanFailed(_logger, ex);
            State = DuplicateState.Error;
            _status.Finish(_localizer.Get("Duplicates_ScanFailed"), StatusSeverity.Error);
        }
        finally
        {
            DisposeCancellation();
        }
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task DeleteSelectedAsync()
    {
        IReadOnlyList<DuplicatePhotoViewModel> marked = CollectMarked();
        if (marked.Count == 0)
        {
            return;
        }

        bool confirmed = await _confirmationService.ConfirmAsync(
            _localizer.Get("Duplicates_DeleteTitle"),
            _localizer.Format("Duplicates_DeleteMessage", marked.Count),
            _localizer.Get("Duplicates_DeletePrimary"),
            _localizer.Get("Common_Cancel")).ConfigureAwait(true);
        if (!confirmed)
        {
            return;
        }

        await DeleteMarkedAsync(marked).ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        _cancellation?.Cancel();
        _status.Report(_localizer.Get("Common_CancelRequested"));
    }

    private async Task DeleteMarkedAsync(IReadOnlyList<DuplicatePhotoViewModel> marked)
    {
        using IDisposable? logScope = _logger.BeginScope("Löschen {CorrelationId}", NewCorrelationId());
        State = DuplicateState.Deleting;
        _status.Begin(_localizer.Get("Duplicates_Deleting"), Cancel);
        _cancellation = new CancellationTokenSource();

        int deleted = 0;
        int processed = 0;
        bool canceled = false;

        try
        {
            foreach (DuplicatePhotoViewModel photo in marked)
            {
                _cancellation.Token.ThrowIfCancellationRequested();

                try
                {
                    await _fileDeleter.DeleteAsync(photo.FilePath, _cancellation.Token).ConfigureAwait(true);
                    RemovePhoto(photo);
                    deleted++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    DuplicatesLog.DeleteFailed(_logger, photo.FileName, ex);
                }

                processed++;
                _status.ReportProgress(
                    _localizer.Format("Duplicates_DeleteProgress", processed, marked.Count),
                    processed * 100d / marked.Count);
            }
        }
        catch (OperationCanceledException)
        {
            canceled = true;
        }
        finally
        {
            DisposeCancellation();
        }

        PruneEmptyGroups();
        UpdateMarkedCount();
        State = Groups.Count > 0 ? DuplicateState.Review : DuplicateState.Completed;
        _status.Finish(
            canceled
                ? _localizer.Format("Duplicates_DeleteCanceled", deleted)
                : _localizer.Format("Duplicates_Deleted", deleted, Groups.Count),
            canceled ? StatusSeverity.Warning : StatusSeverity.Success);
    }

    // Bis hierher wurde der Zählstand nur als Text gemeldet; der Balken lief daneben
    // unbestimmt weiter und zeigte nichts an. Jetzt tragen zwei Balken den tatsächlichen
    // Anteil – einer je Abschnitt, weil Laden und Prüfen gleichzeitig laufen.
    private ScanProgressPair _scanProgress;

    // Siehe ProgressThrottle: Ungefiltert bringen die Meldungen beider Abschnitte den
    // Oberflächen-Faden zum Erliegen, und die Statusleiste steht dann still.
    private readonly ProgressThrottle _scanThrottle = new(TimeSpan.FromMilliseconds(100));

    private void OnScanProgress(DuplicateScanProgress progress)
    {
        if (progress.Total <= 0)
        {
            return;
        }

        _scanProgress = _scanProgress.With(progress.Phase, progress.Processed, progress.Total);

        if (!_scanThrottle.ShouldReport(progress.Processed >= progress.Total))
        {
            return;
        }

        string message = _scanProgress.HasAnalyzed
            ? _localizer.Format("Duplicates_ScanProgress", _scanProgress.Analyzed, _scanProgress.Total)
            : _localizer.Format("Common_GatherProgress", _scanProgress.Gathered, _scanProgress.Total);

        _status.ReportPipelineProgress(message, _scanProgress.GatherPercent, _scanProgress.AnalyzePercent);
    }

    private void PopulateGroups(IReadOnlyList<DuplicateGroup> groups)
    {
        foreach (DuplicateGroup group in groups)
        {
            DuplicateGroupViewModel groupViewModel = new(group, _localizer);
            foreach (DuplicatePhotoViewModel photo in groupViewModel.Photos)
            {
                photo.PropertyChanged += OnPhotoPropertyChanged;
            }

            _allGroups.Add(groupViewModel);
        }

        ApplyFilter();
    }

    private IReadOnlyList<DuplicatePhotoViewModel> CollectMarked() =>
        [.. Groups.SelectMany(group => group.Photos).Where(photo => photo.IsMarkedForDeletion)];

    private void RemovePhoto(DuplicatePhotoViewModel photo)
    {
        foreach (DuplicateGroupViewModel group in Groups)
        {
            if (group.Photos.Remove(photo))
            {
                photo.PropertyChanged -= OnPhotoPropertyChanged;
                return;
            }
        }
    }

    private void PruneEmptyGroups()
    {
        // Eine Gruppe ist nur sinnvoll, solange sie mindestens zwei Bilder enthält.
        // Aufgeräumt wird im Bestand, nicht in der Anzeige: Sonst käme eine geleerte
        // Gruppe beim Zurücksetzen des Filters wieder zum Vorschein.
        for (int index = _allGroups.Count - 1; index >= 0; index--)
        {
            if (_allGroups[index].Photos.Count < 2)
            {
                UnsubscribeGroup(_allGroups[index]);
                _allGroups.RemoveAt(index);
            }
        }

        ApplyFilter();
    }

    private void OnPhotoPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DuplicatePhotoViewModel.IsMarkedForDeletion))
        {
            UpdateMarkedCount();
        }
    }

    private void UpdateMarkedCount() =>
        MarkedCount = Groups.SelectMany(group => group.Photos).Count(photo => photo.IsMarkedForDeletion);

    private void ClearGroups()
    {
        foreach (DuplicateGroupViewModel group in _allGroups)
        {
            UnsubscribeGroup(group);
        }

        _allGroups.Clear();
        Groups.Clear();
        SearchText = string.Empty;
        _filter = AllFilter;
        BuildFilters();
        UpdateMarkedCount();
    }

    private void UnsubscribeGroup(DuplicateGroupViewModel group)
    {
        foreach (DuplicatePhotoViewModel photo in group.Photos)
        {
            photo.PropertyChanged -= OnPhotoPropertyChanged;
        }
    }

    private void DisposeCancellation()
    {
        _cancellation?.Dispose();
        _cancellation = null;
    }

    // Kurze Korrelations-ID je Vorgang. Als Logging-Scope geöffnet, verknüpft sie
    // alle Logeinträge eines Laufs zu einer nachvollziehbaren Einheit.
    private static string NewCorrelationId() => Guid.NewGuid().ToString("N")[..8];

    /// <summary>
    /// Gibt Ereignis-Abonnements und das Abbruch-Token frei.
    /// </summary>
    public void Dispose()
    {
        ClearGroups();
        DisposeCancellation();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Quellgenerierte Logmeldungen des Duplikate-ViewModels.
/// </summary>
internal static partial class DuplicatesLog
{
    [LoggerMessage(EventId = 3400, Level = LogLevel.Error, Message = "Duplikat-Suche fehlgeschlagen.")]
    public static partial void ScanFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 3401, Level = LogLevel.Warning, Message = "Datei {FileName} konnte nicht gelöscht werden.")]
    public static partial void DeleteFailed(ILogger logger, string fileName, Exception exception);
}
