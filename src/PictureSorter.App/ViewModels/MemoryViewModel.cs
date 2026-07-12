using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PictureSorter.Application.Services;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.App.ViewModels;

/// <summary>
/// ViewModel der Gedächtnis-Verwaltung. Zeigt, was sich die Anwendung zu welchen
/// Fotos gemerkt hat, und erlaubt es, einzelne Entscheidungen oder ein ganzes
/// Ordner-Gedächtnis zu verwerfen. Der Ablauf folgt dem expliziten
/// <see cref="MemoryState"/>.
/// </summary>
internal sealed partial class MemoryViewModel : ObservableObject
{
    private const string AllFilter = "Alle";

    private readonly ISortMemory _memory;
    private readonly IConfirmationService _confirmationService;
    private readonly StatusBarViewModel _status;
    private readonly ILogger<MemoryViewModel> _logger;

    // Ungefilterter Gesamtbestand; die angezeigte Liste ist eine Sicht darauf.
    private readonly List<SortMemoryRecord> _allRecords = [];

    /// <summary>Expliziter Zustand der Ansicht.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearFolderCommand))]
    public partial MemoryState State { get; set; }

    /// <summary>Der gewählte Ordner-Filter („Alle" oder ein konkreter Ordner).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ClearFolderCommand))]
    public partial string SelectedFolder { get; set; }

    /// <summary>Der gewählte Kategorie-Filter („Alle" oder eine konkrete Kategorie).</summary>
    [ObservableProperty]
    public partial string SelectedCategory { get; set; }

    /// <summary>
    /// Initialisiert das ViewModel.
    /// </summary>
    /// <param name="memory">Das dauerhafte Sortier-Gedächtnis.</param>
    /// <param name="confirmationService">Die Rückfrage vor dem Löschen.</param>
    /// <param name="status">Die gemeinsame Statusleiste der Anwendung.</param>
    /// <param name="logger">Der Logger.</param>
    public MemoryViewModel(
        ISortMemory memory,
        IConfirmationService confirmationService,
        StatusBarViewModel status,
        ILogger<MemoryViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(confirmationService);
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(logger);

        _memory = memory;
        _confirmationService = confirmationService;
        _status = status;
        _logger = logger;

        State = MemoryState.Idle;
        SelectedFolder = AllFilter;
        SelectedCategory = AllFilter;
    }

    /// <summary>Die angezeigten (gefilterten) Einträge.</summary>
    public ObservableCollection<SortMemoryItemViewModel> Items { get; } = [];

    /// <summary>Auswählbare Ordner-Filter.</summary>
    public ObservableCollection<string> Folders { get; } = [AllFilter];

    /// <summary>Auswählbare Kategorie-Filter.</summary>
    public ObservableCollection<string> Categories { get; } = [AllFilter];

    /// <summary><see langword="true"/>, wenn gerade kein Vorgang läuft.</summary>
    public bool IsInteractive => State is MemoryState.Idle
        or MemoryState.Review
        or MemoryState.Empty
        or MemoryState.Error;

    /// <summary><see langword="true"/>, wenn nichts gemerkt ist (Leerzustand anzeigen).</summary>
    public bool IsEmpty => State is MemoryState.Empty;

    /// <summary>Ein konkreter Ordner ist gewählt und kann geleert werden.</summary>
    public bool CanClearFolder =>
        IsInteractive && !string.Equals(SelectedFolder, AllFilter, StringComparison.Ordinal);

    /// <summary>Neu laden ist möglich, solange kein Vorgang läuft.</summary>
    public bool CanRefresh => IsInteractive;

    /// <summary>
    /// Lädt alle Einträge des Gedächtnisses neu.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync()
    {
        State = MemoryState.Loading;
        try
        {
            IReadOnlyList<SortMemoryRecord> records = await _memory
                .GetAllAsync(CancellationToken.None)
                .ConfigureAwait(true);

            _allRecords.Clear();
            _allRecords.AddRange(records);
            RebuildFilters();
            ApplyFilter();

            State = _allRecords.Count == 0 ? MemoryState.Empty : MemoryState.Review;
            _status.Report(_allRecords.Count == 0
                ? "Es ist noch nichts gemerkt. Nach dem ersten Sortieren erscheinen hier die Entscheidungen."
                : $"{_allRecords.Count} gemerkte Entscheidungen geladen.");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException)
        {
            MemoryViewLog.LoadFailed(_logger, ex);
            State = MemoryState.Error;
            _status.Report("Das Gedächtnis konnte nicht geladen werden.", StatusSeverity.Error);
        }
    }

