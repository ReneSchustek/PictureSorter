using Microsoft.Extensions.Logging.Abstractions;
using PictureSorter.Core.Exceptions;
using PictureSorter.Core.ValueObjects;
using PictureSorter.Imaging;
using PictureSorter.Imaging.Tests.Fixtures;

namespace PictureSorter.Imaging.Tests.Vision;

/// <summary>
/// Prüft das Einlesen für das Bildmodell. Stimmen Maße und Puffer nicht zusammen, schneidet die
/// Aufbereitung das Bild an der falschen Stelle aus, und niemand sieht es.
/// </summary>
public sealed class WindowsScaledImageReaderTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("PictureSorterImaging").FullName;
    private readonly WindowsScaledImageReader _sut = new(NullLogger<WindowsScaledImageReader>.Instance);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task Read_ScalesTheShortEdgeAndKeepsTheRatio()
    {
        string path = Path.Combine(_root, "quer.png");
        await TestImage.WriteGradientPngAsync(path, 100, 50);

        ScaledImage image = await _sut.ReadAsync(path, 20, CancellationToken.None);

        Assert.Equal(40, image.Width);
        Assert.Equal(20, image.Height);
        Assert.Equal(40 * 20 * 4, image.Rgba.Length);
    }

    [Fact]
    public async Task Read_EnlargesASmallImage()
    {
        // Das Modell braucht eine feste Mindestgröße, auch ein kleines Vorschaubild muss sie erreichen.
        string path = Path.Combine(_root, "klein.png");
        await TestImage.WriteGradientPngAsync(path, 10, 10);

        ScaledImage image = await _sut.ReadAsync(path, 20, CancellationToken.None);

        Assert.Equal(20, image.Width);
        Assert.Equal(20, image.Height);
    }

    [Fact]
    public async Task Read_WithAnUnreadableFile_ReportsTheImageAsUnreadable()
    {
        string path = Path.Combine(_root, "kaputt.jpg");
        await File.WriteAllTextAsync(path, "kein Bild", TestContext.Current.CancellationToken);

        _ = await Assert.ThrowsAsync<ImageUnreadableException>(() => _sut.ReadAsync(path, 20, CancellationToken.None));
    }

    [Fact]
    public async Task Read_WithoutAPositiveSize_IsRefused()
    {
        _ = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _sut.ReadAsync("a.jpg", 0, CancellationToken.None));
    }
}
