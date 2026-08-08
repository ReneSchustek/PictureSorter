using System.Runtime.CompilerServices;
using PictureSorter.Core.Entities;
using PictureSorter.Core.Enums;
using PictureSorter.Core.Exceptions;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Application.Tests.Fakes;

/// <summary>Liefert eine feste Fotoliste.</summary>
internal sealed class FakePhotoSource(IReadOnlyList<Photo> photos) : IPhotoSource
{
    public Task<IReadOnlyList<Photo>> GetPhotosAsync(
        string folderPath,
        bool includeSubfolders,
        int skip,
        int? maxCount,
        IProgress<PhotoScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<Photo> found = [.. photos.Skip(skip).Take(maxCount ?? int.MaxValue)];

        // Wie die echte Quelle: zuerst die Gesamtzahl, dann je eingelesener Datei.
        progress?.Report(new PhotoScanProgress(0, found.Count));
        for (int index = 0; index < found.Count; index++)
        {
            progress?.Report(new PhotoScanProgress(index + 1, found.Count));
        }

        return Task.FromResult(found);
    }

    public async IAsyncEnumerable<ScannedPhoto> StreamPhotosAsync(
        string folderPath,
        bool includeSubfolders,
        int skip,
        int? maxCount,
        IProgress<PhotoScanProgress>? progress,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        IReadOnlyList<Photo> found = [.. photos.Skip(skip).Take(maxCount ?? int.MaxValue)];
        progress?.Report(new PhotoScanProgress(0, found.Count));

        for (int index = 0; index < found.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Ein Taktwechsel je Bild: Ohne ihn liefe die ganze Kette synchron durch, und
            // der Test prüfte ein Verhalten, das es im Betrieb nicht gibt.
            await Task.Yield();

            progress?.Report(new PhotoScanProgress(index + 1, found.Count));
            yield return new ScannedPhoto(found[index], index, found.Count);
        }
    }
}

/// <summary>
/// Liefert weniger Fotos, als der Ordner Dateien enthält — so, wie die echte Quelle es
/// tut, wenn eine Datei vom Virenscanner weggeräumt wurde oder ihr Codec fehlt.
///
/// Genau dieser Fall lässt den Zählstand der Bewertung die Gesamtzahl nie erreichen. Er
/// gehört in die Tests, weil er in der Anzeige wie ein Stillstand aussah.
/// </summary>
internal sealed class PartialPhotoSource(IReadOnlyList<Photo> photos, int total) : IPhotoSource
{
    public Task<IReadOnlyList<Photo>> GetPhotosAsync(
        string folderPath,
        bool includeSubfolders,
        int skip,
        int? maxCount,
        IProgress<PhotoScanProgress>? progress,
        CancellationToken cancellationToken) => Task.FromResult(photos);

    public async IAsyncEnumerable<ScannedPhoto> StreamPhotosAsync(
        string folderPath,
        bool includeSubfolders,
        int skip,
        int? maxCount,
        IProgress<PhotoScanProgress>? progress,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        progress?.Report(new PhotoScanProgress(0, total));

        for (int index = 0; index < photos.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();

            // Gezählt werden die bearbeiteten Dateien, nicht die brauchbaren — auch die
            // ausgefallene zählt mit.
            progress?.Report(new PhotoScanProgress(index + 1, total));
            yield return new ScannedPhoto(photos[index], index, total);
        }

        progress?.Report(new PhotoScanProgress(total, total));
    }
}

/// <summary>Erzeugt Embeddings über eine Testfunktion.</summary>
internal sealed class FakeEmbeddingProvider(Func<Photo, float[]> vectorFactory, string model = "fake")
    : IEmbeddingProvider
{
    public Task<ImageEmbedding> CreateEmbeddingAsync(Photo photo, CancellationToken cancellationToken)
        => Task.FromResult(new ImageEmbedding(vectorFactory(photo), model));
}

/// <summary>Liefert einen festen Vektor und zählt, wie oft er angefordert wurde.</summary>
internal sealed class CountingEmbeddingProvider(float[] vector, string model = "fake") : IEmbeddingProvider
{
    private int _callCount;

    // Interlocked, weil der Sortierdienst mehrere Fotos gleichzeitig bewertet: Ein
    // gewöhnliches ++ verlöre Aufrufe, und der Test meldete zu wenige KI-Anfragen —
    // ausgerechnet die Zahl, um die es hier geht.
    public int CallCount => Volatile.Read(ref _callCount);

