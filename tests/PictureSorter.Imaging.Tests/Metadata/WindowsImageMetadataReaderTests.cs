using Microsoft.Extensions.Logging.Abstractions;
using PictureSorter.Core.ValueObjects;
using PictureSorter.Imaging.Tests.Fixtures;

namespace PictureSorter.Imaging.Tests.Metadata;

/// <summary>
/// Tests des EXIF-Lesers. Aufnahmedatum und Ort fließen in die Ordnernamen von
/// Ereignis-Kategorien und in die Beschreibung, die die KI bewertet – ein falsch
/// gelesener Wert landet also sichtbar im Dateisystem. Die Koordinaten sind dabei
/// der heikelste Teil: EXIF legt sie als drei Brüche plus Himmelsrichtung ab, und
/// die Bild-API gibt Zähler und Nenner getrennt heraus.
/// </summary>
public sealed class WindowsImageMetadataReaderTests : IDisposable
{
    private static readonly DateTimeOffset Captured = new(2026, 7, 4, 15, 30, 0, TimeSpan.Zero);

    private readonly string _root;
    private readonly WindowsImageMetadataReader _sut = new(NullLogger<WindowsImageMetadataReader>.Instance);

    public WindowsImageMetadataReaderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "PictureSorterTests", Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task ReadAsync_ReadsDimensionsFromFormatWithoutExif()
    {
        // PNG kennt keinen EXIF-Block: Die Abmessungen müssen trotzdem ankommen,
        // und die übrigen Felder bleiben schlicht leer.
        string path = Path.Combine(_root, "bild.png");
        await TestImage.WriteGradientPngAsync(path, 40, 25);

        PhotoMetadata? metadata = await _sut.ReadAsync(path, CancellationToken.None);

        Assert.NotNull(metadata);
        Assert.Equal(40, metadata.Width);
        Assert.Equal(25, metadata.Height);
        Assert.Null(metadata.CapturedAt);
        Assert.Null(metadata.CameraModel);
        Assert.Null(metadata.Latitude);
    }

    [Fact]
    public async Task ReadAsync_ReadsCaptureDateAndCamera()
    {
        string path = await WriteJpegAsync([48, 8, 30], "N", [11, 34, 0], "E");

        PhotoMetadata? metadata = await _sut.ReadAsync(path, CancellationToken.None);

        Assert.NotNull(metadata);
        Assert.Equal("Pixel 9", metadata.CameraModel);

        // EXIF speichert die Ortszeit ohne Zeitzone; verglichen wird deshalb der
        // Zeitpunkt in der lokalen Schreibweise.
        Assert.Equal(Captured.DateTime, metadata.CapturedAt!.Value.DateTime);
    }

    [Fact]
    public async Task ReadAsync_ConvertsCoordinatesFromDegreesMinutesSeconds()
    {
        // 48° 8′ 30″ N = 48,141667°. Die Anwendung fragte diesen Wert lange als
        // fertige Dezimalzahl ab – die liefert die Bild-API aber nicht, sodass nie
        // ein Ort ankam. Der Test hält fest, dass er jetzt ankommt.
        string path = await WriteJpegAsync([48, 8, 30], "N", [11, 34, 0], "E");

        PhotoMetadata? metadata = await _sut.ReadAsync(path, CancellationToken.None);

        Assert.Equal(48.141667, metadata!.Latitude!.Value, precision: 5);
        Assert.Equal(11.566667, metadata.Longitude!.Value, precision: 5);
    }

    [Fact]
    public async Task ReadAsync_ForSouthAndWest_ReturnsNegativeCoordinates()
    {
        // Südlich des Äquators und westlich von Greenwich ist das Vorzeichen negativ.
        // Ein vergessenes Minus verlegt das Foto auf die andere Erdhalbkugel.
        string path = await WriteJpegAsync([33, 51, 54], "S", [70, 39, 12], "W");

        PhotoMetadata? metadata = await _sut.ReadAsync(path, CancellationToken.None);

        Assert.Equal(-33.865, metadata!.Latitude!.Value, precision: 3);
        Assert.Equal(-70.653333, metadata.Longitude!.Value, precision: 5);
    }

    [Fact]
    public async Task ReadAsync_ForUnreadableFile_ReturnsNullInsteadOfThrowing()
    {
        // Metadaten sind optional: Eine kaputte Datei darf den Scan über tausende
        // Fotos nicht abbrechen.
        string path = Path.Combine(_root, "kein-bild.jpg");
        await File.WriteAllTextAsync(path, "Das ist in Wahrheit Text.");

        Assert.Null(await _sut.ReadAsync(path, CancellationToken.None));
    }

    [Fact]
    public async Task ReadAsync_ForMissingFile_ReturnsNull() =>
        Assert.Null(await _sut.ReadAsync(Path.Combine(_root, "fehlt.jpg"), CancellationToken.None));

    [Fact]
    public async Task ReadAsync_WithoutPath_IsRejected() =>
        await Assert.ThrowsAsync<ArgumentException>(() => _sut.ReadAsync(" ", CancellationToken.None));

    private async Task<string> WriteJpegAsync(
        uint[] latitude,
        string latitudeRef,
        uint[] longitude,
        string longitudeRef)
    {
        string path = Path.Combine(_root, "foto.jpg");
        await TestImage
            .WriteJpegWithExifAsync(path, Captured, "Pixel 9", latitude, latitudeRef, longitude, longitudeRef)
            .ConfigureAwait(false);

        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
