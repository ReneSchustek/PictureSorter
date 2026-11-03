using System.Diagnostics.CodeAnalysis;
using BenchmarkDotNet.Attributes;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace PictureSorter.Benchmarks;

/// <summary>
/// Öffnen eines Bildes bis zum fertigen Decoder, einmal je Foto und Lauf, bei 4.000 Fotos also spürbar.
/// Der Vergleich belegt, dass der direkte Dateistrom gegenüber <see cref="StorageFile"/> nichts kostet.
/// Das Programm öffnet Bilder nur noch direkt, weil es mit <see cref="StorageFile"/> an einem echten Bestand
/// am Speicher starb.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
[SuppressMessage(
    "Maintainability",
    "CA1515:Consider making public types internal",
    Justification = "BenchmarkDotNet erzeugt zu jeder Messklasse eine abgeleitete Klasse in einer eigenen Assembly.")]
public class ImageOpenBenchmarks
{
    private const int Edge = 512;

    private string _path = string.Empty;

    /// <summary>Legt ein Testbild an.</summary>
    /// <returns>Eine Aufgabe.</returns>
    [GlobalSetup]
    public async Task SetupAsync()
    {
        _path = Path.Combine(Path.GetTempPath(), $"PictureSorterBench-{Guid.NewGuid():N}.jpg");

        byte[] pixels = new byte[Edge * Edge * 4];
        for (int index = 0; index < pixels.Length; index++)
        {
            pixels[index] = (byte)(index * 31);
        }

        using IRandomAccessStream target = await FileRandomAccessStream.OpenAsync(
            _path,
            FileAccessMode.ReadWrite,
            StorageOpenOptions.None,
            FileOpenDisposition.CreateNew).AsTask().ConfigureAwait(false);
        BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, target).AsTask().ConfigureAwait(false);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, Edge, Edge, 96, 96, pixels);
        await encoder.FlushAsync().AsTask().ConfigureAwait(false);
    }

    /// <summary>Räumt das Testbild weg.</summary>
    [GlobalCleanup]
    public void Cleanup() => File.Delete(_path);

    /// <summary>Der Weg des Programms.</summary>
    /// <returns>Die Breite des Bildes.</returns>
    [Benchmark(Baseline = true)]
    public async Task<uint> OpenDirectlyAsync()
    {
        using IRandomAccessStream stream = await FileRandomAccessStream.OpenAsync(_path, FileAccessMode.Read).AsTask().ConfigureAwait(false);
        BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream).AsTask().ConfigureAwait(false);
        return decoder.PixelWidth;
    }

    /// <summary>Ein Vorschaubild für die Listen, wie es der Lader je Zeile liest.</summary>
    /// <returns>Die Zahl der Bildpunkt-Bytes.</returns>
    [Benchmark]
    public async Task<int> ReadThumbnailAsync()
    {
        using IRandomAccessStream stream = await FileRandomAccessStream.OpenAsync(_path, FileAccessMode.Read).AsTask().ConfigureAwait(false);
        BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream).AsTask().ConfigureAwait(false);
        BitmapTransform transform = new() { ScaledWidth = 112, ScaledHeight = 112, InterpolationMode = BitmapInterpolationMode.Fant };
        PixelDataProvider pixels = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Rgba8,
            BitmapAlphaMode.Ignore,
            transform,
            ExifOrientationMode.RespectExifOrientation,
            ColorManagementMode.ColorManageToSRgb).AsTask().ConfigureAwait(false);
        return pixels.DetachPixelData().Length;
    }

    /// <summary>Der frühere Weg.</summary>
    /// <returns>Die Breite des Bildes.</returns>
    [Benchmark]
    public async Task<uint> OpenThroughStorageFileAsync()
    {
        StorageFile file = await StorageFile.GetFileFromPathAsync(_path).AsTask().ConfigureAwait(false);
        using IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.Read).AsTask().ConfigureAwait(false);
        BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream).AsTask().ConfigureAwait(false);
        return decoder.PixelWidth;
    }
}
