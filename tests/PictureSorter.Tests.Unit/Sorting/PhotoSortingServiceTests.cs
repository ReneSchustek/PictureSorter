using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PictureSorter.Application.Sorting;
using PictureSorter.Core.Entities;
using PictureSorter.Core.Enums;
using PictureSorter.Core.ValueObjects;
using PictureSorter.Tests.Unit.Fakes;

namespace PictureSorter.Tests.Unit.Sorting;

/// <summary>
/// Tests der Sortier-Orchestrierung (Embedding-Vorsortierung und Vision-Grenzfälle).
/// </summary>
public sealed class PhotoSortingServiceTests
{
    private static readonly Photo SamplePhoto = new()
    {
        FullPath = @"C:\fotos\a.jpg",
        FileName = "a.jpg",
    };

    [Fact]
    public async Task CreateProposalsAsync_HighSimilarity_AssignsViaEmbedding()
    {
        FakeImageClassifier classifier = new(new VisionVerdict { Matches = false, Confidence = 0.0 });
        PhotoSortingService service = CreateService(
            embedding: [1.0f, 0.0f, 0.0f],
            classifier: classifier);

        IReadOnlyList<SortProposal> proposals =
            await service.CreateProposalsAsync(@"C:\fotos", CreateCategory(), includeSubfolders: false, progress: null, CancellationToken.None);

        SortProposal proposal = Assert.Single(proposals);
        Assert.Equal(ClassificationMethod.Embedding, proposal.Method);
        Assert.Equal(0, classifier.CallCount);
    }

    [Fact]
    public async Task CreateProposalsAsync_LowSimilarity_SkipsPhotoWithoutVision()
    {
        FakeImageClassifier classifier = new(new VisionVerdict { Matches = true, Confidence = 1.0 });
        PhotoSortingService service = CreateService(
            embedding: [0.0f, 1.0f, 0.0f],
            classifier: classifier);

        IReadOnlyList<SortProposal> proposals =
            await service.CreateProposalsAsync(@"C:\fotos", CreateCategory(), includeSubfolders: false, progress: null, CancellationToken.None);

        Assert.Empty(proposals);
        Assert.Equal(0, classifier.CallCount);
    }

    [Fact]
    public async Task CreateProposalsAsync_BorderlineSimilarity_UsesVisionVerdict()
    {
        FakeImageClassifier classifier = new(new VisionVerdict { Matches = true, Confidence = 0.9 });
        PhotoSortingService service = CreateService(
            embedding: [1.0f, 1.0f, 0.0f],
            classifier: classifier);

        IReadOnlyList<SortProposal> proposals =
            await service.CreateProposalsAsync(@"C:\fotos", CreateCategory(), includeSubfolders: false, progress: null, CancellationToken.None);

        SortProposal proposal = Assert.Single(proposals);
        Assert.Equal(ClassificationMethod.VisionModel, proposal.Method);
        Assert.Equal(1, classifier.CallCount);
    }

    [Fact]
    public async Task CreateProposalsAsync_WithoutPositiveExamples_ReturnsEmpty()
    {
        FakeImageClassifier classifier = new(new VisionVerdict { Matches = true, Confidence = 1.0 });
        PhotoSortingService service = CreateService(
            embedding: [1.0f, 0.0f, 0.0f],
            classifier: classifier);
        Category emptyCategory = new("Familie", "ohne Beispiele", CategoryKind.Topic);

        IReadOnlyList<SortProposal> proposals =
            await service.CreateProposalsAsync(@"C:\fotos", emptyCategory, includeSubfolders: false, progress: null, CancellationToken.None);

        Assert.Empty(proposals);
    }

    [Fact]
    public async Task ApplyProposalsAsync_AppliesEachProposal_ReturnsCount()
    {
        FakeFileOrganizer organizer = new();
        PhotoSortingService service = CreateService(
            embedding: [1.0f, 0.0f, 0.0f],
            classifier: new FakeImageClassifier(new VisionVerdict { Matches = false, Confidence = 0.0 }),
            organizer: organizer);
        SortProposal[] proposals =
        [
            new()
            {
                Photo = SamplePhoto,
                CategoryName = "Familie",
                TargetFolderPath = @"C:\fotos\Familie",
                Confidence = 1.0,
                Method = ClassificationMethod.Embedding,
            },
        ];

        int applied = await service.ApplyProposalsAsync(proposals, dryRun: true, CancellationToken.None);

        Assert.Equal(1, applied);
        _ = Assert.Single(organizer.Applied);
        Assert.True(organizer.LastDryRun);
    }

    private static Category CreateCategory()
    {
        Category category = new("Familie", "Bilder meiner Familie", CategoryKind.Topic);
        category.AddExample(new CategoryExample
        {
            PhotoPath = @"C:\fotos\beispiel.jpg",
            IsPositive = true,
            Embedding = new ImageEmbedding([1.0f, 0.0f, 0.0f], "fake"),
        });
        return category;
    }

    private static PhotoSortingService CreateService(
        float[] embedding,
        FakeImageClassifier classifier,
        FakeFileOrganizer? organizer = null)
    {
        FakePhotoSource photoSource = new([SamplePhoto]);
        FakeEmbeddingProvider embeddingProvider = new(_ => embedding);
        IOptions<SortingOptions> options = Options.Create(new SortingOptions());

        return new PhotoSortingService(
            photoSource,
            embeddingProvider,
            classifier,
            organizer ?? new FakeFileOrganizer(),
            options,
            NullLogger<PhotoSortingService>.Instance);
    }
}
