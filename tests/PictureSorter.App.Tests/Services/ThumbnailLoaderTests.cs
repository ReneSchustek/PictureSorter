using PictureSorter.App.Services;
using PictureSorter.Core.Exceptions;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.App.Tests.Services;

/// <summary>
/// Tests des Laders für Vorschaubilder. Ohne Grenze und Abbruch trieb das Scrollen durch einen großen Bestand das
/// Programm auf über 4 GB und 1.300 Threads, bis es nicht mehr reagierte.
/// </summary>
public sealed class ThumbnailLoaderTests
{
    private const int Edge = 112;

    [Fact]
    public async Task Load_ReadsAtMostTheAllowedNumberAtOnce()
    {
        CountingReader reader = new() { Delay = TimeSpan.FromMilliseconds(30) };
        using ThumbnailLoader sut = new(reader, parallelReads: 2);

        _ = await Task.WhenAll(Enumerable.Range(0, 12).Select(index =>
            sut.LoadAsync($"bild{index}.jpg", Edge, TestContext.Current.CancellationToken)));

        Assert.Equal(12, reader.Reads);
        Assert.True(reader.MaxConcurrent <= 2, $"Zugleich gelesen: {reader.MaxConcurrent}");
    }

    [Fact]
    public async Task Load_ForAnImageScrolledAway_NeverReadsIt()
    {
        // Das ist die eigentliche Ersparnis: Wer durch die Liste fährt, stößt für die übersprungenen Zeilen nichts an.
        CountingReader reader = new() { Delay = TimeSpan.FromSeconds(30) };
        using ThumbnailLoader sut = new(reader, parallelReads: 1);
        using CancellationTokenSource first = new();
        using CancellationTokenSource second = new();

        Task<ScaledImage?> blocking = sut.LoadAsync("erstes.jpg", Edge, first.Token);
        Task<ScaledImage?> waiting = sut.LoadAsync("zweites.jpg", Edge, second.Token);
        await second.CancelAsync();
        await first.CancelAsync();

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => blocking);
        Assert.Equal(1, reader.Reads);
    }

    [Fact]
    public async Task Load_TheSameImageAgain_ComesFromTheCache()
    {
        CountingReader reader = new();
        using ThumbnailLoader sut = new(reader);

        ScaledImage? first = await sut.LoadAsync("bild.jpg", Edge, TestContext.Current.CancellationToken);
        ScaledImage? second = await sut.LoadAsync("bild.jpg", Edge, TestContext.Current.CancellationToken);

        Assert.Same(first, second);
        Assert.Equal(1, reader.Reads);
    }

    [Fact]
    public async Task Load_AnotherSize_ReadsAgain()
    {
        CountingReader reader = new();
        using ThumbnailLoader sut = new(reader);

        _ = await sut.LoadAsync("bild.jpg", Edge, TestContext.Current.CancellationToken);
        _ = await sut.LoadAsync("bild.jpg", Edge * 2, TestContext.Current.CancellationToken);

        Assert.Equal(2, reader.Reads);
    }

    [Fact]
    public async Task Load_AnUnreadableFile_GivesNothingAndIsNotDecodedAgain()
    {
        CountingReader reader = new() { Failure = new ImageUnreadableException("kaputt") };
        using ThumbnailLoader sut = new(reader);

        Assert.Null(await sut.LoadAsync("kaputt.jpg", Edge, TestContext.Current.CancellationToken));
        Assert.Null(await sut.LoadAsync("kaputt.jpg", Edge, TestContext.Current.CancellationToken));

        Assert.Equal(1, reader.Reads);
    }

    [Fact]
    public async Task Load_WhenAFileDoesNotAnswer_FreesItsSlot()
    {
        CountingReader reader = new() { Delay = TimeSpan.FromSeconds(30) };
        using ThumbnailLoader sut = new(reader, parallelReads: 1, readTimeout: TimeSpan.FromMilliseconds(50));

        Assert.Null(await sut.LoadAsync("steht-still.jpg", Edge, TestContext.Current.CancellationToken));

        reader.Delay = TimeSpan.Zero;
        Assert.NotNull(await sut.LoadAsync("geht.jpg", Edge, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Load_ManyImages_DropsTheOldestInsteadOfGrowingForever()
    {
        CountingReader reader = new() { PixelBytes = 8 * 1024 * 1024 };
        using ThumbnailLoader sut = new(reader);

        _ = await sut.LoadAsync("alt.jpg", Edge, TestContext.Current.CancellationToken);
        foreach (int index in Enumerable.Range(0, 6))
        {
            _ = await sut.LoadAsync($"neu{index}.jpg", Edge, TestContext.Current.CancellationToken);
        }

        int before = reader.Reads;
        _ = await sut.LoadAsync("alt.jpg", Edge, TestContext.Current.CancellationToken);

        Assert.Equal(before + 1, reader.Reads);
    }

    /// <summary>Zählt die Lesevorgänge und wie viele zugleich liefen.</summary>
    private sealed class CountingReader : IScaledImageReader
    {
        private int _reads;
        private int _current;
        private int _maxConcurrent;

        public int Reads => Volatile.Read(ref _reads);

        public int MaxConcurrent => Volatile.Read(ref _maxConcurrent);

        public TimeSpan Delay { get; set; }

        public Exception? Failure { get; init; }

        public int PixelBytes { get; init; } = 16;

        public async Task<ScaledImage> ReadAsync(string filePath, int shortEdge, CancellationToken cancellationToken)
        {
            _ = Interlocked.Increment(ref _reads);
            int current = Interlocked.Increment(ref _current);
            InterlockedMax(ref _maxConcurrent, current);
            try
            {
                if (Delay > TimeSpan.Zero)
                {
                    await Task.Delay(Delay, cancellationToken).ConfigureAwait(false);
                }

                return Failure is null
                    ? new ScaledImage(new byte[PixelBytes], 2, 2)
                    : throw Failure;
            }
            finally
            {
                _ = Interlocked.Decrement(ref _current);
            }
        }

        private static void InterlockedMax(ref int target, int value)
        {
            int seen = Volatile.Read(ref target);
            while (value > seen)
            {
                int previous = Interlocked.CompareExchange(ref target, value, seen);
                if (previous == seen)
                {
                    return;
                }

                seen = previous;
            }
        }
    }
}
