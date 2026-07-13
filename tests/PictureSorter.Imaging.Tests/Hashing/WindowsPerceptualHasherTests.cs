using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using PictureSorter.Core.ValueObjects;
using PictureSorter.Imaging.Tests.Fixtures;

namespace PictureSorter.Imaging.Tests.Hashing;

/// <summary>
/// Tests der Fingerabdrücke, auf denen die Duplikat-Erkennung beruht. Ein Fehler
/// hier führt dazu, dass die Nutzerin Fotos in den Papierkorb legt, die keine
/// Duplikate sind – der Weg zum Datenverlust. Geprüft wird deshalb beides: dass
/// gleiche Bilder gleich erkannt werden und dass verschiedene deutlich auseinander
/// liegen.
/// </summary>
public sealed class WindowsPerceptualHasherTests : IDisposable
{
    private readonly string _root;
    private readonly WindowsPerceptualHasher _sut = new(NullLogger<WindowsPerceptualHasher>.Instance);

    public WindowsPerceptualHasherTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "PictureSorterTests", Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task ComputeAsync_ReturnsSha256OfTheFileContent()
    {
        string path = Path.Combine(_root, "bild.png");
        await TestImage.WriteGradientPngAsync(path, 32, 32);
        string expected = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path)));

        ImageFingerprint fingerprint = await _sut.ComputeAsync(path, CancellationToken.None);

        Assert.Equal(expected, fingerprint.ContentHash);
        Assert.Equal(path, fingerprint.FilePath);
    }

    [Fact]
    public async Task ComputeAsync_ForDecodableImage_ProducesAPerceptualHash()
    {
        string path = Path.Combine(_root, "bild.png");
        await TestImage.WriteGradientPngAsync(path, 32, 32);

        ImageFingerprint fingerprint = await _sut.ComputeAsync(path, CancellationToken.None);

        _ = Assert.NotNull(fingerprint.Perceptual);
    }

    [Fact]
    public async Task ComputeAsync_ForIdenticalContent_ProducesIdenticalHashes()
    {
        string first = Path.Combine(_root, "a.png");
        await TestImage.WriteGradientPngAsync(first, 32, 32);
        string second = Path.Combine(_root, "b.png");
        File.Copy(first, second);

        ImageFingerprint one = await _sut.ComputeAsync(first, CancellationToken.None);
        ImageFingerprint two = await _sut.ComputeAsync(second, CancellationToken.None);

        Assert.Equal(one.ContentHash, two.ContentHash);
        Assert.Equal(one.Perceptual, two.Perceptual);
    }

    [Fact]
    public async Task ComputeAsync_ForSameMotifAtDifferentSize_ProducesNearbyPerceptualHashes()
    {
        // Dasselbe Motiv, neu skaliert: bit-verschieden, aber visuell gleich. Genau
        // das soll der Wahrnehmungs-Hash zusammenführen, wo der Inhalts-Hash aufgibt.
        string small = Path.Combine(_root, "klein.png");
        await TestImage.WriteGradientPngAsync(small, 32, 32);
        string large = Path.Combine(_root, "gross.png");
        await TestImage.WriteGradientPngAsync(large, 128, 128);

        ImageFingerprint one = await _sut.ComputeAsync(small, CancellationToken.None);
        ImageFingerprint two = await _sut.ComputeAsync(large, CancellationToken.None);

        Assert.NotEqual(one.ContentHash, two.ContentHash);
        Assert.Equal(0, one.Perceptual!.Value.DistanceTo(two.Perceptual!.Value));
    }

    [Fact]
    public async Task ComputeAsync_ForMirroredMotif_ProducesDistantPerceptualHashes()
    {
        // Der Verlauf läuft andersherum: Für das Auge ein anderes Bild, und der Hash
        // muss das auch so sehen – sonst würden verschiedene Fotos als Duplikate
        // vorgeschlagen und landeten im Papierkorb.
        string rising = Path.Combine(_root, "hell-rechts.png");
        await TestImage.WriteGradientPngAsync(rising, 32, 32);
        string falling = Path.Combine(_root, "hell-links.png");
        await TestImage.WriteGradientPngAsync(falling, 32, 32, invert: true);

        ImageFingerprint one = await _sut.ComputeAsync(rising, CancellationToken.None);
        ImageFingerprint two = await _sut.ComputeAsync(falling, CancellationToken.None);

        // 8×8 = 64 Bit; jedes Bit vergleicht zwei benachbarte Spalten. Der gespiegelte
        // Verlauf dreht jeden dieser Vergleiche um.
        Assert.Equal(64, one.Perceptual!.Value.DistanceTo(two.Perceptual!.Value));
    }

    [Fact]
    public async Task ComputeAsync_ForUndecodableFile_KeepsContentHashAndOmitsPerceptualHash()
    {
        // Eine kaputte oder falsch benannte Datei darf den Duplikat-Lauf nicht
        // abbrechen: Der exakte Vergleich bleibt möglich, der visuelle entfällt.
        string path = Path.Combine(_root, "kein-bild.jpg");
        await File.WriteAllTextAsync(path, "Das ist in Wahrheit Text.");

        ImageFingerprint fingerprint = await _sut.ComputeAsync(path, CancellationToken.None);

        Assert.NotEmpty(fingerprint.ContentHash);
        Assert.Null(fingerprint.Perceptual);
    }

    [Fact]
    public async Task ComputeAsync_WithoutPath_IsRejected() =>
        await Assert.ThrowsAsync<ArgumentException>(
            () => _sut.ComputeAsync(" ", CancellationToken.None));

    [Fact]
    public async Task ComputeAsync_ForMissingFile_Throws() =>
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => _sut.ComputeAsync(Path.Combine(_root, "fehlt.png"), CancellationToken.None));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
