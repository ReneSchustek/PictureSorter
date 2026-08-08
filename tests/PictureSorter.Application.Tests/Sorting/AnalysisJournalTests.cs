using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PictureSorter.Application.Sorting;
using PictureSorter.Application.Tests.Fakes;
using PictureSorter.Core.Entities;
using PictureSorter.Core.Enums;
using PictureSorter.Core.Exceptions;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Application.Tests.Sorting;

/// <summary>
/// Tests des Analyse-Protokolls, des Anhaltens und des Fortsetzens.
///
/// Ein Lauf über tausende Fotos dauert Stunden bis Tage; bleibt er stehen, muss etwas
/// übrig sein, woran sich wieder ansetzen lässt. Die Zusage dieser Tests lautet: Jedes
/// Ergebnis steht auf der Platte, bevor der Lauf endet — und ein zweiter Anlauf legt kein
/// bereits beurteiltes Foto noch einmal der Bilderkennung vor.
/// </summary>
public sealed class AnalysisJournalTests
{
    private const string SourceFolder = @"C:\fotos";

    [Fact]
    public async Task CreateProposalsAsync_WritesEveryDecisionAndClosesTheRun()
    {
        FakeAnalysisJournal journal = new();
        PhotoAnalysisService service = CreateService(journal, [Foto("a.jpg"), Foto("b.jpg"), Foto("c.jpg")]);

        _ = await service.CreateProposalsAsync(
            SourceFolder, CreateCategory(), includeSubfolders: false, DateRange.Unbounded, progress: null, CancellationToken.None);

        AnalysisRun run = Assert.Single(journal.Runs);
        Assert.Equal(AnalysisRunState.Completed, run.State);
        Assert.Equal(SourceFolder, run.SourceFolder);
        Assert.Equal(3, journal.ItemsOf(run.Id).Count);
    }

    [Fact]
    public async Task CreateProposalsAsync_WhenPaused_KeepsWhatWasDecidedSoFar()
    {
        FakeAnalysisJournal journal = new();
        using CancellationTokenSource cancellation = new();

        // Bricht ab, sobald das erste Foto bewertet ist — genau der Fall „Nutzerin hält
        // an, weil der Rechner nicht drei Tage laufen soll".
        CancelingEmbeddingProvider provider = new([1.0f, 0.0f, 0.0f], cancellation);
        PhotoAnalysisService service = CreateService(
            journal,
            [Foto("a.jpg"), Foto("b.jpg"), Foto("c.jpg")],
            embeddingProvider: provider);

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.CreateProposalsAsync(
            SourceFolder, CreateCategory(), includeSubfolders: false, DateRange.Unbounded, progress: null, cancellation.Token));

        AnalysisRun run = Assert.Single(journal.Runs);
        Assert.Equal(AnalysisRunState.Paused, run.State);

