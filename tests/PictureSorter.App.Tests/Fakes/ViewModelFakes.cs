using PictureSorter.App.Services;
using PictureSorter.Application.Services;
using PictureSorter.Core.Entities;
using PictureSorter.Core.Enums;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.App.Tests.Fakes;

/// <summary>Liefert eine feste Liste von Duplikat-Gruppen und meldet Fortschritt.</summary>
internal sealed class FakeDuplicateScanner(IReadOnlyList<DuplicateGroup> groups) : IDuplicateScanner
{
    public Task<IReadOnlyList<DuplicateGroup>> ScanAsync(
        string folderPath,
        bool includeSubfolders,
        IProgress<DuplicateScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(new DuplicateScanProgress(groups.Count, groups.Count));
        return Task.FromResult(groups);
    }
}

/// <summary>Protokolliert gelöschte Pfade, ohne echte Dateien anzufassen.</summary>
internal sealed class FakeFileDeleter : IFileDeleter
{
    public List<string> Deleted { get; } = [];

    /// <summary>
    /// Läuft nach jedem gelöschten Pfad und bekommt deren Anzahl. Damit lässt sich
    /// ein Abbruch mitten im Lauf auslösen, ohne den Ablauf mit Wartezeiten
    /// nachzustellen.
    /// </summary>
    public Action<int>? AfterDelete { get; set; }

    public Task DeleteAsync(string filePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Deleted.Add(filePath);
        AfterDelete?.Invoke(Deleted.Count);
        return Task.CompletedTask;
    }
}

/// <summary>Merkt sich, wohin navigiert wurde, ohne eine Oberfläche zu brauchen.</summary>
internal sealed class FakeNavigationService : INavigationService
{
    public List<AppSection> Navigations { get; } = [];

    public void NavigateTo(AppSection section) => Navigations.Add(section);
}

/// <summary>Meldet einen festen Zustand der lokalen KI.</summary>
internal sealed class FakeModelAvailabilityChecker(ModelAvailability availability) : IModelAvailabilityChecker
{
    /// <summary>Alles vorhanden.</summary>
    public static FakeModelAvailabilityChecker Ready() => new(new ModelAvailability
    {
        IsReachable = true,
        RequiredModels = ["llava", "nomic-embed-text"],
        MissingModels = [],
    });

    /// <summary>Ollama läuft, aber ein Modell fehlt.</summary>
    public static FakeModelAvailabilityChecker Missing(string model) => new(new ModelAvailability
    {
        IsReachable = true,
        RequiredModels = ["llava", "nomic-embed-text"],
        MissingModels = [model],
    });

    /// <summary>Ollama ist gar nicht erreichbar.</summary>
    public static FakeModelAvailabilityChecker Unreachable() => new(new ModelAvailability
    {
        IsReachable = false,
        RequiredModels = ["llava", "nomic-embed-text"],
        MissingModels = ["llava", "nomic-embed-text"],
    });

    public Task<ModelAvailability> CheckAsync(CancellationToken cancellationToken) =>
        Task.FromResult(availability);
}

/// <summary>Liefert einen festen Ordnerpfad und eine feste Bildauswahl.</summary>
internal sealed class FakeFolderPicker(string? folder, IReadOnlyList<string>? images = null) : IFolderPicker
{
    public Task<string?> PickFolderAsync(CancellationToken cancellationToken) => Task.FromResult(folder);

    public Task<IReadOnlyList<string>> PickImagesAsync(CancellationToken cancellationToken) =>
        Task.FromResult(images ?? []);
}

/// <summary>Beantwortet jede Rückfrage mit einem festen Ergebnis.</summary>
internal sealed class StubConfirmationService(bool result) : IConfirmationService
{
    public Task<bool> ConfirmAsync(string title, string message, string confirmText, string cancelText) =>
        Task.FromResult(result);
}

/// <summary>
/// Liefert feste Vorschläge und protokolliert, was angewendet bzw. abgewählt wurde.
/// </summary>
internal sealed class FakePhotoSorter(IReadOnlyList<SortProposal> proposals) : IPhotoSorter
{
    public List<SortProposal> Applied { get; } = [];

    public List<SortProposal> Ignored { get; } = [];

    /// <summary>Die Betriebsart des zuletzt angewendeten Laufs.</summary>
    public FileOperationMode LastOperation { get; private set; }

    public Task<IReadOnlyList<SortProposal>> CreateProposalsAsync(
        string sourceFolder,
        Category category,
        bool includeSubfolders,
        IProgress<SortProgress>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(new SortProgress(proposals.Count, proposals.Count));
        return Task.FromResult(proposals);
    }

    public Task<int> ApplyProposalsAsync(
        IReadOnlyList<SortProposal> toApply,
        FileOperationMode operation,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        Applied.AddRange(toApply);
        LastOperation = operation;
        return Task.FromResult(toApply.Count);
    }