    public Task<ImageEmbedding> CreateEmbeddingAsync(Photo photo, CancellationToken cancellationToken)
    {
        _ = Interlocked.Increment(ref _callCount);
        return Task.FromResult(new ImageEmbedding(vector, model));
    }
}

/// <summary>Simuliert eine nicht erreichbare KI.</summary>
internal sealed class ThrowingEmbeddingProvider : IEmbeddingProvider
{
    public Task<ImageEmbedding> CreateEmbeddingAsync(Photo photo, CancellationToken cancellationToken)
        => throw new AiUnavailableException();
}

/// <summary>Liefert ein festes Vision-Urteil und zählt die Aufrufe.</summary>
internal sealed class FakeImageClassifier(VisionVerdict verdict) : IImageClassifier
{
    public int CallCount { get; private set; }

    public Task<VisionVerdict> ClassifyAsync(Photo photo, Category category, CancellationToken cancellationToken)
    {
        CallCount++;
        return Task.FromResult(verdict);
    }
}

/// <summary>Protokolliert angewendete Vorschläge, ohne Dateien zu verschieben.</summary>
internal sealed class FakeFileOrganizer : IFileOrganizer
{
    public List<SortProposal> Applied { get; } = [];

    /// <summary>Zurückgeholte Dateien als Paare (Ziel → Ursprung).</summary>
    public List<(string CurrentPath, string OriginalPath)> Restored { get; } = [];

    /// <summary>Ordner, deren Entfernung geprüft wurde.</summary>
    public List<string> CheckedFolders { get; } = [];