        // Der Stand ist gerettet: Was vor dem Anhalten beurteilt wurde, steht auf der
        // Platte. Ohne das wäre das Anhalten ein Verwerfen.
        Assert.NotEmpty(journal.ItemsOf(run.Id));
    }

    [Fact]
    public async Task ResumeAsync_DoesNotAskTheAiAboutAlreadyDecidedPhotos()
    {
        // Der härteste Test dieser Zusage: Der Embedding-Anbieter wirft bei jedem
        // einzelnen Aufruf. Kommen die Vorschläge trotzdem zurück, wurde die KI kein
        // einziges Mal befragt — anders lässt sich das nicht beweisen.
        FakeAnalysisJournal journal = new();
        IReadOnlyList<Photo> photos = [Foto("a.jpg"), Foto("b.jpg")];
        PhotoAnalysisService first = CreateService(journal, photos);

        IReadOnlyList<SortProposal> original = await first.CreateProposalsAsync(
            SourceFolder, CreateCategory(), includeSubfolders: false, DateRange.Unbounded, progress: null, CancellationToken.None);
        Assert.Equal(2, original.Count);

        AnalysisRun run = Assert.Single(journal.Runs);
        PhotoAnalysisService second = CreateService(
            journal,
            photos,
            embeddingProvider: new ThrowingEmbeddingProvider());

        IReadOnlyList<SortProposal> restored = await second.ResumeAsync(
            run, CreateCategory(), progress: null, CancellationToken.None);

        Assert.Equal(2, restored.Count);
        Assert.Equal(
            original.Select(proposal => proposal.Photo.FullPath),
            restored.Select(proposal => proposal.Photo.FullPath));
    }

    [Fact]
    public async Task ResumeAsync_WithoutTheCategory_DeliversNothingInsteadOfGuessing()
    {
        FakeAnalysisJournal journal = new();
        PhotoAnalysisService service = CreateService(journal, [Foto("a.jpg")]);
        AnalysisRun run = NewRun(byDateOnly: false);

        IReadOnlyList<SortProposal> proposals =
            await service.ResumeAsync(run, category: null, progress: null, CancellationToken.None);

        Assert.Empty(proposals);
    }

    [Fact]
    public async Task CreateProposalsAsync_WhenTheJournalIsBroken_StillFinishesTheRun()
    {
        // Das Protokoll ist eine Zugabe, kein Kernbestandteil: Eine gesperrte Datenbank
        // darf einen Lauf, der Tage dauert, nicht abbrechen.
        PhotoAnalysisService service = CreateService(new BrokenAnalysisJournal(), [Foto("a.jpg")]);

        IReadOnlyList<SortProposal> proposals = await service.CreateProposalsAsync(
            SourceFolder, CreateCategory(), includeSubfolders: false, DateRange.Unbounded, progress: null, CancellationToken.None);

        _ = Assert.Single(proposals);
    }

    [Fact]
    public async Task CreateProposalsAsync_WhenAPhotoIsUnreadable_StillReportsTheEnd()
    {
        // Die Quelle findet drei Dateien, liefert aber nur zwei Fotos: Die dritte ließ
        // sich nicht lesen. Der Zählstand erreicht die Gesamtzahl damit nie — die
        // Abschlussmeldung muss trotzdem kommen, sonst bliebe die Anzeige kurz vor dem
        // Ende stehen und wäre von einem Stillstand nicht zu unterscheiden.
        FakeAnalysisJournal journal = new();
        PhotoAnalysisService service = CreateService(
            journal,
            [Foto("a.jpg"), Foto("b.jpg")],
            source: new PartialPhotoSource([Foto("a.jpg"), Foto("b.jpg")], total: 3));

        RecordingProgress progress = new();
        _ = await service.CreateProposalsAsync(
            SourceFolder, CreateCategory(), includeSubfolders: false, DateRange.Unbounded, progress, CancellationToken.None);

        SortProgress last = Assert.Single(progress.Reports, report => report.IsFinal);
        Assert.Equal(2, last.Processed);
        Assert.Equal(3, last.Total);
    }

    [Fact]
    public async Task CreateDateProposalsAsync_WritesTheRunAsWell()
    {
        FakeAnalysisJournal journal = new();
        PhotoAnalysisService service = CreateService(
            journal,
            [FotoAm("urlaub.jpg", new DateOnly(2026, 7, 15))]);

        _ = await service.CreateDateProposalsAsync(
            SourceFolder,
            "Urlaub",
            includeSubfolders: false,
            new DateRange(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31)),
            progress: null,
            CancellationToken.None);

        AnalysisRun run = Assert.Single(journal.Runs);
        Assert.True(run.ByDateOnly);
        Assert.Equal(AnalysisRunState.Completed, run.State);
        _ = Assert.Single(journal.ItemsOf(run.Id));
    }

    [Fact]
    public async Task ResumeAsync_ForADateRun_TakesOverWhatIsAlreadyDecided()
    {
        FakeAnalysisJournal journal = new();
        IReadOnlyList<Photo> photos =
        [
            FotoAm("drin.jpg", new DateOnly(2026, 7, 15)),
            FotoAm("winter.jpg", new DateOnly(2026, 1, 1)),
        ];
        PhotoAnalysisService service = CreateService(journal, photos);
        DateRange range = new(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31));

        IReadOnlyList<SortProposal> first = await service.CreateDateProposalsAsync(
            SourceFolder, "Urlaub", includeSubfolders: false, range, progress: null, CancellationToken.None);
        _ = Assert.Single(first);

        AnalysisRun run = journal.Runs[^1];
        IReadOnlyList<SortProposal> resumed =
            await service.ResumeAsync(run, category: null, progress: null, CancellationToken.None);

        // Dasselbe Ergebnis, ohne dass ein Foto noch einmal geprüft wurde: Der Zeitraum
        // steht im Protokoll, die Urteile ebenfalls.
        SortProposal proposal = Assert.Single(resumed);
        Assert.Equal(first[0].Photo.FullPath, proposal.Photo.FullPath);
    }

    [Fact]
    public async Task CreateDateProposalsAsync_SkipsWhatTheMemoryAlreadySettled()
    {
        FakeAnalysisJournal journal = new();
        Photo photo = FotoAm("schon-sortiert.jpg", new DateOnly(2026, 7, 15));
        FakeSortMemory memory = new();
        await memory.UpsertAsync(
            new SortMemoryRecord
            {
                FolderPath = SourceFolder,
                FileSignature = photo.ComputeSignature(),
                PhotoPath = photo.FullPath,
                CategoryName = "Urlaub",
                Status = SortMemoryStatus.Sorted,
                Confidence = 1.0,
                UpdatedAt = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
            },
            CancellationToken.None);

        PhotoAnalysisService service = CreateService(journal, [photo], memory: memory);

        IReadOnlyList<SortProposal> proposals = await service.CreateDateProposalsAsync(
            SourceFolder,
            "Urlaub",
            includeSubfolders: false,
            new DateRange(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31)),
            progress: null,
            CancellationToken.None);

        Assert.Empty(proposals);
        AnalysisRunItem item = Assert.Single(journal.ItemsOf(journal.Runs[^1].Id));
        Assert.Equal(AnalysisOutcome.SkippedByMemory, item.Outcome);
    }

    [Fact]
    public async Task CreateDateProposalsAsync_WhenTheSourceFails_MarksTheRunAsFailed()
    {
        // Ein Lauf, der einfach aufhört, wäre im Protokoll nicht von einem laufenden zu
        // unterscheiden. Der Grund wird festgehalten, bevor die Ausnahme weiterzieht.
        FakeAnalysisJournal journal = new();
        PhotoAnalysisService service = CreateService(
            journal,
            [],
            source: new ThrowingPhotoSource(new TimeoutException("Das Netzlaufwerk antwortet nicht.")));

        _ = await Assert.ThrowsAsync<TimeoutException>(() => service.CreateDateProposalsAsync(
            SourceFolder,
            "Urlaub",
            includeSubfolders: false,
            new DateRange(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31)),
            progress: null,
            CancellationToken.None));

        AnalysisRun run = Assert.Single(journal.Runs);
        Assert.Equal(AnalysisRunState.Failed, run.State);
        Assert.Contains("Netzlaufwerk", run.FailureReason!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateProposalsAsync_WhenTheSourceFails_MarksTheRunAsFailed()
    {
        FakeAnalysisJournal journal = new();
        PhotoAnalysisService service = CreateService(
            journal,
            [],
            source: new ThrowingPhotoSource(new TimeoutException("Das Netzlaufwerk antwortet nicht.")));

        _ = await Assert.ThrowsAsync<TimeoutException>(() => service.CreateProposalsAsync(
            SourceFolder, CreateCategory(), includeSubfolders: false, DateRange.Unbounded, progress: null, CancellationToken.None));

        AnalysisRun run = Assert.Single(journal.Runs);
        Assert.Equal(AnalysisRunState.Failed, run.State);
    }

    [Fact]
    public async Task ResumeAsync_ForADateRunWithoutUsableRange_DeliversNothing()
    {
        FakeAnalysisJournal journal = new();
        PhotoAnalysisService service = CreateService(journal, [FotoAm("a.jpg", new DateOnly(2026, 7, 15))]);

        IReadOnlyList<SortProposal> proposals = await service.ResumeAsync(
            NewRun(byDateOnly: true), category: null, progress: null, CancellationToken.None);

        Assert.Empty(proposals);
    }

    // ── Testhilfen ─────────────────────────────────────────────────────────────

    private static Photo Foto(string name) => new()
    {
        FullPath = Path.Combine(SourceFolder, name),
        FileName = name,
    };

    private static Photo FotoAm(string name, DateOnly tag) => new()
    {
        FullPath = Path.Combine(SourceFolder, name),
        FileName = name,
        CapturedAt = new DateTimeOffset(tag.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
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

    private static AnalysisRun NewRun(bool byDateOnly) => new()
    {
        Id = new Guid("11111111-1111-1111-1111-111111111111"),
        SourceFolder = SourceFolder,
        CategoryName = "Familie",
        ByDateOnly = byDateOnly,
        IncludeSubfolders = false,
        State = AnalysisRunState.Paused,
        StartedAt = new DateTimeOffset(2026, 8, 6, 20, 0, 0, TimeSpan.Zero),
        LastProgressAt = new DateTimeOffset(2026, 8, 6, 22, 0, 0, TimeSpan.Zero),
    };

    private static PhotoAnalysisService CreateService(
        IAnalysisJournal journal,
        IReadOnlyList<Photo> photos,
        IEmbeddingProvider? embeddingProvider = null,
        IPhotoSource? source = null,
        FakeSortMemory? memory = null)
    {
        FakeClock clock = new(new DateTimeOffset(2026, 8, 8, 8, 0, 0, TimeSpan.Zero));

        return new PhotoAnalysisService(
            source ?? new FakePhotoSource(photos),
            embeddingProvider ?? new FakeEmbeddingProvider(_ => [1.0f, 0.0f, 0.0f]),
            new FakeImageClassifier(new VisionVerdict { Matches = false, Confidence = 0.0 }),
            new SortMemoryGateway(memory ?? new FakeSortMemory(), clock, NullLogger<SortMemoryGateway>.Instance),
            journal,
            clock,
            Options.Create(new SortingOptions()),
            NullLogger<PhotoAnalysisService>.Instance);
    }

    /// <summary>Scheitert schon beim Einlesen des Ordners.</summary>
    private sealed class ThrowingPhotoSource(Exception failure) : IPhotoSource
    {
        public Task<IReadOnlyList<Photo>> GetPhotosAsync(
            string folderPath,
            bool includeSubfolders,
            int skip,
            int? maxCount,
            IProgress<PhotoScanProgress>? progress,
            CancellationToken cancellationToken) => throw failure;

        public async IAsyncEnumerable<ScannedPhoto> StreamPhotosAsync(
            string folderPath,
            bool includeSubfolders,
            int skip,
            int? maxCount,
            IProgress<PhotoScanProgress>? progress,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            throw failure;

            // Unerreichbar, aber nötig: Ohne yield ist die Methode kein Iterator und der
            // Fehler flöge bereits beim Erzeugen der Aufzählung statt beim Auslesen.
#pragma warning disable CS0162 // Unerreichbarer Code entdeckt
            yield break;
#pragma warning restore CS0162
        }
    }

    /// <summary>Wirft bei jedem Aufruf — jeder KI-Zugriff fliegt damit auf.</summary>
    private sealed class ThrowingEmbeddingProvider : IEmbeddingProvider
    {
        public Task<ImageEmbedding> CreateEmbeddingAsync(Photo photo, CancellationToken cancellationToken) =>
            throw new AiUnavailableException("Die KI darf hier nicht gefragt werden.");
    }

    /// <summary>Löst nach der ersten Bewertung den Abbruch aus.</summary>
    private sealed class CancelingEmbeddingProvider(float[] vector, CancellationTokenSource cancellation)
        : IEmbeddingProvider
    {
        public async Task<ImageEmbedding> CreateEmbeddingAsync(Photo photo, CancellationToken cancellationToken)
        {
            await cancellation.CancelAsync().ConfigureAwait(false);
            return new ImageEmbedding(vector, "fake");
        }
    }

    /// <summary>Scheitert bei jedem Zugriff — wie eine gesperrte Datenbank.</summary>
    private sealed class BrokenAnalysisJournal : IAnalysisJournal
    {
        public Task StartAsync(AnalysisRun run, CancellationToken cancellationToken) =>
            throw new TimeoutException("Die Datenbank ist gesperrt.");

        public Task AppendAsync(
            Guid runId,
            IReadOnlyList<AnalysisRunItem> items,
            int totalPhotos,
            DateTimeOffset at,
            CancellationToken cancellationToken) => throw new TimeoutException("Die Datenbank ist gesperrt.");

        public Task FinishAsync(
            Guid runId,
            AnalysisRunState state,
            string? failureReason,
            DateTimeOffset at,
            CancellationToken cancellationToken) => throw new TimeoutException("Die Datenbank ist gesperrt.");

        public Task<AnalysisRun?> GetLatestAsync(CancellationToken cancellationToken) =>
            throw new TimeoutException("Die Datenbank ist gesperrt.");

        public Task<IReadOnlyList<AnalysisRunItem>> GetItemsAsync(Guid runId, CancellationToken cancellationToken) =>
            throw new TimeoutException("Die Datenbank ist gesperrt.");

        public Task DiscardAsync(Guid runId, CancellationToken cancellationToken) =>
            throw new TimeoutException("Die Datenbank ist gesperrt.");
    }

    /// <summary>Sammelt jede Fortschrittsmeldung.</summary>
    private sealed class RecordingProgress : IProgress<SortProgress>
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
}