    public Task IgnoreProposalsAsync(IReadOnlyList<SortProposal> toIgnore, CancellationToken cancellationToken)
    {
        Ignored.AddRange(toIgnore);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Hält einen zurücknehmbaren Lauf bereit und protokolliert, ob er zurückgenommen
/// wurde. Ohne echtes Dateisystem.
/// </summary>
internal sealed class FakeSortUndoService : ISortUndoService
{
    private SortRun? _run;

    /// <summary>Wie oft das Rückgängigmachen ausgeführt wurde.</summary>
    public int UndoCount { get; private set; }

    /// <summary>Das Ergebnis, das das Rückgängigmachen liefern soll.</summary>
    public UndoResult Result { get; set; } = new() { Restored = 0, Skipped = 0 };

    /// <summary>Legt einen zurücknehmbaren Lauf an.</summary>
    /// <param name="fileCount">Zahl der verschobenen Fotos.</param>
    public void SetUndoableRun(int fileCount)
    {
        _run = new SortRun
        {
            Id = Guid.NewGuid(),
            StartedAt = new DateTimeOffset(2026, 7, 1, 8, 0, 0, TimeSpan.Zero),
            SourceFolder = @"C:\fotos",
            CategoryName = "Familie",
            Items =
            [
                .. Enumerable.Range(0, fileCount).Select(index => new SortRunItem
                {
                    SourcePath = Path.Combine(@"C:\fotos", $"foto{index}.jpg"),
                    TargetPath = Path.Combine(@"C:\fotos\Familie", $"foto{index}.jpg"),
                    FileSignature = $"sig-{index}",
                }),
            ],
        };

        Result = new UndoResult { Restored = fileCount, Skipped = 0 };
    }

    public Task<SortRun?> GetUndoableRunAsync(CancellationToken cancellationToken) => Task.FromResult(_run);

    public Task<UndoResult?> UndoLastRunAsync(CancellationToken cancellationToken)
    {
        if (_run is null)
        {
            return Task.FromResult<UndoResult?>(null);
        }

        UndoCount++;
        _run = null;
        return Task.FromResult<UndoResult?>(Result);
    }
}

/// <summary>
/// Liefert eine feste Fotoliste und hält die zuletzt erfragte Höchstzahl fest –
/// daran hängt, ob die Beispielauswahl den ganzen Ordner einliest oder nur so viel,
/// wie sie anzeigt.
/// </summary>
internal sealed class FakePhotoSource(IReadOnlyList<Photo> photos) : IPhotoSource
{
    /// <summary>Die bei der letzten Abfrage übergebene Höchstzahl.</summary>
    public int? LastMaxCount { get; private set; }

    /// <summary>Der bei der letzten Abfrage übergebene Startpunkt.</summary>
    public int LastSkip { get; private set; }

    public Task<IReadOnlyList<Photo>> GetPhotosAsync(
        string folderPath,
        bool includeSubfolders,
        int skip,
        int? maxCount,
        CancellationToken cancellationToken)
    {
        LastMaxCount = maxCount;
        LastSkip = skip;
        return Task.FromResult<IReadOnlyList<Photo>>([.. photos.Skip(skip).Take(maxCount ?? int.MaxValue)]);
    }
}

/// <summary>Liefert eine feste, bereits angelernte Kategorie.</summary>
internal sealed class FakeCategoryTrainer(Category category) : ICategoryTrainer
{
    public Task<Category> TrainAsync(
        string name,
        string description,
        CategoryKind kind,
        IReadOnlyList<TrainingExample> examples,
        IProgress<TrainingProgress>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(new TrainingProgress(examples.Count, examples.Count));
        return Task.FromResult(category);
    }
}

/// <summary>Hält Kategorien im Speicher.</summary>
internal sealed class FakeCategoryRepository : ICategoryRepository
{
    public List<Category> Saved { get; } = [];

    public Task<IReadOnlyList<Category>> LoadAllAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Category>>([.. Saved]);

    public Task SaveAllAsync(IReadOnlyList<Category> categories, CancellationToken cancellationToken)
    {
        Saved.Clear();
        Saved.AddRange(categories);
        return Task.CompletedTask;
    }
}

/// <summary>Hält das Sortier-Gedächtnis im Speicher.</summary>
internal sealed class FakeSortMemory : ISortMemory
{
    public List<SortMemoryRecord> Records { get; } = [];

    public Task<IReadOnlyList<SortMemoryRecord>> GetForFolderAsync(
        string folderPath,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SortMemoryRecord>>(
            [.. Records.Where(record => record.FolderPath == folderPath)]);

    public Task<SortMemoryRecord?> GetAsync(
        string folderPath,
        string fileSignature,
        string categoryName,
        CancellationToken cancellationToken) =>
        Task.FromResult(Records.Find(record =>
            record.FolderPath == folderPath
            && record.FileSignature == fileSignature
            && record.CategoryName == categoryName));

    public Task UpsertAsync(SortMemoryRecord record, CancellationToken cancellationToken)
    {
        _ = Records.RemoveAll(existing =>
            existing.FolderPath == record.FolderPath
            && existing.FileSignature == record.FileSignature
            && existing.CategoryName == record.CategoryName);
        Records.Add(record);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(
        string folderPath,
        string fileSignature,
        string categoryName,
        CancellationToken cancellationToken)
    {
        _ = Records.RemoveAll(record =>
            record.FolderPath == folderPath
            && record.FileSignature == fileSignature
            && record.CategoryName == categoryName);
        return Task.CompletedTask;
    }

    public Task ClearFolderAsync(string folderPath, CancellationToken cancellationToken)
    {
        _ = Records.RemoveAll(record => record.FolderPath == folderPath);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SortMemoryRecord>> GetAllAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SortMemoryRecord>>([.. Records]);
}
