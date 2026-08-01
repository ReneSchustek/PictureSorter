using Microsoft.Extensions.Logging.Abstractions;
using PictureSorter.Core.Exceptions;
using PictureSorter.Imaging;
using PictureSorter.Imaging.Tests.Fixtures;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace PictureSorter.Imaging.Tests.Vision;

/// <summary>
/// Tests der Bildaufbereitung für das Bild-Modell. Sie ist die Stelle, an der aus
/// einem Handyfoto überhaupt erst etwas wird, das die Bilderkennung öffnen kann.
/// Kommt hier das falsche Format oder ein zu großes Bild heraus, urteilt das Modell
/// über nichts – und das fällt niemandem auf, weil eine Antwort trotzdem zurückkommt.
/// </summary>
public sealed class WindowsVisionImageEncoderTests : IDisposable
{
    private readonly string _root;
    private readonly WindowsVisionImageEncoder _sut;

    public WindowsVisionImageEncoderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "PictureSorterTests", Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(_root);
        _sut = new WindowsVisionImageEncoder(NullLogger<WindowsVisionImageEncoder>.Instance);
    }

    [Fact]
    public async Task EncodeAsync_ProducesJpeg_EvenThoughTheSourceIsPng()
    {
        // Ausdrücklich kodieren statt umkodieren: Ein Transcoding behielte das
        // Quellformat bei, und genau daran scheitert die Bilderkennung bei HEIC.
        string path = Path.Combine(_root, "bild.png");
        await TestImage.WriteGradientPngAsync(path, 64, 48);

        byte[] jpeg = await _sut.EncodeAsync(path, TestContext.Current.CancellationToken);

        Assert.NotEmpty(jpeg);
        Assert.Equal(await FormatOf(jpeg), BitmapDecoder.JpegDecoderId);
    }

    [Fact]
    public async Task EncodeAsync_ForALargeImage_ShrinksTheLongestEdge()
    {
        // Ein heutiges Handyfoto bringt zweistellige Megabyte mit, die als Base64 noch
        // einmal wachsen. Das Modell rechnet ohnehin kleiner.
        string path = Path.Combine(_root, "gross.png");
        await TestImage.WriteGradientPngAsync(path, 2048, 1024);

        byte[] jpeg = await _sut.EncodeAsync(path, TestContext.Current.CancellationToken);

        (uint width, uint height) = await SizeOf(jpeg);
        Assert.Equal(1024u, width);
        Assert.Equal(512u, height);
    }

    [Fact]
    public async Task EncodeAsync_ForASmallImage_KeepsItsSize()
    {
        // Hochskalieren bringt dem Modell nichts und kostet nur Übertragung.
        string path = Path.Combine(_root, "klein.png");
        await TestImage.WriteGradientPngAsync(path, 120, 90);

        byte[] jpeg = await _sut.EncodeAsync(path, TestContext.Current.CancellationToken);

        (uint width, uint height) = await SizeOf(jpeg);
        Assert.Equal(120u, width);
        Assert.Equal(90u, height);
    }

    [Fact]
    public async Task EncodeAsync_ForAFileThatIsNoImage_ThrowsImageUnreadable()
    {
        // Der Unterschied zählt: Nicht die Bilderkennung fehlt, sondern die Datei ist
        // unlesbar. Nur so wird das Foto übersprungen statt als beurteilt gemerkt.
        string path = Path.Combine(_root, "kein-bild.jpg");
        await File.WriteAllTextAsync(path, "Das ist in Wahrheit Text.", TestContext.Current.CancellationToken);

        _ = await Assert.ThrowsAsync<ImageUnreadableException>(
            () => _sut.EncodeAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task EncodeAsync_ForAMissingFile_ThrowsImageUnreadable()
    {
        string path = Path.Combine(_root, "gibt-es-nicht.jpg");

        _ = await Assert.ThrowsAsync<ImageUnreadableException>(
            () => _sut.EncodeAsync(path, TestContext.Current.CancellationToken));
    }

    private static async Task<Guid> FormatOf(byte[] daten) =>
        await ReadAsync(daten, decoder => decoder.DecoderInformation.CodecId).ConfigureAwait(true);

    private static async Task<(uint Width, uint Height)> SizeOf(byte[] daten) =>
        await ReadAsync(daten, decoder => (decoder.PixelWidth, decoder.PixelHeight)).ConfigureAwait(true);

    // Der Strom muss bis zum Ende des Dekodierens leben, danach wird er freigegeben.
    private static async Task<T> ReadAsync<T>(byte[] daten, Func<BitmapDecoder, T> lesen)
    {
        using InMemoryRandomAccessStream stream = new();
        using (DataWriter writer = new(stream.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(daten);
            _ = await writer.StoreAsync().AsTask().ConfigureAwait(true);
            _ = await writer.FlushAsync().AsTask().ConfigureAwait(true);
            _ = writer.DetachStream();
        }

        BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream).AsTask().ConfigureAwait(true);
        return lesen(decoder);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
