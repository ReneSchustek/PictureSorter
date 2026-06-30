using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PictureSorter.Application.Services;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Application.ViewModels;

/// <summary>
/// ViewModel der Duplikate-Ansicht. Steuert den Ablauf Suche → Durchsicht →
/// Löschen über den expliziten <see cref="DuplicateState"/>. Das Löschen
/// verschiebt Dateien in den Papierkorb und verlangt eine Bestätigung.
/// </summary>
public sealed partial class DuplicatesViewModel : ObservableObject, IDisposable
{
    private readonly IDuplicateScanner _scanner;
    private readonly IFileDeleter _fileDeleter;
    private readonly IFolderPicker _folderPicker;
    private readonly IConfirmationService _confirmationService;
    private readonly StatusBarViewModel _status;
    private readonly ILogger<DuplicatesViewModel> _logger;

    private CancellationTokenSource? _cancellation;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private DuplicateState _state = DuplicateState.Idle;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    private string _sourceFolder = string.Empty;

    [ObservableProperty]
    private bool _includeSubfolders = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand))]
    private int _markedCount;

    /// <summary>
    /// Initialisiert das ViewModel.
    /// </summary>
    /// <param name="scanner">Die Duplikat-Suche.</param>
    /// <param name="fileDeleter">Das Löschen in den Papierkorb.</param>
    /// <param name="folderPicker">Der Ordnerauswahl-Dialog.</param>
    /// <param name="confirmationService">Die Rückfrage vor dem Löschen.</param>
    /// <param name="status">Die gemeinsame Statusleiste der Anwendung.</param>
    /// <param name="logger">Der Logger.</param>
    public DuplicatesViewModel(
        IDuplicateScanner scanner,
        IFileDeleter fileDeleter,
        IFolderPicker folderPicker,
        IConfirmationService confirmationService,
        StatusBarViewModel status,
        ILogger<DuplicatesViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(scanner);
        ArgumentNullException.ThrowIfNull(fileDeleter);
        ArgumentNullException.ThrowIfNull(folderPicker);
        ArgumentNullException.ThrowIfNull(confirmationService);
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(logger);

        _scanner = scanner;
        _fileDeleter = fileDeleter;
        _folderPicker = folderPicker;
        _confirmationService = confirmationService;
        _status = status;
        _logger = logger;
    }

    /// <summary>
    /// Die gefundenen Duplikat-Gruppen.
    /// </summary>
    public ObservableCollection<DuplicateGroupViewModel> Groups { get; } = [];

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

    /// <summary>
    /// Abbrechen ist möglich, solange eine Suche läuft.
    /// </summary>
    public bool CanCancel => State is DuplicateState.Scanning;

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
        _status.Begin("Duplikate werden gesucht…", Cancel);
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
                ? $"{Groups.Count} Duplikat-Gruppen gefunden. {MarkedCount} zum Löschen vorgemerkt."
                : "Keine Duplikate gefunden.");
        }
        catch (OperationCanceledException)
        {
            State = DuplicateState.Idle;
            _status.Finish("Suche abgebrochen.", StatusSeverity.Warning);
        }
        catch (DirectoryNotFoundException ex)
        {
            DuplicatesLog.ScanFailed(_logger, ex);
            State = DuplicateState.Error;
            _status.Finish("Der Ordner wurde nicht gefunden. Bitte prüfe den Pfad.", StatusSeverity.Error);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DuplicatesLog.ScanFailed(_logger, ex);
            State = DuplicateState.Error;
            _status.Finish("Die Suche ist fehlgeschlagen.", StatusSeverity.Error);
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
            "Duplikate löschen",
            $"{marked.Count} Datei(en) werden in den Papierkorb verschoben. Fortfahren?",
            "In Papierkorb",
            "Abbrechen").ConfigureAwait(true);
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
        _status.Report("Abbruch angefordert…");
    }

    private async Task DeleteMarkedAsync(IReadOnlyList<DuplicatePhotoViewModel> marked)
    {
        using IDisposable? logScope = _logger.BeginScope("Löschen {CorrelationId}", NewCorrelationId());
        State = DuplicateState.Deleting;
        _status.Report("Dateien werden in den Papierkorb verschoben…");

        int deleted = 0;
        foreach (DuplicatePhotoViewModel photo in marked)
        {
            try
            {
                await _fileDeleter.DeleteAsync(photo.FilePath, CancellationToken.None).ConfigureAwait(true);
                RemovePhoto(photo);
                deleted++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                DuplicatesLog.DeleteFailed(_logger, photo.FileName, ex);
            }
        }

        PruneEmptyGroups();
        UpdateMarkedCount();
        State = Groups.Count > 0 ? DuplicateState.Review : DuplicateState.Completed;
        _status.Report(string.Create(
            CultureInfo.GetCultureInfo("de-DE"),
            $"{deleted} Datei(en) in den Papierkorb verschoben. {Groups.Count} Gruppen verbleiben."));
    }

    private void OnScanProgress(DuplicateScanProgress progress)
    {
        if (progress.Total > 0)
        {
            _status.Report($"Duplikate werden gesucht… {progress.Processed}/{progress.Total}");
        }
    }

    private void PopulateGroups(IReadOnlyList<DuplicateGroup> groups)
    {
        foreach (DuplicateGroup group in groups)
        {
            DuplicateGroupViewModel groupViewModel = new(group);
            foreach (DuplicatePhotoViewModel photo in groupViewModel.Photos)
            {
                photo.PropertyChanged += OnPhotoPropertyChanged;
            }

            Groups.Add(groupViewModel);
        }

        UpdateMarkedCount();
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
        for (int index = Groups.Count - 1; index >= 0; index--)
        {
            if (Groups[index].Photos.Count < 2)
            {
                UnsubscribeGroup(Groups[index]);
                Groups.RemoveAt(index);
            }
        }
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
        foreach (DuplicateGroupViewModel group in Groups)
        {
            UnsubscribeGroup(group);
        }

        Groups.Clear();
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
