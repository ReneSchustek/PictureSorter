using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PictureSorter.Application.Sorting;
using PictureSorter.Application.Tests.Fakes;
using PictureSorter.Core.Entities;
using PictureSorter.Core.Enums;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Application.Tests.Sorting;

/// <summary>
/// Tests der Sortier-Orchestrierung: Embedding-Vorsortierung, Vision-Grenzfälle und
/// das Zusammenspiel mit dem dauerhaften Sortier-Gedächtnis.
/// </summary>
public sealed class PhotoSortingServiceTests
{
    private const string SourceFolder = @"C:\fotos";

    private static readonly Photo SamplePhoto = new()
    {
        FullPath = @"C:\fotos\a.jpg",
        FileName = "a.jpg",
    };

    [Fact]
    public async Task CreateProposalsAsync_HighSimilarity_AssignsViaEmbedding()
    {
        FakeImageClassifier classifier = new(new VisionVerdict { Matches = false, Confidence = 0.0 });
        PhotoSortingService service = CreateService(embedding: [1.0f, 0.0f, 0.0f], classifier: classifier);

        IReadOnlyList<SortProposal> proposals = await service.CreateProposalsAsync(
            SourceFolder, CreateCategory(), includeSubfolders: false, progress: null, CancellationToken.None);

        SortProposal proposal = Assert.Single(proposals);
        Assert.Equal(ClassificationMethod.Embedding, proposal.Method);
        Assert.Equal(SourceFolder, proposal.SourceFolder);
        Assert.Equal(0, classifier.CallCount);
    }

    [Fact]
    public async Task CreateProposalsAsync_LowSimilarity_SkipsPhotoWithoutVision()
    {
        FakeImageClassifier classifier = new(new VisionVerdict { Matches = true, Confidence = 1.0 });
        PhotoSortingService service = CreateService(embedding: [0.0f, 1.0f, 0.0f], classifier: classifier);

        IReadOnlyList<SortProposal> proposals = await service.CreateProposalsAsync(
            SourceFolder, CreateCategory(), includeSubfolders: false, progress: null, CancellationToken.None);

        Assert.Empty(proposals);
        Assert.Equal(0, classifier.CallCount);
    }

    [Fact]
    public async Task CreateProposalsAsync_BorderlineSimilarity_UsesVisionVerdict()
    {
        FakeImageClassifier classifier = new(new VisionVerdict { Matches = true, Confidence = 0.9 });
        PhotoSortingService service = CreateService(embedding: [1.0f, 1.0f, 0.0f], classifier: classifier);

        IReadOnlyList<SortProposal> proposals = await service.CreateProposalsAsync(
            SourceFolder, CreateCategory(), includeSubfolders: false, progress: null, CancellationToken.None);

        SortProposal proposal = Assert.Single(proposals);
        Assert.Equal(ClassificationMethod.VisionModel, proposal.Method);
        Assert.Equal(1, classifier.CallCount);
    }

    [Fact]
    public async Task CreateProposalsAsync_WithoutPositiveExamples_ReturnsEmpty()
    {
        FakeImageClassifier classifier = new(new VisionVerdict { Matches = true, Confidence = 1.0 });
        PhotoSortingService service = CreateService(embedding: [1.0f, 0.0f, 0.0f], classifier: classifier);
        Category emptyCategory = new("Familie", "ohne Beispiele", CategoryKind.Topic);

        IReadOnlyList<SortProposal> proposals = await service.CreateProposalsAsync(
            SourceFolder, emptyCategory, includeSubfolders: false, progress: null, CancellationToken.None);

        Assert.Empty(proposals);
    }

    [Fact]
    public async Task CreateProposalsAsync_WithSortedPhotoInMemory_SkipsPhotoEntirely()
    {
        FakeSortMemory memory = new();
        await memory.UpsertAsync(
            CreateMemory(SortMemoryStatus.Sorted),
            CancellationToken.None);

        CountingEmbeddingProvider provider = new([1.0f, 0.0f, 0.0f]);
        PhotoSortingService service = CreateService(
            embedding: [1.0f, 0.0f, 0.0f],
            classifier: new FakeImageClassifier(new VisionVerdict { Matches = true, Confidence = 1.0 }),
            memory: memory,
            embeddingProvider: provider);

        IReadOnlyList<SortProposal> proposals = await service.CreateProposalsAsync(
            SourceFolder, CreateCategory(), includeSubfolders: false, progress: null, CancellationToken.None);

        // Der teure KI-Aufruf muss ausbleiben – genau dafür gibt es das Gedächtnis.
        Assert.Empty(proposals);
        Assert.Equal(0, provider.CallCount);
    }

