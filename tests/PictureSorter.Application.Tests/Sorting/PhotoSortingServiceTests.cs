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

        int applied = await service.ApplyProposalsAsync([CreateProposal()], FileOperationMode.Move, dryRun: true, CancellationToken.None);

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

        _ = await service.ApplyProposalsAsync([CreateProposal()], FileOperationMode.Move, dryRun: true, CancellationToken.None);

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

        _ = await service.ApplyProposalsAsync([CreateProposal()], FileOperationMode.Move, dryRun: false, CancellationToken.None);

        SortMemoryRecord remembered = Assert.Single(memory.Records);
        Assert.Equal(SortMemoryStatus.Sorted, remembered.Status);
    }

    [Fact]
    public async Task ApplyProposalsAsync_RecordsEveryMoveWithSourceAndTarget()
    {
        // Ohne dieses Protokoll wäre der Lauf nicht umkehrbar: Nach dem Verschieben
        // ist nirgends mehr festgehalten, wo eine Datei vorher lag.
        FakeSortJournal journal = new();
        PhotoSortingService service = CreateService(
            embedding: [1.0f, 0.0f, 0.0f],
            classifier: new FakeImageClassifier(new VisionVerdict { Matches = false, Confidence = 0.0 }),
            journal: journal);

        _ = await service.ApplyProposalsAsync([CreateProposal()], FileOperationMode.Move, dryRun: false, CancellationToken.None);

        SortRun run = Assert.Single(journal.Runs);
        Assert.Equal(SourceFolder, run.SourceFolder);
        Assert.Equal("Familie", run.CategoryName);

        SortRunItem item = Assert.Single(run.Items);
        Assert.Equal(@"C:\fotos\a.jpg", item.SourcePath);
        Assert.Equal(Path.Combine(SourceFolder, "Familie", "a.jpg"), item.TargetPath);
        Assert.NotEmpty(item.FileSignature);
    }

    [Fact]
    public async Task ApplyProposalsAsync_RecordsTheOperationOfTheRun()
    {
        // Ohne die Betriebsart wüsste das Rückgängigmachen nicht, ob es die Datei
        // zurückholen oder eine Kopie entfernen muss – und träfe im Zweifel die
        // gefährliche Annahme.
        FakeSortJournal journal = new();
        PhotoSortingService service = CreateService(
            embedding: [1.0f, 0.0f, 0.0f],
            classifier: new FakeImageClassifier(new VisionVerdict { Matches = false, Confidence = 0.0 }),
            journal: journal);

        _ = await service.ApplyProposalsAsync([CreateProposal()], FileOperationMode.Copy, dryRun: false, CancellationToken.None);

        Assert.Equal(FileOperationMode.Copy, Assert.Single(journal.Runs).Operation);
    }

    [Fact]
    public async Task ApplyProposalsAsync_PassesTheOperationToTheOrganizer()
    {
        FakeFileOrganizer organizer = new();
        PhotoSortingService service = CreateService(
            embedding: [1.0f, 0.0f, 0.0f],
            classifier: new FakeImageClassifier(new VisionVerdict { Matches = false, Confidence = 0.0 }),
            organizer: organizer);

        _ = await service.ApplyProposalsAsync([CreateProposal()], FileOperationMode.Copy, dryRun: false, CancellationToken.None);

        Assert.Equal(FileOperationMode.Copy, organizer.LastOperation);
    }

    [Fact]
    public async Task ApplyProposalsAsync_WithDryRun_RecordsNothing()
    {
        FakeSortJournal journal = new();
        PhotoSortingService service = CreateService(
            embedding: [1.0f, 0.0f, 0.0f],
            classifier: new FakeImageClassifier(new VisionVerdict { Matches = false, Confidence = 0.0 }),
            journal: journal);

        _ = await service.ApplyProposalsAsync([CreateProposal()], FileOperationMode.Move, dryRun: true, CancellationToken.None);

        // Im Probelauf bewegt sich nichts – es gäbe nichts zurückzunehmen.
        Assert.Empty(journal.Runs);
    }

    [Fact]
    public async Task ApplyProposalsAsync_WhenOneFileFails_RecordsOnlyTheMovedOnes()
    {
        // Eine Datei, die gar nicht verschoben wurde, darf nicht im Protokoll landen –
        // ein Rückgängig würde sonst versuchen, sie von einem Ort zurückzuholen, an
        // dem sie nie war.
        FakeSortJournal journal = new();
        PhotoSortingService service = CreateService(
            embedding: [1.0f, 0.0f, 0.0f],
            classifier: new FakeImageClassifier(new VisionVerdict { Matches = false, Confidence = 0.0 }),
            organizer: new FailingFileOrganizer(failOn: "foto1.jpg"),
            journal: journal);

        _ = await service.ApplyProposalsAsync(
            [CreateProposal("foto0.jpg"), CreateProposal("foto1.jpg"), CreateProposal("foto2.jpg")],
            FileOperationMode.Move,
            dryRun: false,
            CancellationToken.None);

        SortRun run = Assert.Single(journal.Runs);
        Assert.Equal(2, run.Items.Count);
        Assert.DoesNotContain(run.Items, item => item.SourcePath.EndsWith("foto1.jpg", StringComparison.Ordinal));
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

        int applied = await service.ApplyProposalsAsync(proposals, FileOperationMode.Move, dryRun: false, CancellationToken.None);

        // Eine gesperrte Datei darf den Lauf nicht abbrechen: die übrigen werden
        // verschoben, die fehlgeschlagene bleibt ungemerkt und kommt wieder.
        Assert.Equal(2, applied);
        Assert.Equal(2, memory.Records.Count);
        Assert.DoesNotContain(memory.Records, record => record.PhotoPath.EndsWith("foto1.jpg", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ApplyProposalsAsync_WhenCancelledMidRun_RecordsWhatWasAlreadyMoved()
    {
        // Bricht die Nutzerin ab, sind die bis dahin verschobenen Fotos längst im
        // Zielordner und im Gedächtnis als einsortiert vermerkt. Ohne Protokoll gäbe
        // es für sie keinen Weg zurück – ausgerechnet nach einem Abbruch, wo sie ihn
        // am ehesten sucht.
        using CancellationTokenSource cancellation = new();
        FakeSortJournal journal = new();
        PhotoSortingService service = CreateService(
            embedding: [1.0f, 0.0f, 0.0f],
            classifier: new FakeImageClassifier(new VisionVerdict { Matches = false, Confidence = 0.0 }),
            organizer: new CancellingFileOrganizer(cancellation, cancelAfter: 2),
            journal: journal);

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ApplyProposalsAsync(
            [CreateProposal("foto0.jpg"), CreateProposal("foto1.jpg"), CreateProposal("foto2.jpg")],
            FileOperationMode.Move,
            dryRun: false,
            cancellation.Token));

        SortRun run = Assert.Single(journal.Runs);
        Assert.Equal(2, run.Items.Count);
        Assert.DoesNotContain(run.Items, item => item.SourcePath.EndsWith("foto2.jpg", StringComparison.Ordinal));
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

    [Fact]
    public async Task ApplyProposalsAsync_WithMixedFoldersOrCategories_IsRejected()
    {
        // Der Lauf wird als ein Protokolleintrag festgehalten – mit einem Quellordner
        // und einer Kategorie. Käme eine gemischte Liste durch, stünde im Protokoll die
        // Angabe des ersten Vorschlags für alle, und das Zurücknehmen liefe ins Leere.
        PhotoSortingService service = CreateService([1.0f, 0.0f, 0.0f], new FakeImageClassifier(new VisionVerdict { Matches = true, Confidence = 0.9 }));
        SortProposal fromAnotherFolder = CreateProposal("b.jpg") with { SourceFolder = @"C:\andere" };
        SortProposal anotherCategory = CreateProposal("c.jpg") with { CategoryName = "Urlaub" };

        _ = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyProposalsAsync(
            [CreateProposal(), fromAnotherFolder],
            FileOperationMode.Move,
            dryRun: false,
            TestContext.Current.CancellationToken));

        _ = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyProposalsAsync(
            [CreateProposal(), anotherCategory],
            FileOperationMode.Move,
            dryRun: false,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CreateProposalsAsync_ForAnEventCategory_PutsTheCaptureDateIntoTheFolderName()
    {
        // Bei einem Ereignis ist das Datum der eigentliche Ordnername – „Geburtstag"
        // allein hilft nicht, wenn es davon mehrere gibt.
        Photo captured = new()
        {
            FullPath = @"C:\fotos\fest.jpg",
            FileName = "fest.jpg",
            CapturedAt = new DateTimeOffset(2026, 5, 17, 15, 30, 0, TimeSpan.Zero),
        };

        Category eventCategory = new("Geburtstag", "Bilder der Feier", CategoryKind.Event);
        eventCategory.AddExample(new CategoryExample
        {
            PhotoPath = @"C:\fotos\beispiel.jpg",
            IsPositive = true,
            Embedding = new ImageEmbedding([1.0f, 0.0f, 0.0f], "fake"),
        });

        PhotoSortingService service = CreateService(
            [1.0f, 0.0f, 0.0f],
            new FakeImageClassifier(new VisionVerdict { Matches = true, Confidence = 0.9 }),
            photos: [captured]);

        IReadOnlyList<SortProposal> proposals = await service.CreateProposalsAsync(
            SourceFolder, eventCategory, includeSubfolders: false, progress: null,
            TestContext.Current.CancellationToken);

        SortProposal proposal = Assert.Single(proposals);
        Assert.Equal(Path.Combine(SourceFolder, "Geburtstag 17.05.26"), proposal.TargetFolderPath);
    }

    [Fact]
    public async Task CreateProposalsAsync_ReportsProgressAlsoForSettledPhotos()
    {
        // Ein bereits abgehaktes Foto wird übersprungen – der Zählstand muss trotzdem
        // weiterlaufen, sonst bliebe der Fortschrittsbalken bei einem Ordner voller
        // bekannter Bilder scheinbar stehen.
        FakeSortMemory memory = new();
        memory.Records.Add(CreateMemory(SortMemoryStatus.Sorted));
        PhotoSortingService service = CreateService(
            [1.0f, 0.0f, 0.0f],
            new FakeImageClassifier(new VisionVerdict { Matches = true, Confidence = 0.9 }),
            memory: memory);

        // Bewusst kein Progress<T>: Das meldet über den Synchronisationskontext und
        // damit nicht zwingend vor der Rückkehr – der Test wäre zeitabhängig.
        CollectingProgress reported = new();
        IReadOnlyList<SortProposal> proposals = await service.CreateProposalsAsync(
            SourceFolder, CreateCategory(), includeSubfolders: false, reported,
            TestContext.Current.CancellationToken);

        Assert.Empty(proposals);
        Assert.Equal(new SortProgress(1, 1), reported.Reports[^1]);
    }

    /// <summary>Nimmt Fortschrittsmeldungen unmittelbar entgegen.</summary>
    private sealed class CollectingProgress : IProgress<SortProgress>
    {
        public List<SortProgress> Reports { get; } = [];

        public void Report(SortProgress value) => Reports.Add(value);
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

    [Fact]
    public async Task CreateProposalsAsync_WhenCloserToACounterExample_DoesNotAssign()
    {
        // Die Gegenbeispiele wurden zwar erfasst und gespeichert, aber nie ausgewertet:
        // Jede Markierung „passt nicht" blieb ohne Wirkung. Ein Foto, das einem
        // Gegenbeispiel ähnlicher ist als jedem Beispiel, gehört nicht in die Gruppe.
        Category category = CreateCategory();
        category.AddExample(new CategoryExample
        {
            PhotoPath = @"C:\fotos\gegenbeispiel.jpg",
            IsPositive = false,
            Embedding = new ImageEmbedding([0.0f, 1.0f, 0.0f], "fake"),
        });

        FakeImageClassifier classifier = new(new VisionVerdict { Matches = true, Confidence = 1.0 });
        PhotoSortingService service = CreateService(embedding: [0.0f, 1.0f, 0.0f], classifier: classifier);

        IReadOnlyList<SortProposal> proposals = await service.CreateProposalsAsync(
            SourceFolder, category, includeSubfolders: false, progress: null, CancellationToken.None);

        Assert.Empty(proposals);
        Assert.Equal(0, classifier.CallCount);
    }

    [Fact]
    public async Task CreateProposalsAsync_WhenCloserToAnExample_StillAssigns()
    {
        // Gegenprobe: Ein Gegenbeispiel darf nicht pauschal blockieren, sondern nur,
        // wenn das Foto ihm tatsächlich näher steht.
        Category category = CreateCategory();
        category.AddExample(new CategoryExample
        {
            PhotoPath = @"C:\fotos\gegenbeispiel.jpg",
            IsPositive = false,
            Embedding = new ImageEmbedding([0.0f, 1.0f, 0.0f], "fake"),
        });

        FakeImageClassifier classifier = new(new VisionVerdict { Matches = false, Confidence = 0.0 });
        PhotoSortingService service = CreateService(embedding: [1.0f, 0.0f, 0.0f], classifier: classifier);

        IReadOnlyList<SortProposal> proposals = await service.CreateProposalsAsync(
            SourceFolder, category, includeSubfolders: false, progress: null, CancellationToken.None);

        _ = Assert.Single(proposals);
    }

    [Fact]
    public async Task CreateProposalsAsync_WithExamplesFromAnotherModel_MakesNoProposals()
    {
        // Die gespeicherten Beispiele stammen aus dem Modell „fake", die frisch
        // erzeugten Vektoren aus einem anderen. Sie sind nicht vergleichbar – jedes
        // Urteil daraus wäre geraten. Vorher wurde trotzdem gerechnet, solange nur die
        // Vektorlänge zufällig übereinstimmte.
        FakeImageClassifier classifier = new(new VisionVerdict { Matches = true, Confidence = 1.0 });
        PhotoSortingService service = CreateService(
            embedding: [1.0f, 0.0f, 0.0f],
            classifier: classifier,
            embeddingProvider: new FakeEmbeddingProvider(_ => [1.0f, 0.0f, 0.0f], model: "anderes-modell"));

        IReadOnlyList<SortProposal> proposals = await service.CreateProposalsAsync(
            SourceFolder, CreateCategory(), includeSubfolders: false, progress: null,
            TestContext.Current.CancellationToken);

        Assert.Empty(proposals);
        Assert.Equal(0, classifier.CallCount);
    }

    [Fact]
    public async Task CreateProposalsAsync_WithExamplesFromAnotherModel_RemembersNothing()
    {
        // Der eigentliche Schaden lag im Gedächtnis: Ein nicht vergleichbares Beispiel
        // führte zu „passt nicht", und das wurde dauerhaft gemerkt. Der ganze Ordner
        // wäre danach als erledigt abgehakt gewesen – auch nachdem das Modell wieder
        // zurückgestellt ist, käme kein Foto je erneut zur Prüfung.
        FakeSortMemory memory = new();
        FakeImageClassifier classifier = new(new VisionVerdict { Matches = false, Confidence = 0.0 });
        PhotoSortingService service = CreateService(
            embedding: [1.0f, 0.0f, 0.0f],
            classifier: classifier,
            memory: memory,
            embeddingProvider: new FakeEmbeddingProvider(_ => [1.0f, 0.0f, 0.0f], model: "anderes-modell"));

        _ = await service.CreateProposalsAsync(
            SourceFolder, CreateCategory(), includeSubfolders: false, progress: null,
            TestContext.Current.CancellationToken);

        Assert.Empty(memory.Records);
    }

    [Fact]
    public async Task CreateProposalsAsync_WithExamplesOfDifferentLength_RemembersNothing()
    {
        // Derselbe Fall, andere Ursache: gleiches Modell, aber die gespeicherten
        // Vektoren haben eine andere Länge. Auch das ist kein Urteil über das Bild.
        FakeSortMemory memory = new();
        FakeImageClassifier classifier = new(new VisionVerdict { Matches = false, Confidence = 0.0 });
        PhotoSortingService service = CreateService(
            embedding: [1.0f, 0.0f],
            classifier: classifier,
            memory: memory,
            embeddingProvider: new FakeEmbeddingProvider(_ => [1.0f, 0.0f]));

        _ = await service.CreateProposalsAsync(
            SourceFolder, CreateCategory(), includeSubfolders: false, progress: null,
            TestContext.Current.CancellationToken);

        Assert.Empty(memory.Records);
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
        IFileOrganizer? organizer = null,
        FakeSortMemory? memory = null,
        IEmbeddingProvider? embeddingProvider = null,
        FakeSortJournal? journal = null,
        IReadOnlyList<Photo>? photos = null)
    {
        FakePhotoSource photoSource = new(photos ?? [SamplePhoto]);
        IOptions<SortingOptions> options = Options.Create(new SortingOptions());
        FakeClock clock = new(new DateTimeOffset(2026, 7, 1, 8, 0, 0, TimeSpan.Zero));

        SortMemoryGateway gateway = new(
            memory ?? new FakeSortMemory(),
            clock,
            NullLogger<SortMemoryGateway>.Instance);

        SortJournalGateway journalGateway = new(
            journal ?? new FakeSortJournal(),
            NullLogger<SortJournalGateway>.Instance);

        return new PhotoSortingService(
            photoSource,
            embeddingProvider ?? new FakeEmbeddingProvider(_ => embedding),
            classifier,
            organizer ?? new FakeFileOrganizer(),
            gateway,
            journalGateway,
            clock,
            options,
            NullLogger<PhotoSortingService>.Instance);
    }
}
