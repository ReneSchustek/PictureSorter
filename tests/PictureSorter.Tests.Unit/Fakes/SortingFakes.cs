using PictureSorter.Core.Entities;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Tests.Unit.Fakes;

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
