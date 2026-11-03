using Microsoft.Extensions.Logging.Abstractions;
using PictureSorter.Application.Sorting;
using PictureSorter.Application.Tests.Fakes;
using PictureSorter.Core.Entities;
using PictureSorter.Core.Exceptions;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Application.Tests.Sorting;

/// <summary>
/// Prüft, woher der Sammeldurchlauf seine Ziele nimmt und was mit Bildern geschieht, die sich nicht lesen
/// lassen. Ein Fehler hier hieße: falsche Ordner, oder ein Lauf über Tausende bricht an einem Bild ab.
/// </summary>
public sealed class BulkSortServiceTests
{
    private const string Source = @"C:\Fotos";

    [Fact]
    public async Task Analyze_TakesTheFoldersAsTargetsAndTheirPhotosAsExamples()
    {
        FolderPhotoSource source = new(new Dictionary<string, Photo[]>
        {
            [Source] = [Named("offen.jpg")],
            [Path.Combine(Source, "Meer")] = [Named("strand.jpg")],
        });
        RecordingEmbedder embedder = new();

        BulkSortSession session = await CreateSut(source, embedder, ["Meer"]).AnalyzeAsync(Source, [], null, CancellationToken.None);

        Assert.Equal(["Meer"], session.CategoryNames);
        Assert.Contains("strand.jpg", embedder.Photos);
    }

    [Fact]
    public async Task Analyze_KeepsAnEmptyFolderAsATargetWithoutExamples()
    {
        RecordingEmbedder embedder = new();
        FolderPhotoSource source = new(new Dictionary<string, Photo[]> { [Source] = [Named("offen.jpg")] });

        BulkSortSession session = await CreateSut(source, embedder, ["Rezepte"]).AnalyzeAsync(Source, [], null, CancellationToken.None);

        Assert.Equal(["Rezepte"], session.CategoryNames);
        Assert.Equal(["Rezepte"], session.CategoryNamesWithoutExamples);
    }

    [Fact]
    public async Task Analyze_AddsEnteredNamesOnce()
    {
        // Ein Name, den es schon als Ordner gibt, darf kein zweites Ziel werden; Groß und Klein zählen
        // unter Windows nicht. Leere Einträge entstehen beim Tippen und zählen auch nicht.
        RecordingEmbedder embedder = new();
        FolderPhotoSource source = new(new Dictionary<string, Photo[]>
        {
            [Source] = [Named("offen.jpg")],
            [Path.Combine(Source, "Meer")] = [Named("strand.jpg")],
        });

        BulkSortSession session = await CreateSut(source, embedder, ["Meer"])
            .AnalyzeAsync(Source, ["meer", " Hochzeit ", "  "], null, CancellationToken.None);

        Assert.Equal(["Meer", "Hochzeit"], session.CategoryNames);
        Assert.Equal(["Hochzeit"], session.CategoryNamesWithoutExamples);
    }

    [Fact]
    public async Task Analyze_CountsWhatItCouldNotRead()
    {
        FolderPhotoSource source = new(new Dictionary<string, Photo[]>
        {
            [Source] = [Named("kaputt.jpg"), Named("gut.jpg")],
        });

        BulkSortSession session = await CreateSut(source, new RecordingEmbedder { Unreadable = "kaputt.jpg" }, [])
            .AnalyzeAsync(Source, [], null, CancellationToken.None);

        Assert.Equal(1, session.UnreadablePhotoCount);
        Assert.Equal(7, session.UnsupportedFileCount);
        Assert.Equal(58, session.OnlineOnlyPhotoCount);
    }

