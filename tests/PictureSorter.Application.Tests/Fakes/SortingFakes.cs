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
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Photo>>([.. photos.Skip(skip).Take(maxCount ?? int.MaxValue)]);
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
    public int CallCount { get; private set; }

    public Task<ImageEmbedding> CreateEmbeddingAsync(Photo photo, CancellationToken cancellationToken)
    {
        CallCount++;
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

/// <summary>Hält das Sortier-Gedächtnis im Speicher; legt die Einträge offen.</summary>
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

/// <summary>Liefert eine feste Zeit, damit Zeitstempel im Test deterministisch sind.</summary>
internal sealed class FakeClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; } = now;
}
