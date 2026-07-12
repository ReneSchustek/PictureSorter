using PictureSorter.Core.Entities;
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
        CancellationToken cancellationToken) => Task.FromResult(photos);
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

    public bool LastDryRun { get; private set; }

    public Task<string> ApplyAsync(SortProposal proposal, bool dryRun, CancellationToken cancellationToken)
    {
        Applied.Add(proposal);
        LastDryRun = dryRun;
        return Task.FromResult(Path.Combine(proposal.TargetFolderPath, proposal.Photo.FileName));
    }
}

/// <summary>
/// Verschiebt Dateien wie <see cref="FakeFileOrganizer"/>, wirft aber für eine
/// bestimmte Datei eine <see cref="IOException"/> – simuliert eine gesperrte Datei.
/// </summary>
internal sealed class FailingFileOrganizer(string failOn) : IFileOrganizer
{
    public List<SortProposal> Applied { get; } = [];

    public Task<string> ApplyAsync(SortProposal proposal, bool dryRun, CancellationToken cancellationToken)
    {
        if (string.Equals(proposal.Photo.FileName, failOn, StringComparison.Ordinal))
        {
            throw new IOException($"Die Datei {failOn} wird von einem anderen Prozess verwendet.");
        }

        Applied.Add(proposal);
        return Task.FromResult(Path.Combine(proposal.TargetFolderPath, proposal.Photo.FileName));
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