    /// <summary>Zielpfade, die beim Zurückholen als „nicht auffindbar" gelten sollen.</summary>
    public HashSet<string> Unrestorable { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Entfernte Kopien – gefüllt, wenn das Rückgängig einen Kopierlauf zurücknimmt.</summary>
    public List<string> Discarded { get; } = [];

    /// <summary>Kopien, die als „inzwischen verändert" gelten und nicht entfernt werden dürfen.</summary>
    public HashSet<string> Undiscardable { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool LastDryRun { get; private set; }

    /// <summary>Die Betriebsart des zuletzt angewendeten Vorschlags.</summary>
    public FileOperationMode LastOperation { get; private set; }

    public Task<string> ApplyAsync(
        SortProposal proposal,
        FileOperationMode operation,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        Applied.Add(proposal);
        LastDryRun = dryRun;
        LastOperation = operation;
        return Task.FromResult(Path.Combine(proposal.TargetFolderPath, proposal.Photo.FileName));
    }

    public Task<bool> RestoreAsync(string currentPath, string originalPath, CancellationToken cancellationToken)
    {
        if (Unrestorable.Contains(currentPath))
        {
            return Task.FromResult(false);
        }

        Restored.Add((currentPath, originalPath));
        return Task.FromResult(true);
    }

    public Task<bool> DiscardCopyAsync(
        string copyPath,
        long? expectedLength,
        DateTime? expectedLastWriteUtc,
        CancellationToken cancellationToken)
    {
        if (Undiscardable.Contains(copyPath) || expectedLength is null || expectedLastWriteUtc is null)
        {
            return Task.FromResult(false);
        }

        Discarded.Add(copyPath);
        return Task.FromResult(true);
    }

    public Task RemoveFolderIfEmptyAsync(string folderPath, CancellationToken cancellationToken)
    {
        CheckedFolders.Add(folderPath);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Verschiebt Dateien wie <see cref="FakeFileOrganizer"/>, wirft aber für eine
/// bestimmte Datei eine <see cref="IOException"/> – simuliert eine gesperrte Datei.
/// </summary>
internal sealed class FailingFileOrganizer(string failOn) : IFileOrganizer
{
    public List<SortProposal> Applied { get; } = [];

    public Task<string> ApplyAsync(
        SortProposal proposal,
        FileOperationMode operation,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        if (string.Equals(proposal.Photo.FileName, failOn, StringComparison.Ordinal))
        {
            throw new IOException($"Die Datei {failOn} wird von einem anderen Prozess verwendet.");
        }

        Applied.Add(proposal);
        return Task.FromResult(Path.Combine(proposal.TargetFolderPath, proposal.Photo.FileName));
    }

    public Task<bool> RestoreAsync(string currentPath, string originalPath, CancellationToken cancellationToken)
    {
        if (string.Equals(Path.GetFileName(currentPath), failOn, StringComparison.Ordinal))
        {
            throw new IOException($"Die Datei {failOn} wird von einem anderen Prozess verwendet.");
        }

        return Task.FromResult(true);
    }

    public Task<bool> DiscardCopyAsync(
        string copyPath,
        long? expectedLength,
        DateTime? expectedLastWriteUtc,
        CancellationToken cancellationToken)
    {
        if (string.Equals(Path.GetFileName(copyPath), failOn, StringComparison.Ordinal))
        {
            throw new IOException($"Die Datei {failOn} wird von einem anderen Prozess verwendet.");
        }

        return Task.FromResult(true);
    }

    public Task RemoveFolderIfEmptyAsync(string folderPath, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}

/// <summary>
/// Verschiebt wie der <see cref="FakeFileOrganizer"/>, fordert aber nach der
/// angegebenen Anzahl Dateien den Abbruch an – so, wie die Nutzerin mitten im
/// Sortierlauf auf „Abbrechen" klickt.
/// </summary>
internal sealed class CancellingFileOrganizer(CancellationTokenSource cancellation, int cancelAfter)
    : IFileOrganizer
{
    public List<SortProposal> Applied { get; } = [];

    public async Task<string> ApplyAsync(
        SortProposal proposal,
        FileOperationMode operation,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        Applied.Add(proposal);
        if (Applied.Count >= cancelAfter)
        {
            await cancellation.CancelAsync().ConfigureAwait(false);
        }

        return Path.Combine(proposal.TargetFolderPath, proposal.Photo.FileName);
    }

    public Task<bool> RestoreAsync(string currentPath, string originalPath, CancellationToken cancellationToken) =>
        Task.FromResult(true);

    public Task<bool> DiscardCopyAsync(
        string copyPath,
        long? expectedLength,
        DateTime? expectedLastWriteUtc,
        CancellationToken cancellationToken) => Task.FromResult(true);

    public Task RemoveFolderIfEmptyAsync(string folderPath, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}

/// <summary>Hält das Protokoll der Sortierläufe im Speicher.</summary>
internal sealed class FakeSortJournal : ISortJournal
{
    public List<SortRun> Runs { get; } = [];

    public HashSet<Guid> Undone { get; } = [];

    public Task RecordAsync(SortRun run, CancellationToken cancellationToken)
    {
        Runs.Add(run);
        return Task.CompletedTask;
    }

    public Task<SortRun?> GetLastUndoableAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Runs.FindLast(run => !Undone.Contains(run.Id)));

    public Task MarkUndoneAsync(Guid runId, CancellationToken cancellationToken)
    {
        _ = Undone.Add(runId);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Hält das Protokoll der Analyseläufe im Speicher und legt es offen.
///
/// Wie beim Gedächtnis läuft jeder Zugriff unter einem Schloss: Der Sortierdienst
/// bewertet mehrere Fotos gleichzeitig und schreibt aus mehreren Fäden ins Protokoll.
/// Ohne das Schloss beschriebe der Fake ein Verhalten, das es nicht gibt.
/// </summary>
internal sealed class FakeAnalysisJournal : IAnalysisJournal
{
    private readonly Lock _gate = new();
    private readonly List<AnalysisRun> _runs = [];
    private readonly Dictionary<Guid, List<AnalysisRunItem>> _items = [];

    /// <summary>Die angelegten Läufe in der Reihenfolge ihrer Anlage.</summary>
    public IReadOnlyList<AnalysisRun> Runs
    {
        get
        {
            lock (_gate)
            {
                return [.. _runs];
            }
        }
    }

    /// <summary>Die protokollierten Ergebnisse eines Laufs.</summary>
    /// <param name="runId">Kennung des Laufs.</param>
    /// <returns>Die Ergebnisse.</returns>
    public IReadOnlyList<AnalysisRunItem> ItemsOf(Guid runId)
    {
        lock (_gate)
        {
            return _items.TryGetValue(runId, out List<AnalysisRunItem>? items) ? [.. items] : [];
        }
    }

    public Task StartAsync(AnalysisRun run, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _runs.Add(run);
            _items[run.Id] = [];
        }

        return Task.CompletedTask;
    }

    public Task AppendAsync(
        Guid runId,
        IReadOnlyList<AnalysisRunItem> items,
        int totalPhotos,
        DateTimeOffset at,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!_items.TryGetValue(runId, out List<AnalysisRunItem>? stored))
            {
                return Task.CompletedTask;
            }

            stored.AddRange(items);
            Replace(runId, run => run with
            {
                LastProgressAt = at,
                TotalPhotos = totalPhotos > 0 ? totalPhotos : run.TotalPhotos,
                DecidedPhotos = stored.Count,
            });
        }

        return Task.CompletedTask;
    }

    public Task FinishAsync(
        Guid runId,
        AnalysisRunState state,
        string? failureReason,
        DateTimeOffset at,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            Replace(runId, run => run with
            {
                State = state,
                FailureReason = failureReason,
                FinishedAt = at,
                LastProgressAt = at,
            });
        }

        return Task.CompletedTask;
    }

    public Task<AnalysisRun?> GetLatestAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(_runs.Count == 0 ? null : _runs[^1]);
        }
    }

    public Task<IReadOnlyList<AnalysisRunItem>> GetItemsAsync(Guid runId, CancellationToken cancellationToken) =>
        Task.FromResult(ItemsOf(runId));

    public Task DiscardAsync(Guid runId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _ = _runs.RemoveAll(run => run.Id == runId);
            _ = _items.Remove(runId);
        }

        return Task.CompletedTask;
    }

