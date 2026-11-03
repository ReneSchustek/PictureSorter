using Microsoft.Extensions.Logging.Abstractions;
using PictureSorter.Core.ValueObjects;
using PictureSorter.Imaging.Tests.Fixtures;

namespace PictureSorter.Imaging.Tests.Metadata;

/// <summary>
/// Tests des EXIF-Lesers. Aufnahmedatum und Ort fließen in die Ordnernamen von
/// Ereignis-Kategorien und in die Beschreibung, die die KI bewertet. Ein falsch
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
    public async Task ReadAsync_KeepsTheWallClockOfTheCamera()
    {
        // So schreibt es eine echte Kamera: Ortszeit ohne Zeitzone, hier kurz nach Mitternacht.
        // Als UTC gelesen läge das Foto auf dem Vortag, im falschen Zeitraum und im falschen Ordner.
        // Sichtbar nur auf einem Rechner östlich von Greenwich, auf dem Fließband steht UTC.
        string path = await WriteJpegAsync([48, 8, 30], "N", [11, 34, 0], "E");
        byte[] jpeg = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(
            path,
            WithCameraExif(jpeg, "2026:07:04 00:30:00"),
            TestContext.Current.CancellationToken);

        PhotoMetadata? metadata = await _sut.ReadAsync(path, CancellationToken.None);

        Assert.Equal(new DateTime(2026, 7, 4, 0, 30, 0), metadata!.CapturedAt!.Value.DateTime);

        // Die Anzeige rechnet in die Ortszeit um. Sie muss dieselbe Uhrzeit zeigen, nach der einsortiert wird.
        Assert.Equal(new DateTime(2026, 7, 4, 0, 30, 0), metadata.CapturedAt.Value.ToLocalTime().DateTime);
    }

    [Fact]
    public async Task ReadAsync_ConvertsCoordinatesFromDegreesMinutesSeconds()
    {
        // 48° 8′ 30″ N = 48,141667°. Die Bild-API liefert den Wert nicht als fertige
        // Dezimalzahl; wer ihn so abfragt, bekommt nie einen Ort. Der Test hält fest, dass
        // der Ort ankommt.
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
        await File.WriteAllTextAsync(path, "Das ist in Wahrheit Text.", TestContext.Current.CancellationToken);

        Assert.Null(await _sut.ReadAsync(path, CancellationToken.None));
    }

    [Fact]
    public async Task ReadAsync_ForMissingFile_ReturnsNull() =>
        Assert.Null(await _sut.ReadAsync(Path.Combine(_root, "fehlt.jpg"), CancellationToken.None));

    [Fact]
    public async Task ReadAsync_WithoutPath_IsRejected() =>
        await Assert.ThrowsAsync<ArgumentException>(() => _sut.ReadAsync(" ", CancellationToken.None));

    // Ersetzt alle APP1-Blöcke durch einen von Hand gebauten EXIF-Block mit genau einem Aufnahmedatum.
    private static byte[] WithCameraExif(byte[] jpeg, string dateTimeOriginal)
    {
        byte[] date = [.. System.Text.Encoding.ASCII.GetBytes(dateTimeOriginal), 0];
        byte[] tiff =
        [
            (byte)'I', (byte)'I', 42, 0, 8, 0, 0, 0,
            1, 0, 0x69, 0x87, 4, 0, 1, 0, 0, 0, 26, 0, 0, 0, 0, 0, 0, 0,
            1, 0, 0x03, 0x90, 2, 0, (byte)date.Length, 0, 0, 0, 44, 0, 0, 0, 0, 0, 0, 0,
            .. date,
        ];
        byte[] header = [.. "Exif"u8, 0, 0];
        int length = 2 + header.Length + tiff.Length;

        List<byte> result = [jpeg[0], jpeg[1], 0xFF, 0xE1, (byte)(length >> 8), (byte)length, .. header, .. tiff];

        int position = 2;
        while (jpeg[position] == 0xFF && jpeg[position + 1] != 0xDA)
        {
            int segment = 2 + (jpeg[position + 2] << 8) + jpeg[position + 3];
            if (jpeg[position + 1] != 0xE1)
            {
                result.AddRange(jpeg.AsSpan(position, segment));
            }

            position += segment;
        }

        result.AddRange(jpeg.AsSpan(position));
        return [.. result];
    }

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
