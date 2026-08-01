using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PictureSorter.Core.Entities;
using PictureSorter.Core.Exceptions;
using PictureSorter.Core.ValueObjects;
using PictureSorter.Ollama;
using PictureSorter.Ollama.Tests.Fakes;

namespace PictureSorter.Ollama.Tests.Embedding;

/// <summary>
/// Tests des Embedding-Wegs. Der erzeugte Vektor entscheidet, welche Fotos die
/// Vorsortierung überhaupt in dieselbe Nachbarschaft legt – geht hier etwas verloren
/// (das aufbereitete Bild, die Metadaten, der Modellname), sortiert die Anwendung
/// still nach etwas anderem als der Nutzer erwartet.
/// </summary>
public sealed class OllamaEmbeddingProviderTests
{
    private const string Beschreibung = "Zwei Kinder am Strand, sonniger Nachmittag.";

    [Fact]
    public async Task CreateEmbeddingAsync_SendsThePreparedImageToTheVisionModel()
    {
        // Das Bild-Modell bekommt nie die Rohdatei: Ein Handyfoto ist ein
        // HEIC-Container, den es nicht öffnen kann.
        FakeOllamaClient client = new(Beschreibung);
        FakeVisionImageEncoder encoder = new();
        OllamaEmbeddingProvider sut = CreateSut(client, encoder);

        _ = await sut.CreateEmbeddingAsync(Foto(), TestContext.Current.CancellationToken);

        Assert.Equal(@"C:\Fotos\strand.heic", encoder.LastPath);
        _ = Assert.Single(client.LastImages);
    }

    [Fact]
    public async Task CreateEmbeddingAsync_CombinesDescriptionAndMetadata()
    {
        // Aufnahmedatum, Ort und Kamera gehören mit in den Vektor: Sonst landen
        // Fotos desselben Anlasses nur zusammen, wenn sie auch gleich aussehen.
        FakeOllamaClient client = new(Beschreibung);
        OllamaEmbeddingProvider sut = CreateSut(client, new FakeVisionImageEncoder());

        _ = await sut.CreateEmbeddingAsync(Foto(), TestContext.Current.CancellationToken);

        Assert.Contains(Beschreibung, client.LastEmbeddedText, StringComparison.Ordinal);
        Assert.Contains("14.07.2025", client.LastEmbeddedText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateEmbeddingAsync_WithoutMetadata_EmbedsTheDescriptionAlone()
    {
        // Ohne Metadaten darf kein leerer Anhang entstehen – der verschöbe jeden
        // Vektor um denselben Betrag und verwässerte den Vergleich.
        FakeOllamaClient client = new(Beschreibung);
        OllamaEmbeddingProvider sut = CreateSut(client, new FakeVisionImageEncoder());
        Photo withoutMetadata = new() { FullPath = @"C:\Fotos\a.jpg", FileName = "a.jpg", SizeBytes = 1 };

        _ = await sut.CreateEmbeddingAsync(withoutMetadata, TestContext.Current.CancellationToken);

        Assert.Equal(Beschreibung, client.LastEmbeddedText);
    }

    [Fact]
    public async Task CreateEmbeddingAsync_RecordsTheModelThatProducedTheVector()
    {
        // Der Modellname wandert in den Cache-Schlüssel: Ohne ihn würde nach einem
        // Modellwechsel weiter mit den alten, unvergleichbaren Vektoren gerechnet.
        FakeOllamaClient client = new(Beschreibung);
        OllamaEmbeddingProvider sut = CreateSut(client, new FakeVisionImageEncoder());

        ImageEmbedding embedding = await sut.CreateEmbeddingAsync(Foto(), TestContext.Current.CancellationToken);

        Assert.Equal("nomic-embed-text", embedding.Model);
        Assert.Equal("nomic-embed-text", client.LastEmbeddingModel);
    }

    [Fact]
    public async Task CreateEmbeddingAsync_WhenTheImageCannotBePrepared_DoesNotEmbedAnything()
    {
        // Fehlt der Codec, gibt es kein Bild – dann darf auch kein Vektor entstehen,
        // der auf einer Beschreibung ohne Bildgrundlage beruht.
        FakeOllamaClient client = new(Beschreibung);
        FakeVisionImageEncoder encoder = new(new ImageUnreadableException(@"C:\Fotos\strand.heic"));
        OllamaEmbeddingProvider sut = CreateSut(client, encoder);

        _ = await Assert.ThrowsAsync<ImageUnreadableException>(
            () => sut.CreateEmbeddingAsync(Foto(), TestContext.Current.CancellationToken));

        Assert.Null(client.LastEmbeddedText);
    }

    private static Photo Foto() => new()
    {
        FullPath = @"C:\Fotos\strand.heic",
        FileName = "strand.heic",
        SizeBytes = 4096,
        CapturedAt = new DateTimeOffset(2025, 7, 14, 15, 30, 0, TimeSpan.Zero),
        CameraModel = "iPhone 15",
    };

    private static OllamaEmbeddingProvider CreateSut(FakeOllamaClient client, FakeVisionImageEncoder encoder) =>
        new(
            client,
            encoder,
            Options.Create(new OllamaOptions()),
            NullLogger<OllamaEmbeddingProvider>.Instance);
}