    // Läuft stets unter dem Schloss des Aufrufers.
    private void Replace(Guid runId, Func<AnalysisRun, AnalysisRun> update)
    {
        int index = _runs.FindIndex(run => run.Id == runId);
        if (index >= 0)
        {
            _runs[index] = update(_runs[index]);
        }
    }
}

/// <summary>
/// Hält das Sortier-Gedächtnis im Speicher; legt die Einträge offen.
///
/// Jeder Zugriff läuft unter einem Schloss. Das echte Repository ist thread-sicher —
/// es erzeugt je Aufruf einen eigenen Datenbank-Kontext —, und der Sortierdienst
/// bewertet mehrere Fotos gleichzeitig. Ohne das Schloss beschriebe der Fake ein
/// Verhalten, das es nicht gibt: Die Liste geriete unter gleichzeitigem Zugriff
/// durcheinander, und der Test schlüge dort fehl, wo der Betrieb tadellos läuft.
/// </summary>
internal sealed class FakeSortMemory : ISortMemory
{
    private readonly Lock _gate = new();
    private readonly List<SortMemoryRecord> _records = [];

    /// <summary>Die gemerkten Einträge (Kopie; der Zugriff läuft unter dem Schloss).</summary>
    public IList<SortMemoryRecord> Records => _records;

    public Task<IReadOnlyList<SortMemoryRecord>> GetForFolderAsync(
        string folderPath,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<SortMemoryRecord>>(
                [.. _records.Where(record => record.FolderPath == folderPath)]);
        }
    }

    public Task<SortMemoryRecord?> GetAsync(
        string folderPath,
        string fileSignature,
        string categoryName,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(_records.Find(record =>
                record.FolderPath == folderPath
                && record.FileSignature == fileSignature
                && record.CategoryName == categoryName));
        }
    }

    public Task UpsertAsync(SortMemoryRecord record, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _ = _records.RemoveAll(existing =>
                existing.FolderPath == record.FolderPath
                && existing.FileSignature == record.FileSignature
                && existing.CategoryName == record.CategoryName);
            _records.Add(record);
        }

        return Task.CompletedTask;
    }

    public Task RemoveAsync(
        string folderPath,
        string fileSignature,
        string categoryName,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _ = _records.RemoveAll(record =>
                record.FolderPath == folderPath
                && record.FileSignature == fileSignature
                && record.CategoryName == categoryName);
        }

        return Task.CompletedTask;
    }

    public Task ClearFolderAsync(string folderPath, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _ = _records.RemoveAll(record => record.FolderPath == folderPath);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SortMemoryRecord>> GetAllAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<SortMemoryRecord>>([.. _records]);
        }
    }
}

/// <summary>Liefert eine feste Zeit, damit Zeitstempel im Test deterministisch sind.</summary>
internal sealed class FakeClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; } = now;
}