    /// <summary>
    /// Entfernt einen einzelnen Eintrag nach Rückfrage.
    /// </summary>
    /// <param name="item">Der zu entfernende Eintrag.</param>
    [RelayCommand]
    private async Task DeleteAsync(SortMemoryItemViewModel? item)
    {
        if (item is null || !IsInteractive)
        {
            return;
        }

        bool confirmed = await _confirmationService.ConfirmAsync(
            "Eintrag vergessen",
            $"Die gemerkte Entscheidung zu „{item.FileName}“ (Kategorie „{item.CategoryName}“) wird verworfen. "
                + "Das Foto wird beim nächsten Lauf wieder bewertet. Fortfahren?",
            "Vergessen",
            "Abbrechen").ConfigureAwait(true);
        if (!confirmed)
        {
            return;
        }

        State = MemoryState.Deleting;
        try
        {
            await _memory.RemoveAsync(
                item.Record.FolderPath,
                item.Record.FileSignature,
                item.Record.CategoryName,
                CancellationToken.None).ConfigureAwait(true);

            _ = _allRecords.Remove(item.Record);
            _ = Items.Remove(item);
            RebuildFilters();

            State = _allRecords.Count == 0 ? MemoryState.Empty : MemoryState.Review;
            _status.Report("Eintrag vergessen.", StatusSeverity.Success);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException)
        {
            MemoryViewLog.DeleteFailed(_logger, ex);
            State = MemoryState.Error;
            _status.Report("Der Eintrag konnte nicht entfernt werden.", StatusSeverity.Error);
        }
    }

    /// <summary>
    /// Verwirft das gesamte Gedächtnis des gewählten Ordners nach Rückfrage.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanClearFolder))]
    private async Task ClearFolderAsync()
    {
        string folder = SelectedFolder;
        int affected = _allRecords.Count(record => string.Equals(record.FolderPath, folder, StringComparison.Ordinal));

        bool confirmed = await _confirmationService.ConfirmAsync(
            "Ordner-Gedächtnis leeren",
            $"Alle {affected} gemerkten Entscheidungen für „{folder}“ werden verworfen. "
                + "Die Fotos werden beim nächsten Lauf wieder bewertet. Fortfahren?",
            "Leeren",
            "Abbrechen").ConfigureAwait(true);
        if (!confirmed)
        {
            return;
        }

        State = MemoryState.Deleting;
        try
        {
            await _memory.ClearFolderAsync(folder, CancellationToken.None).ConfigureAwait(true);
            _ = _allRecords.RemoveAll(record => string.Equals(record.FolderPath, folder, StringComparison.Ordinal));

            SelectedFolder = AllFilter;
            RebuildFilters();
            ApplyFilter();

            State = _allRecords.Count == 0 ? MemoryState.Empty : MemoryState.Review;
            _status.Report($"{affected} Einträge für „{folder}“ vergessen.", StatusSeverity.Success);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException)
        {
            MemoryViewLog.DeleteFailed(_logger, ex);
            State = MemoryState.Error;
            _status.Report("Das Ordner-Gedächtnis konnte nicht geleert werden.", StatusSeverity.Error);
        }
    }

    partial void OnStateChanged(MemoryState value)
    {
        OnPropertyChanged(nameof(IsInteractive));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(CanClearFolder));
    }

    partial void OnSelectedFolderChanged(string value) => ApplyFilter();

    partial void OnSelectedCategoryChanged(string value) => ApplyFilter();

    // Baut die angezeigte Liste aus dem Gesamtbestand und den gewählten Filtern neu auf.
    private void ApplyFilter()
    {
        Items.Clear();
        foreach (SortMemoryRecord record in _allRecords.Where(Matches))
        {
            Items.Add(new SortMemoryItemViewModel(record));
        }
    }

    private bool Matches(SortMemoryRecord record)
    {
        bool folderMatches = string.Equals(SelectedFolder, AllFilter, StringComparison.Ordinal)
            || string.Equals(record.FolderPath, SelectedFolder, StringComparison.Ordinal);
        bool categoryMatches = string.Equals(SelectedCategory, AllFilter, StringComparison.Ordinal)
            || string.Equals(record.CategoryName, SelectedCategory, StringComparison.Ordinal);

        return folderMatches && categoryMatches;
    }

    // Hält die Filterlisten am Bestand ausgerichtet. Eine Auswahl, die durch das
    // Löschen ungültig geworden ist, fällt auf „Alle" zurück.
    private void RebuildFilters()
    {
        SelectedFolder = FillFilter(Folders, _allRecords.Select(record => record.FolderPath), SelectedFolder);
        SelectedCategory = FillFilter(Categories, _allRecords.Select(record => record.CategoryName), SelectedCategory);
    }

    private static string FillFilter(
        ObservableCollection<string> target,
        IEnumerable<string> values,
        string selected)
    {
        List<string> distinct = [AllFilter, .. values.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

        target.Clear();
        foreach (string value in distinct)
        {
            target.Add(value);
        }

        return distinct.Contains(selected, StringComparer.Ordinal) ? selected : AllFilter;
    }
}

/// <summary>
/// Quellgenerierte Logmeldungen der Gedächtnis-Verwaltung.
/// </summary>
internal static partial class MemoryViewLog
{
    [LoggerMessage(EventId = 3600, Level = LogLevel.Error, Message = "Das Sortier-Gedächtnis konnte nicht geladen werden.")]
    public static partial void LoadFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 3601, Level = LogLevel.Error, Message = "Ein Gedächtnis-Eintrag konnte nicht entfernt werden.")]
    public static partial void DeleteFailed(ILogger logger, Exception exception);
}