    [Fact]
    public async Task Analyze_WhenMemoryRunsLow_StopsWithAClearMessage()
    {
        // Das Programm starb an einem echten Bestand ohne ein Wort. Jetzt endet der Lauf mit einer Meldung.
        Photo[] photos = [.. Enumerable.Range(0, 60).Select(static index => Named($"{index:D2}.jpg"))];
        FolderPhotoSource source = new(new Dictionary<string, Photo[]> { [Source] = photos });
        BulkSortService sut = new(source, new RecordingEmbedder(), new FixedFolderLookup([]), NullLogger<BulkSortService>.Instance, static () => true);

        InsufficientMemoryException error = await Assert.ThrowsAsync<InsufficientMemoryException>(
            () => sut.AnalyzeAsync(Source, [], null, CancellationToken.None));

        Assert.Contains("Arbeitsspeicher", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Analyze_WithEnoughMemory_ReadsEverything()
    {
        Photo[] photos = [.. Enumerable.Range(0, 60).Select(static index => Named($"{index:D2}.jpg"))];
        FolderPhotoSource source = new(new Dictionary<string, Photo[]> { [Source] = photos });
        BulkSortService sut = new(source, new RecordingEmbedder(), new FixedFolderLookup([]), NullLogger<BulkSortService>.Instance, static () => false);

        BulkSortSession session = await sut.AnalyzeAsync(Source, [], null, CancellationToken.None);

        Assert.Equal(60, session.Calculate().Count);
    }

    [Fact]
    public async Task Analyze_CountsFilesTheSourceCouldNotDeliverAsUnreadable()
    {
        // Die Quelle meldet zwei Dateien, liefert aber nur eine: Die andere fiel beim Einlesen aus.
        Photo[] photos = [Named("gut.jpg")];
        FolderPhotoSource source = new(new Dictionary<string, Photo[]> { [Source] = photos }) { ReportedTotal = 2 };

        BulkSortSession session = await CreateSut(source, new RecordingEmbedder(), [])
            .AnalyzeAsync(Source, [], null, CancellationToken.None);

        Assert.Equal(1, session.UnreadablePhotoCount);
    }

    [Fact]
    public async Task Analyze_LeavesUnreadablePhotosOutAndContinues()
    {
        RecordingEmbedder embedder = new() { Unreadable = "kaputt.jpg" };
        FolderPhotoSource source = new(new Dictionary<string, Photo[]>
        {
            [Source] = [Named("kaputt.jpg"), Named("gut.jpg")],
            [Path.Combine(Source, "Meer")] = [Named("kaputt.jpg"), Named("strand.jpg")],
        });

        BulkSortSession session = await CreateSut(source, embedder, ["Meer"]).AnalyzeAsync(Source, [], null, CancellationToken.None);

        BulkSuggestion suggestion = Assert.Single(session.Calculate());
        Assert.Equal("gut.jpg", suggestion.Photo.FileName);
    }

    [Fact]
    public async Task Analyze_KeepsTheOrderOfTheFolder()
    {
        // Mehrere Bilder laufen gleichzeitig. Die Vorschau soll trotzdem bei jedem Lauf gleich aussehen.
        Photo[] photos = [.. Enumerable.Range(0, 40).Select(static index => Named($"{index:D2}.jpg"))];
        FolderPhotoSource source = new(new Dictionary<string, Photo[]> { [Source] = photos });

        BulkSortSession session = await CreateSut(source, new RecordingEmbedder(), []).AnalyzeAsync(Source, [], null, CancellationToken.None);

        Assert.Equal(photos.Select(static photo => photo.FileName), session.Calculate().Select(static suggestion => suggestion.Photo.FileName));
    }

    [Fact]
    public async Task Analyze_ReportsTheDownloadAndThePhotos()
    {
        FolderPhotoSource source = new(new Dictionary<string, Photo[]>
        {
            [Source] = [Named("offen.jpg")],
            [Path.Combine(Source, "Meer")] = [Named("strand.jpg")],
        });
        SynchronousProgress progress = new();
        RecordingEmbedder embedder = new() { ReportsDownload = true };

        _ = await CreateSut(source, embedder, ["Meer"]).AnalyzeAsync(Source, [], progress, CancellationToken.None);

        Assert.Contains(progress.Reports, static report => report.Phase == BulkSortPhase.Examples);
        Assert.Contains(progress.Reports, static report => report is { Phase: BulkSortPhase.Photos, Processed: 1, Total: 1 });
    }

    [Fact]
    public async Task Analyze_WhenTheModelIsUnavailable_PassesItOn()
    {
        RecordingEmbedder embedder = new() { PrepareFailure = new AiUnavailableException("fehlt") };
        FolderPhotoSource source = new(new Dictionary<string, Photo[]> { [Source] = [] });

        _ = await Assert.ThrowsAsync<AiUnavailableException>(
            () => CreateSut(source, embedder, []).AnalyzeAsync(Source, [], null, CancellationToken.None));
    }

    [Fact]
    public async Task Analyze_WhenTheUserCancels_PassesItOn()
    {
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();
        FolderPhotoSource source = new(new Dictionary<string, Photo[]> { [Source] = [Named("offen.jpg")] });

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateSut(source, new RecordingEmbedder(), []).AnalyzeAsync(Source, [], null, cancellation.Token));
    }

    private static BulkSortService CreateSut(IPhotoSource source, IVisualEmbedder embedder, string[] folders) =>
        new(source, embedder, new FixedFolderLookup(folders), NullLogger<BulkSortService>.Instance);

    private static Photo Named(string name) => new()
    {
        FullPath = Path.Combine(Source, name),
        FileName = name,
        ContentFingerprint = name,
    };

    /// <summary>Liefert je Ordner feste Fotos.</summary>
    private sealed class FolderPhotoSource(Dictionary<string, Photo[]> folders) : IPhotoSource
    {
        public Task<IReadOnlyList<Photo>> GetPhotosAsync(
            string folderPath,
            bool includeSubfolders,
            int skip,
            int? maxCount,
            IProgress<PhotoScanProgress>? progress,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Photo>>(folders.GetValueOrDefault(folderPath, []));

        public IAsyncEnumerable<ScannedPhoto> StreamPhotosAsync(
            string folderPath,
            bool includeSubfolders,
            int skip,
            int? maxCount,
            IProgress<PhotoScanProgress>? progress,
            CancellationToken cancellationToken) =>
            WithReportedTotal(new FakePhotoSource(folders.GetValueOrDefault(folderPath, []))
                .StreamPhotosAsync(folderPath, includeSubfolders, skip, maxCount, progress, cancellationToken));

        /// <summary>Die gemeldete Gesamtzahl; sonst gilt die der gelieferten Fotos.</summary>
        public int? ReportedTotal { get; init; }

        private async IAsyncEnumerable<ScannedPhoto> WithReportedTotal(IAsyncEnumerable<ScannedPhoto> photos)
        {
            await foreach (ScannedPhoto photo in photos.ConfigureAwait(false))
            {
                yield return ReportedTotal is int total ? photo with { Total = total } : photo;
            }
        }
    }

    /// <summary>Liefert feste Vektoren und merkt sich die Aufrufe. Unter Sperre, weil Bilder gleichzeitig laufen.</summary>
    private sealed class RecordingEmbedder : IVisualEmbedder
    {
        private readonly Lock _gate = new();

        public List<string> Photos { get; } = [];

        public string? Unreadable { get; init; }

        public bool ReportsDownload { get; init; }

        public Exception? PrepareFailure { get; init; }

        public Task PrepareAsync(IProgress<ModelDownloadProgress>? progress, CancellationToken cancellationToken)
        {
            if (ReportsDownload)
            {
                progress?.Report(new ModelDownloadProgress(1, 2));
            }

            return PrepareFailure is null ? Task.CompletedTask : Task.FromException(PrepareFailure);
        }

        public Task<ImageEmbedding> EmbedPhotoAsync(Photo photo, CancellationToken cancellationToken)
        {
            if (photo.FileName == Unreadable)
            {
                throw new ImageUnreadableException("kaputt");
            }

            lock (_gate)
            {
                Photos.Add(photo.FileName);
            }

            return Task.FromResult(new ImageEmbedding([1f, 0f], "test"));
        }

    }

    /// <summary>Nennt feste Unterordner.</summary>
    private sealed class FixedFolderLookup(string[] folders) : IFolderLookup
    {
        public string? FindLookalike(string requestedPath) => null;

        public IReadOnlyList<string> ListSubfolders(string folderPath) => folders;

        public int CountUnsupportedFiles(string folderPath) => 7;

        public int CountOnlineOnlyPhotos(string folderPath, bool includeSubfolders) => 58;
    }

    /// <summary>Nimmt Meldungen im Aufruf entgegen; unter Sperre, weil Bilder gleichzeitig melden.</summary>
    private sealed class SynchronousProgress : IProgress<BulkSortProgress>
    {
        private readonly Lock _gate = new();
        private readonly List<BulkSortProgress> _reports = [];

        public IReadOnlyList<BulkSortProgress> Reports
        {
            get
            {
                lock (_gate)
                {
                    return [.. _reports];
                }
            }
        }

        public void Report(BulkSortProgress value)
        {
            lock (_gate)
            {
                _reports.Add(value);
            }
        }
    }
}