    [Fact]
    public async Task CreateProposalsAsync_WithRejectedPhotoInMemory_DoesNotAskVisionAgain()
    {
        FakeSortMemory memory = new();
        await memory.UpsertAsync(CreateMemory(SortMemoryStatus.Rejected), CancellationToken.None);

        FakeImageClassifier classifier = new(new VisionVerdict { Matches = true, Confidence = 1.0 });
        PhotoSortingService service = CreateService(
            embedding: [1.0f, 1.0f, 0.0f],
            classifier: classifier,
            memory: memory);

        IReadOnlyList<SortProposal> proposals = await service.CreateProposalsAsync(
            SourceFolder, CreateCategory(), includeSubfolders: false, progress: null, CancellationToken.None);

        Assert.Empty(proposals);
        Assert.Equal(0, classifier.CallCount);
    }

    [Fact]
    public async Task CreateProposalsAsync_WhenRejectedByAi_RemembersRejection()
    {
        FakeSortMemory memory = new();
        PhotoSortingService service = CreateService(
            embedding: [0.0f, 1.0f, 0.0f],
            classifier: new FakeImageClassifier(new VisionVerdict { Matches = false, Confidence = 0.0 }),
            memory: memory);

        _ = await service.CreateProposalsAsync(
            SourceFolder, CreateCategory(), includeSubfolders: false, progress: null, CancellationToken.None);

        SortMemoryRecord remembered = Assert.Single(memory.Records);
        Assert.Equal(SortMemoryStatus.Rejected, remembered.Status);
    }

    [Fact]
    public async Task CreateProposalsAsync_WhenAiUnavailable_RemembersNothing()
    {
        FakeSortMemory memory = new();
        PhotoSortingService service = CreateService(
            embedding: [1.0f, 0.0f, 0.0f],
            classifier: new FakeImageClassifier(new VisionVerdict { Matches = false, Confidence = 0.0 }),
            memory: memory,
            embeddingProvider: new ThrowingEmbeddingProvider());

        IReadOnlyList<SortProposal> proposals = await service.CreateProposalsAsync(
            SourceFolder, CreateCategory(), includeSubfolders: false, progress: null, CancellationToken.None);

        // Ein KI-Ausfall ist kein Urteil über das Bild: nichts merken, sonst gilt das
        // Foto künftig fälschlich als „gehört nicht dazu".
        Assert.Empty(proposals);
        Assert.Empty(memory.Records);
    }

    [Fact]
    public async Task ApplyProposalsAsync_AppliesEachProposal_ReturnsCount()
    {
        FakeFileOrganizer organizer = new();
        PhotoSortingService service = CreateService(
            embedding: [1.0f, 0.0f, 0.0f],
            classifier: new FakeImageClassifier(new VisionVerdict { Matches = false, Confidence = 0.0 }),
            organizer: organizer);

        int applied = await service.ApplyProposalsAsync([CreateProposal()], dryRun: true, CancellationToken.None);

        Assert.Equal(1, applied);
        _ = Assert.Single(organizer.Applied);
        Assert.True(organizer.LastDryRun);
    }

    [Fact]
    public async Task ApplyProposalsAsync_WithDryRun_DoesNotMarkAsSorted()
    {
        FakeSortMemory memory = new();
        PhotoSortingService service = CreateService(
            embedding: [1.0f, 0.0f, 0.0f],
            classifier: new FakeImageClassifier(new VisionVerdict { Matches = false, Confidence = 0.0 }),
            memory: memory);

        _ = await service.ApplyProposalsAsync([CreateProposal()], dryRun: true, CancellationToken.None);

        // Im Probelauf wird nichts verschoben, also darf auch nichts als erledigt gelten.
        Assert.Empty(memory.Records);
    }

