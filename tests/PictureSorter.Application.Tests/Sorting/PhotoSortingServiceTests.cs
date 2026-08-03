using System.Globalization;
using System.Runtime.CompilerServices;
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
            SourceFolder, CreateCategory(), includeSubfolders: false, DateRange.Unbounded, progress: null, CancellationToken.None);

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
            SourceFolder, CreateCategory(), includeSubfolders: false, DateRange.Unbounded, progress: null, CancellationToken.None);

        Assert.Empty(proposals);
        Assert.Equal(0, classifier.CallCount);
    }

    [Fact]
    public async Task CreateProposalsAsync_BorderlineSimilarity_UsesVisionVerdict()
    {
        FakeImageClassifier classifier = new(new VisionVerdict { Matches = true, Confidence = 0.9 });
        PhotoSortingService service = CreateService(embedding: [1.0f, 1.0f, 0.0f], classifier: classifier);

        IReadOnlyList<SortProposal> proposals = await service.CreateProposalsAsync(
            SourceFolder, CreateCategory(), includeSubfolders: false, DateRange.Unbounded, progress: null, CancellationToken.None);

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
            SourceFolder, emptyCategory, includeSubfolders: false, DateRange.Unbounded, progress: null, CancellationToken.None);

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
            SourceFolder, CreateCategory(), includeSubfolders: false, DateRange.Unbounded, progress: null, CancellationToken.None);

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
            SourceFolder, CreateCategory(), includeSubfolders: false, DateRange.Unbounded, progress: null, CancellationToken.None);

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
            SourceFolder, CreateCategory(), includeSubfolders: false, DateRange.Unbounded, progress: null, CancellationToken.None);

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
            SourceFolder, CreateCategory(), includeSubfolders: false, DateRange.Unbounded, progress: null, CancellationToken.None);

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
            SourceFolder, eventCategory, includeSubfolders: false, DateRange.Unbounded, progress: null,
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
            SourceFolder, CreateCategory(), includeSubfolders: false, DateRange.Unbounded, reported,
            TestContext.Current.CancellationToken);

        Assert.Empty(proposals);
        Assert.Equal(new SortProgress(1, 1), reported.Reports[^1]);
    }

    [Fact]
    public async Task CreateProposalsAsync_ReportsTheGatheringOfTheFilesFirst()
    {
        // Vor dem Bewerten wird jede Datei einmal geöffnet, um ihre Metadaten zu lesen.
        // Bei einem großen Ordner ist das der längste Teil des Laufs – und er lief bis
        // hierher ohne jede Meldung ab. In der Oberfläche stand deshalb minutenlang nur
        // „es arbeitet", und der Zählstand erschien erst, als dieser Teil vorbei war.
        PhotoSortingService service = CreateService(
            [1.0f, 0.0f, 0.0f],
            new FakeImageClassifier(new VisionVerdict { Matches = true, Confidence = 0.9 }));

        CollectingProgress reported = new();
        _ = await service.CreateProposalsAsync(
            SourceFolder, CreateCategory(), includeSubfolders: false, DateRange.Unbounded, reported,
            TestContext.Current.CancellationToken);

        List<SortProgress> reports = [.. reported.Reports];
        Assert.Contains(reports, report => report.Phase == ScanPhase.Gathering);
        Assert.Equal(ScanPhase.Gathering, reports[0].Phase);
        Assert.True(
            reports.FindIndex(report => report.Phase == ScanPhase.Gathering)
            < reports.FindIndex(report => report.Phase == ScanPhase.Analyzing),
            "Das Einlesen muss vor dem Bewerten gemeldet werden.");
    }

    [Fact]
    public async Task CreateProposalsAsync_WithDateRange_NeverAsksTheAiAboutPhotosOutside()
    {
        // Der eigentliche Zweck des Zeitraums: Er greift vor dem teuren Schritt. Wer einen
        // Urlaub sucht, lässt die KI über hundert statt über tausend Bilder laufen. Geprüft
        // wird deshalb die Zahl der KI-Aufrufe, nicht bloß das Ergebnis — bei einem
        // nachträglichen Filter wäre das Ergebnis dasselbe, die Wartezeit aber nicht.
        IReadOnlyList<Photo> photos =
        [
            Foto("vorher.jpg", new DateOnly(2026, 7, 10)),
            Foto("urlaub1.jpg", new DateOnly(2026, 7, 12)),
            Foto("urlaub2.jpg", new DateOnly(2026, 7, 26)),
            Foto("danach.jpg", new DateOnly(2026, 7, 27)),
        ];
        CountingEmbeddingProvider provider = new([1.0f, 0.0f, 0.0f]);
        PhotoSortingService service = CreateService(
            [1.0f, 0.0f, 0.0f],
            new FakeImageClassifier(new VisionVerdict { Matches = true, Confidence = 0.9 }),
            embeddingProvider: provider,
            photos: photos);

        IReadOnlyList<SortProposal> proposals = await service.CreateProposalsAsync(
            SourceFolder,
            CreateCategory(),
            includeSubfolders: false,
            new DateRange(new DateOnly(2026, 7, 12), new DateOnly(2026, 7, 26)),
            progress: null,
            TestContext.Current.CancellationToken);

        Assert.Equal(2, provider.CallCount);
        Assert.Equal(2, proposals.Count);
        Assert.All(proposals, vorschlag => Assert.StartsWith("urlaub", vorschlag.Photo.FileName, StringComparison.Ordinal));
    }

    [Fact]
    public async Task CreateProposalsAsync_WithDateRange_StillCountsSkippedPhotosInTheProgress()
    {
        // Auch die übergangenen Fotos zählen mit: Sonst bliebe der Balken bei einem engen
        // Zeitraum weit vor dem Ende stehen, und der Lauf sähe abgebrochen aus.
        IReadOnlyList<Photo> photos =
        [
            Foto("a.jpg", new DateOnly(2026, 1, 1)),
            Foto("b.jpg", new DateOnly(2026, 7, 12)),
            Foto("c.jpg", new DateOnly(2026, 12, 31)),
        ];
        PhotoSortingService service = CreateService(
            [1.0f, 0.0f, 0.0f],
            new FakeImageClassifier(new VisionVerdict { Matches = true, Confidence = 0.9 }),
            photos: photos);

        CollectingProgress reported = new();
        _ = await service.CreateProposalsAsync(
            SourceFolder,
            CreateCategory(),
            includeSubfolders: false,
            new DateRange(new DateOnly(2026, 7, 12), new DateOnly(2026, 7, 12)),
            reported,
            TestContext.Current.CancellationToken);

        Assert.Equal(new SortProgress(3, 3, ScanPhase.Analyzing), reported.Reports[^1]);
    }

    [Fact]
    public async Task CreateProposalsAsync_WithoutDateRange_EvaluatesEveryPhoto()
    {
        IReadOnlyList<Photo> photos =
        [
            Foto("a.jpg", new DateOnly(2026, 1, 1)),
            Foto("b.jpg", new DateOnly(2026, 7, 12)),
            Foto("c.jpg", new DateOnly(2026, 12, 31)),
        ];
        CountingEmbeddingProvider provider = new([1.0f, 0.0f, 0.0f]);
        PhotoSortingService service = CreateService(
            [1.0f, 0.0f, 0.0f],
            new FakeImageClassifier(new VisionVerdict { Matches = true, Confidence = 0.9 }),
            embeddingProvider: provider,
            photos: photos);

        _ = await service.CreateProposalsAsync(
            SourceFolder, CreateCategory(), includeSubfolders: false, DateRange.Unbounded,
            progress: null, TestContext.Current.CancellationToken);

        Assert.Equal(3, provider.CallCount);
    }

    [Fact]
    public async Task CreateProposalsAsync_WithDateRange_LeavesOutPhotosWithoutACaptureDate()
    {
        // „Von–bis" gilt streng: Drin ist nur, was nachweislich in den Zeitraum fällt.
        // Ein Foto ohne Aufnahmedatum lässt sich nicht zuordnen und bleibt draußen —
        // sonst wäre die Angabe keine verlässliche Grenze, sondern eine ungefähre.
        IReadOnlyList<Photo> photos =
        [
            new Photo { FullPath = @"C:\fotos\ohne.jpg", FileName = "ohne.jpg", CapturedAt = null },
            Foto("drinnen.jpg", new DateOnly(2026, 7, 14)),
            Foto("januar.jpg", new DateOnly(2026, 1, 1)),
        ];
        CountingEmbeddingProvider provider = new([1.0f, 0.0f, 0.0f]);
        PhotoSortingService service = CreateService(
            [1.0f, 0.0f, 0.0f],
            new FakeImageClassifier(new VisionVerdict { Matches = true, Confidence = 0.9 }),
            embeddingProvider: provider,
            photos: photos);

        IReadOnlyList<SortProposal> proposals = await service.CreateProposalsAsync(
            SourceFolder,
            CreateCategory(),
            includeSubfolders: false,
            new DateRange(new DateOnly(2026, 7, 12), new DateOnly(2026, 7, 26)),
            progress: null,
            TestContext.Current.CancellationToken);

        Assert.Equal(1, provider.CallCount);
        SortProposal vorschlag = Assert.Single(proposals);
        Assert.Equal("drinnen.jpg", vorschlag.Photo.FileName);
    }

    [Fact]
    public async Task CreateProposalsAsync_WithoutDateRange_StillEvaluatesPhotosWithoutACaptureDate()
    {
        // Ohne Zeitraum gibt es keine Grenze, an der ein fehlendes Datum scheitern könnte —
        // solche Fotos werden ganz normal bewertet.
        IReadOnlyList<Photo> photos =
        [
            new Photo { FullPath = @"C:\fotos\ohne.jpg", FileName = "ohne.jpg", CapturedAt = null },
        ];
        CountingEmbeddingProvider provider = new([1.0f, 0.0f, 0.0f]);
        PhotoSortingService service = CreateService(
            [1.0f, 0.0f, 0.0f],
            new FakeImageClassifier(new VisionVerdict { Matches = true, Confidence = 0.9 }),
            embeddingProvider: provider,
            photos: photos);

        _ = await service.CreateProposalsAsync(
            SourceFolder, CreateCategory(), includeSubfolders: false, DateRange.Unbounded,
            progress: null, TestContext.Current.CancellationToken);

        Assert.Equal(1, provider.CallCount);
    }

    private static Photo Foto(string name, DateOnly tag) => new()
    {
        FullPath = Path.Combine(SourceFolder, name),
        FileName = name,
        CapturedAt = new DateTimeOffset(tag.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero),
    };

    [Fact]
    public async Task CreateProposalsAsync_EvaluatesSeveralPhotosAtOnce()
    {
        // Der Kern der Beschleunigung: Jede Bewertung ist ein Aufruf des Bild-Modells und
        // dauert Sekunden, die die Anwendung nur wartet. Nacheinander summiert sich das
        // bei tausend Fotos auf Stunden. Der Test belegt, dass tatsächlich mehrere
        // Aufrufe gleichzeitig offen sind – eine Zusicherung über die Laufzeit allein
        // wäre auf der Baumaschine nicht verlässlich zu messen.
        IReadOnlyList<Photo> photos = [.. Enumerable.Range(0, 8).Select(index => new Photo
        {
            FullPath = $@"C:\fotos\bild{index}.jpg",
            FileName = $"bild{index}.jpg",
        })];
        ConcurrencyTrackingEmbeddingProvider provider = new([1.0f, 0.0f, 0.0f]);
        PhotoSortingService service = CreateService(
            [1.0f, 0.0f, 0.0f],
            new FakeImageClassifier(new VisionVerdict { Matches = true, Confidence = 0.9 }),
            embeddingProvider: provider,
            photos: photos);

        _ = await service.CreateProposalsAsync(
            @"C:\fotos", CreateCategory(), includeSubfolders: false, DateRange.Unbounded, progress: null,
            TestContext.Current.CancellationToken);

        Assert.True(
            provider.MaxConcurrent > 1,
            $"Es war immer nur ein Aufruf offen ({provider.MaxConcurrent}); die Bewertung läuft also weiterhin nacheinander.");
    }

    [Fact]
    public async Task CreateProposalsAsync_KeepsTheFolderOrderDespiteParallelEvaluation()
    {
        // Die Vorschau zeigt die Vorschläge in der Reihenfolge des Ordners. Würden sie in
        // der Reihenfolge angehängt, in der die Bewertungen fertig werden, stünden sie
        // nach jedem Lauf anders – und bei zwei Läufen über denselben Ordner käme eine
        // andere Liste heraus, ohne dass sich etwas geändert hätte.
        IReadOnlyList<Photo> photos = [.. Enumerable.Range(0, 6).Select(index => new Photo
        {
            FullPath = $@"C:\fotos\bild{index}.jpg",
            FileName = $"bild{index}.jpg",
        })];

        // Das erste Bild braucht am längsten, das letzte am kürzesten: Ohne feste Plätze
        // käme die Liste genau verkehrt herum heraus.
        ConcurrencyTrackingEmbeddingProvider provider = new(
            [1.0f, 0.0f, 0.0f],
            photo => TimeSpan.FromMilliseconds(60 - (int.Parse(photo.FileName[4..5], CultureInfo.InvariantCulture) * 10)));

        PhotoSortingService service = CreateService(
            [1.0f, 0.0f, 0.0f],
            new FakeImageClassifier(new VisionVerdict { Matches = true, Confidence = 0.9 }),
            embeddingProvider: provider,
            photos: photos);

        IReadOnlyList<SortProposal> proposals = await service.CreateProposalsAsync(
            @"C:\fotos", CreateCategory(), includeSubfolders: false, DateRange.Unbounded, progress: null,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            photos.Select(photo => photo.FileName),
            proposals.Select(proposal => proposal.Photo.FileName));
    }

    [Fact]
    public async Task CreateProposalsAsync_CountsEveryPhotoExactlyOnceDespiteParallelEvaluation()
    {
        // Mehrere Bewertungen werden gleichzeitig fertig. Ein gewöhnliches ++ verlöre
        // dabei Zählschritte, und der Balken käme nie ganz an – die Anzeige stünde am
        // Ende bei „Bild 994 von 1000", obwohl der Lauf durch ist.
        IReadOnlyList<Photo> photos = [.. Enumerable.Range(0, 12).Select(index => new Photo
        {
            FullPath = $@"C:\fotos\bild{index}.jpg",
            FileName = $"bild{index}.jpg",
        })];
        PhotoSortingService service = CreateService(
            [1.0f, 0.0f, 0.0f],
            new FakeImageClassifier(new VisionVerdict { Matches = true, Confidence = 0.9 }),
            embeddingProvider: new ConcurrencyTrackingEmbeddingProvider([1.0f, 0.0f, 0.0f]),
            photos: photos);

        CollectingProgress reported = new();
        _ = await service.CreateProposalsAsync(
            @"C:\fotos", CreateCategory(), includeSubfolders: false, DateRange.Unbounded, reported,
            TestContext.Current.CancellationToken);

        Assert.Equal(new SortProgress(12, 12, ScanPhase.Analyzing), reported.Reports[^1]);
    }

    [Fact]
    public async Task CreateProposalsAsync_StartsEvaluatingBeforeAllPhotosAreLoaded()
    {
        // Der eigentliche Zweck der Kette: Die Bewertung wartet nicht mehr, bis der ganze
        // Ordner geladen ist. Vorher zeigte die Oberfläche bei tausend Bildern aus der
        // Cloud minutenlang nur das Laden, bevor sich beim Bewerten überhaupt etwas tat.
        IReadOnlyList<Photo> photos = [.. Enumerable.Range(0, 10).Select(index => new Photo
        {
            FullPath = $@"C:\fotos\bild{index}.jpg",
            FileName = $"bild{index}.jpg",
        })];
        SlowStreamingPhotoSource source = new(photos);
        SnapshotEmbeddingProvider provider = new([1.0f, 0.0f, 0.0f], () => source.Yielded);

        PhotoSortingService service = CreateService(
            [1.0f, 0.0f, 0.0f],
            new FakeImageClassifier(new VisionVerdict { Matches = true, Confidence = 0.9 }),
            embeddingProvider: provider,
            source: source);

        _ = await service.CreateProposalsAsync(
            @"C:\fotos", CreateCategory(), includeSubfolders: false, DateRange.Unbounded, progress: null,
            TestContext.Current.CancellationToken);

        Assert.True(
            provider.YieldedAtFirstCall is > 0 and < 10,
            $"Beim ersten Bewerten waren {provider.YieldedAtFirstCall} von 10 Bildern geladen — "
            + "die Bewertung wartet also weiterhin, bis der ganze Ordner da ist.");
    }

    [Fact]
    public async Task CreateProposalsAsync_NeverReportsMoreEvaluatedThanLoaded()
    {
        // Die Bewertung muss dem Laden immer mindestens ein Bild hinterherlaufen — anders
        // geht es gar nicht, denn bewertet werden kann nur, was schon da ist. Sichtbar ist
        // das an der Reihenfolge der Meldungen: Ein Bild wird als geladen gezählt, bevor es
        // weitergereicht wird. Andersherum stünde der Analysebalken vor dem Ladebalken, und
        // die Anzeige behauptete, es sei mehr ausgewertet als überhaupt geladen.
        IReadOnlyList<Photo> photos = [.. Enumerable.Range(0, 20).Select(index => new Photo
        {
            FullPath = $@"C:\fotos\bild{index}.jpg",
            FileName = $"bild{index}.jpg",
        })];
        PhotoSortingService service = CreateService(
            [1.0f, 0.0f, 0.0f],
            new FakeImageClassifier(new VisionVerdict { Matches = true, Confidence = 0.9 }),
            photos: photos);

        CollectingProgress reported = new();
        _ = await service.CreateProposalsAsync(
            @"C:\fotos", CreateCategory(), includeSubfolders: false, DateRange.Unbounded, reported,
            TestContext.Current.CancellationToken);

        int loaded = 0;
        foreach (SortProgress report in reported.Reports)
        {
            if (report.Phase == ScanPhase.Gathering)
            {
                loaded = report.Processed;
                continue;
            }

            Assert.True(
                report.Processed <= loaded,
                $"Es waren {report.Processed} Bilder bewertet, aber erst {loaded} geladen.");
        }

        Assert.Equal(new SortProgress(20, 20, ScanPhase.Analyzing), reported.Reports[^1]);
    }

    /// <summary>
    /// Reicht die Bilder einzeln und mit spürbarer Wartezeit weiter — wie ein Ordner, der
    /// erst aus der Cloud geholt werden muss — und zählt mit, wie viele schon draußen sind.
    /// </summary>
    private sealed class SlowStreamingPhotoSource(IReadOnlyList<Photo> photos) : IPhotoSource
    {
        private int _yielded;

        /// <summary>Wie viele Bilder die Quelle bereits weitergereicht hat.</summary>
        public int Yielded => Volatile.Read(ref _yielded);

        public Task<IReadOnlyList<Photo>> GetPhotosAsync(
            string folderPath,
            bool includeSubfolders,
            int skip,
            int? maxCount,
            IProgress<PhotoScanProgress>? progress,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("Dieser Test prüft ausschließlich die Kette.");

        public async IAsyncEnumerable<ScannedPhoto> StreamPhotosAsync(
            string folderPath,
            bool includeSubfolders,
            int skip,
            int? maxCount,
            IProgress<PhotoScanProgress>? progress,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            for (int index = 0; index < photos.Count; index++)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken).ConfigureAwait(false);
                _ = Interlocked.Increment(ref _yielded);
                yield return new ScannedPhoto(photos[index], index, photos.Count);
            }
        }
    }

    /// <summary>
    /// Hält fest, wie weit das Laden beim allerersten Bewerten war.
    /// </summary>
    private sealed class SnapshotEmbeddingProvider(float[] vector, Func<int> loadedSoFar) : IEmbeddingProvider
    {
        private int _calls;

        /// <summary>Anzahl der geladenen Bilder beim ersten Aufruf; -1, wenn nie aufgerufen.</summary>
        public int YieldedAtFirstCall { get; private set; } = -1;

        public Task<ImageEmbedding> CreateEmbeddingAsync(Photo photo, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                YieldedAtFirstCall = loadedSoFar();
            }

            return Task.FromResult(new ImageEmbedding(vector, "fake"));
        }
    }

    /// <summary>
    /// Liefert einen festen Vektor nach einer kurzen Wartezeit und hält fest, wie viele
    /// Aufrufe dabei gleichzeitig offen waren.
    /// </summary>
    private sealed class ConcurrencyTrackingEmbeddingProvider(
        float[] vector,
        Func<Photo, TimeSpan>? delay = null) : IEmbeddingProvider
    {
        private readonly Lock _gate = new();
        private int _current;

        /// <summary>Die höchste Zahl gleichzeitig offener Aufrufe.</summary>
        public int MaxConcurrent { get; private set; }

        public async Task<ImageEmbedding> CreateEmbeddingAsync(Photo photo, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                _current++;
                MaxConcurrent = Math.Max(MaxConcurrent, _current);
            }

            try
            {
                await Task.Delay(delay?.Invoke(photo) ?? TimeSpan.FromMilliseconds(30), cancellationToken)
                    .ConfigureAwait(false);
                return new ImageEmbedding(vector, "fake");
            }
            finally
            {
                lock (_gate)
                {
                    _current--;
                }
            }
        }
    }

    /// <summary>
    /// Nimmt Fortschrittsmeldungen unmittelbar entgegen.
    ///
    /// Unter einem Schloss, weil beide Abschnitte der Kette gleichzeitig melden: das Laden
    /// aus seinen Lesefäden, die Bewertung aus ihren. Im Betrieb sammelt ein
    /// <see cref="Progress{T}"/> die Meldungen auf dem Oberflächen-Faden ein; hier muss der
    /// Empfänger selbst dafür sorgen. Das Schloss hält zugleich die Reihenfolge fest, auf
    /// die sich der Test unten stützt.
    /// </summary>
    private sealed class CollectingProgress : IProgress<SortProgress>
    {
        private readonly Lock _gate = new();
        private readonly List<SortProgress> _reports = [];

        public IReadOnlyList<SortProgress> Reports
        {
            get
            {
                lock (_gate)
                {
                    return [.. _reports];
                }
            }
        }

        public void Report(SortProgress value)
        {
            lock (_gate)
            {
                _reports.Add(value);
            }
        }
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
            SourceFolder, category, includeSubfolders: false, DateRange.Unbounded, progress: null, CancellationToken.None);

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
            SourceFolder, category, includeSubfolders: false, DateRange.Unbounded, progress: null, CancellationToken.None);

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
            SourceFolder, CreateCategory(), includeSubfolders: false, DateRange.Unbounded, progress: null,
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
            SourceFolder, CreateCategory(), includeSubfolders: false, DateRange.Unbounded, progress: null,
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
            SourceFolder, CreateCategory(), includeSubfolders: false, DateRange.Unbounded, progress: null,
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

    // ── Sortieren allein nach Aufnahmedatum (ohne KI) ──────────────────────────

    // Ein Provider, der bei jedem Aufruf scheitert. Er ist der eigentliche Beweis dieses
    // Abschnitts: Wenn der Datums-Lauf durchläuft, wurde die KI kein einziges Mal
    // befragt — genau das ist der Sinn dieses Weges.
    private static PhotoSortingService CreateDateOnlyService(
        IReadOnlyList<Photo> photos,
        FakeSortMemory? memory = null) =>
        CreateService(
            embedding: [1.0f, 0.0f, 0.0f],
            classifier: new FakeImageClassifier(new VisionVerdict { Matches = true, Confidence = 1.0 }),
            memory: memory,
            embeddingProvider: new FakeEmbeddingProvider(
                _ => throw new InvalidOperationException(
                    "Beim Sortieren nach Datum darf kein Embedding erzeugt werden.")),
            photos: photos);

    [Fact]
    public async Task CreateDateProposalsAsync_PhotoInRange_IsProposedWithoutAnyAiCall()
    {
        PhotoSortingService service = CreateDateOnlyService([Foto("urlaub.jpg", new DateOnly(2026, 7, 15))]);
        DateRange range = new(new DateOnly(2026, 7, 12), new DateOnly(2026, 7, 21));

        IReadOnlyList<SortProposal> proposals = await service.CreateDateProposalsAsync(
            SourceFolder, "Urlaub Norwegen", includeSubfolders: false, range, progress: null, CancellationToken.None);

        SortProposal proposal = Assert.Single(proposals);
        Assert.Equal(ClassificationMethod.CaptureDate, proposal.Method);
        Assert.Equal(1.0, proposal.Confidence);
        Assert.Equal(Path.Combine(SourceFolder, "Urlaub Norwegen"), proposal.TargetFolderPath);
    }

    [Fact]
    public async Task CreateDateProposalsAsync_PhotoOutsideRange_IsNotProposed()
    {
        PhotoSortingService service = CreateDateOnlyService([Foto("davor.jpg", new DateOnly(2026, 7, 11))]);
        DateRange range = new(new DateOnly(2026, 7, 12), new DateOnly(2026, 7, 21));

        IReadOnlyList<SortProposal> proposals = await service.CreateDateProposalsAsync(
            SourceFolder, "Urlaub", includeSubfolders: false, range, progress: null, CancellationToken.None);

        Assert.Empty(proposals);
    }

    [Fact]
    public async Task CreateDateProposalsAsync_PhotoWithoutCaptureDate_IsNotProposed()
    {
        // Ohne Aufnahmedatum lässt sich das Foto dem Zeitraum nicht zuordnen. Es
        // stillschweigend mitzunehmen wäre die gefährlichere Richtung.
        PhotoSortingService service = CreateDateOnlyService([SamplePhoto]);
        DateRange range = new(new DateOnly(2026, 7, 12), new DateOnly(2026, 7, 21));

        IReadOnlyList<SortProposal> proposals = await service.CreateDateProposalsAsync(
            SourceFolder, "Urlaub", includeSubfolders: false, range, progress: null, CancellationToken.None);

        Assert.Empty(proposals);
    }

    [Fact]
    public async Task CreateDateProposalsAsync_UnboundedRange_ProposesNothing()
    {
        // Ohne Grenze wäre jedes Foto des Ordners ein Vorschlag zum Verschieben.
        PhotoSortingService service = CreateDateOnlyService([Foto("a.jpg", new DateOnly(2026, 7, 15))]);

        IReadOnlyList<SortProposal> proposals = await service.CreateDateProposalsAsync(
            SourceFolder, "Urlaub", includeSubfolders: false, DateRange.Unbounded, progress: null, CancellationToken.None);

        Assert.Empty(proposals);
    }

    [Fact]
    public async Task CreateDateProposalsAsync_ReversedRange_ProposesNothing()
    {
        PhotoSortingService service = CreateDateOnlyService([Foto("a.jpg", new DateOnly(2026, 7, 15))]);
        DateRange reversed = new(new DateOnly(2026, 7, 21), new DateOnly(2026, 7, 12));

        IReadOnlyList<SortProposal> proposals = await service.CreateDateProposalsAsync(
            SourceFolder, "Urlaub", includeSubfolders: false, reversed, progress: null, CancellationToken.None);

        Assert.Empty(proposals);
    }

    [Fact]
    public async Task CreateDateProposalsAsync_BothBoundsIncluded_TakesFirstAndLastDay()
    {
        // Beide Enden gehören dazu: Sonst fielen ausgerechnet Anreise- und Abreisetag
        // heraus, die die Nutzerin gerade eingetippt hat.
        PhotoSortingService service = CreateDateOnlyService(
            [Foto("erster.jpg", new DateOnly(2026, 7, 12)), Foto("letzter.jpg", new DateOnly(2026, 7, 21))]);
        DateRange range = new(new DateOnly(2026, 7, 12), new DateOnly(2026, 7, 21));

        IReadOnlyList<SortProposal> proposals = await service.CreateDateProposalsAsync(
            SourceFolder, "Urlaub", includeSubfolders: false, range, progress: null, CancellationToken.None);

        Assert.Equal(2, proposals.Count);
    }

    private static PhotoSortingService CreateService(
        float[] embedding,
        FakeImageClassifier classifier,
        IFileOrganizer? organizer = null,
        FakeSortMemory? memory = null,
        IEmbeddingProvider? embeddingProvider = null,
        FakeSortJournal? journal = null,
        IReadOnlyList<Photo>? photos = null,
        IPhotoSource? source = null)
    {
        IPhotoSource photoSource = source ?? new FakePhotoSource(photos ?? [SamplePhoto]);
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