    [Fact]
    public async Task ApplyProposalsAsync_WhenApplied_MarksPhotoAsSorted()
    {
        FakeSortMemory memory = new();
        PhotoSortingService service = CreateService(
            embedding: [1.0f, 0.0f, 0.0f],
            classifier: new FakeImageClassifier(new VisionVerdict { Matches = false, Confidence = 0.0 }),
            memory: memory);

        _ = await service.ApplyProposalsAsync([CreateProposal()], dryRun: false, CancellationToken.None);

        SortMemoryRecord remembered = Assert.Single(memory.Records);
        Assert.Equal(SortMemoryStatus.Sorted, remembered.Status);
    }

    [Fact]
    public async Task ApplyProposalsAsync_WhenOneFileFails_ContinuesWithTheRest()
    {
        FakeSortMemory memory = new();
        FailingFileOrganizer organizer = new(failOn: "foto1.jpg");
        PhotoSortingService service = CreateService(
            embedding: [1.0f, 0.0f, 0.0f],
            classifier: new FakeImageClassifier(new VisionVerdict { Matches = false, Confidence = 0.0 }),
            organizer: organizer,
            memory: memory);

        SortProposal[] proposals =
        [
            CreateProposal("foto0.jpg"),
            CreateProposal("foto1.jpg"),
            CreateProposal("foto2.jpg"),
        ];

        int applied = await service.ApplyProposalsAsync(proposals, dryRun: false, CancellationToken.None);

        // Eine gesperrte Datei darf den Lauf nicht abbrechen: die übrigen werden
        // verschoben, die fehlgeschlagene bleibt ungemerkt und kommt wieder.
        Assert.Equal(2, applied);
        Assert.Equal(2, memory.Records.Count);
        Assert.DoesNotContain(memory.Records, record => record.PhotoPath.EndsWith("foto1.jpg", StringComparison.Ordinal));
    }

    [Fact]
    public async Task IgnoreProposalsAsync_MarksProposalsAsIgnored()
    {
        FakeSortMemory memory = new();
        PhotoSortingService service = CreateService(
            embedding: [1.0f, 0.0f, 0.0f],
            classifier: new FakeImageClassifier(new VisionVerdict { Matches = false, Confidence = 0.0 }),
            memory: memory);

        await service.IgnoreProposalsAsync([CreateProposal()], CancellationToken.None);

        SortMemoryRecord remembered = Assert.Single(memory.Records);
        Assert.Equal(SortMemoryStatus.Ignored, remembered.Status);
    }

    private static SortProposal CreateProposal() => CreateProposal(SamplePhoto.FileName);

    private static SortProposal CreateProposal(string fileName) => new()
    {
        Photo = new Photo
        {
            FullPath = Path.Combine(SourceFolder, fileName),
            FileName = fileName,
        },
        CategoryName = "Familie",
        SourceFolder = SourceFolder,
        TargetFolderPath = @"C:\fotos\Familie",
        Confidence = 1.0,
        Method = ClassificationMethod.Embedding,
    };

    private static SortMemoryRecord CreateMemory(SortMemoryStatus status) => new()
    {
        FolderPath = SourceFolder,
        FileSignature = SamplePhoto.ComputeSignature(),
        PhotoPath = SamplePhoto.FullPath,
        CategoryName = "Familie",
        Status = status,
        Confidence = 1.0,
        UpdatedAt = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero),
    };

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
        IFileOrganizer? organizer = null,
        FakeSortMemory? memory = null,
        IEmbeddingProvider? embeddingProvider = null)
    {
        FakePhotoSource photoSource = new([SamplePhoto]);
        IOptions<SortingOptions> options = Options.Create(new SortingOptions());

        SortMemoryGateway gateway = new(
            memory ?? new FakeSortMemory(),
            new FakeClock(new DateTimeOffset(2026, 7, 1, 8, 0, 0, TimeSpan.Zero)),
            NullLogger<SortMemoryGateway>.Instance);

        return new PhotoSortingService(
            photoSource,
            embeddingProvider ?? new FakeEmbeddingProvider(_ => embedding),
            classifier,
            organizer ?? new FakeFileOrganizer(),
            gateway,
            options,
            NullLogger<PhotoSortingService>.Instance);
    }
}
