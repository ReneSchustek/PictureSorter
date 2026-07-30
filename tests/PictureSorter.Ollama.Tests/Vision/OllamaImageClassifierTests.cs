using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PictureSorter.Core.Entities;
using PictureSorter.Core.Enums;
using PictureSorter.Core.Exceptions;
using PictureSorter.Core.ValueObjects;
using PictureSorter.Ollama.Tests.Fakes;

namespace PictureSorter.Ollama.Tests.Vision;

/// <summary>
/// Tests der Auswertung des Vision-Urteils. Hier ist ein Fehler besonders teuer:
/// Ein missverstandenes Urteil ist kein Absturz, sondern ein falsch einsortiertes
/// oder stillschweigend übergangenes Foto – dem Nutzer fällt es nie auf. Sprachmodelle
/// halten sich zudem nur ungefähr an das geforderte Format, weshalb die Auswertung
/// die üblichen Abweichungen aushalten muss.
/// </summary>
public sealed class OllamaImageClassifierTests : IDisposable
{
    private readonly string _root;
    private readonly Photo _photo;

    public OllamaImageClassifierTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "PictureSorterTests", Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(_root);

        // Der Klassifikator liest die Datei wirklich (Base64 für die API), deshalb
        // muss sie existieren; ihr Inhalt spielt für die Auswertung keine Rolle.
        string path = Path.Combine(_root, "foto.jpg");
        File.WriteAllBytes(path, [1, 2, 3, 4]);
        _photo = new Photo { FullPath = path, FileName = "foto.jpg" };
    }

    [Fact]
    public async Task ClassifyAsync_WithCleanJsonAnswer_ReturnsVerdict()
    {
        VisionVerdict verdict = await ClassifyAsync("""{"matches": true, "confidence": 0.87, "reason": "Strand"}""");

        Assert.True(verdict.Matches);
        Assert.Equal(0.87, verdict.Confidence, precision: 5);
        Assert.Equal("Strand", verdict.Reason);
    }

    [Fact]
    public async Task ClassifyAsync_WithJsonWrappedInProse_ReturnsVerdict()
    {
        // Modelle stellen dem JSON gern einen Satz voran, trotz gegenteiliger Ansage.
        VisionVerdict verdict = await ClassifyAsync(
            """Gerne! Hier ist mein Urteil: {"matches": true, "confidence": 0.5} Ich hoffe, das hilft.""");

        Assert.True(verdict.Matches);
        Assert.Equal(0.5, verdict.Confidence, precision: 5);
    }

    [Theory]
    [InlineData("""{"matches": "true", "confidence": 0.9}""")]
    [InlineData("""{"matches": "yes", "confidence": 0.9}""")]
    public async Task ClassifyAsync_WithBooleanAsString_IsUnderstoodAsMatch(string answer)
    {
        // Ein häufiger Modell-Ausrutscher: der Wahrheitswert kommt als Zeichenkette.
        // Wird das nicht verstanden, gilt das Foto als „passt nicht" – und der Nutzer
        // sieht nie, dass die KI in Wahrheit zugestimmt hat.
        VisionVerdict verdict = await ClassifyAsync(answer);

        Assert.True(verdict.Matches);
        Assert.Equal(0.9, verdict.Confidence, precision: 5);
    }

    [Fact]
    public async Task ClassifyAsync_WithConfidenceAsString_IsUnderstood()
    {
        VisionVerdict verdict = await ClassifyAsync("""{"matches": true, "confidence": "0.75"}""");

        Assert.Equal(0.75, verdict.Confidence, precision: 5);
    }

    [Theory]
    [InlineData("""{"matches": false, "confidence": 0.9}""")]
    [InlineData("""{"matches": "false", "confidence": 0.9}""")]
    [InlineData("""{"matches": "no", "confidence": 0.9}""")]
    public async Task ClassifyAsync_WithRejection_IsUnderstoodAsNoMatch(string answer) =>
        Assert.False((await ClassifyAsync(answer)).Matches);

    [Theory]
    [InlineData("""{"matches": true, "confidence": 1.5}""", 1.0)]
    [InlineData("""{"matches": true, "confidence": -0.2}""", 0.0)]
    [InlineData("""{"matches": true, "confidence": 95}""", 1.0)]
    public async Task ClassifyAsync_WithConfidenceOutOfRange_IsClamped(string answer, double expected) =>
        Assert.Equal(expected, (await ClassifyAsync(answer)).Confidence, precision: 5);

    [Theory]
    [InlineData("Das Bild zeigt einen Strand.")]
    [InlineData("")]
    [InlineData("{ kaputt")]
    public async Task ClassifyAsync_WithUnparseableAnswer_RejectsInsteadOfGuessing(string answer)
    {
        // Ohne verwertbares Urteil ist Ablehnung die sichere Seite: Das Foto bleibt
        // liegen, statt auf Verdacht verschoben zu werden.
        VisionVerdict verdict = await ClassifyAsync(answer);

        Assert.False(verdict.Matches);
        Assert.Equal(0.0, verdict.Confidence);
    }

    [Fact]
    public async Task ClassifyAsync_WhenAiIsUnavailable_PropagatesTheError()
    {
        // Ein Ausfall der KI darf nicht als „passt nicht" durchgehen – sonst merkt
        // sich die Anwendung eine Ablehnung, die nie ein Modell getroffen hat.
        OllamaImageClassifier sut = CreateSut(new FakeOllamaClient(new AiUnavailableException("aus")));

        _ = await Assert.ThrowsAsync<AiUnavailableException>(
            () => sut.ClassifyAsync(_photo, CreateCategory(), CancellationToken.None));
    }

    [Fact]
    public async Task ClassifyAsync_PassesCategoryDescriptionToTheModel()
    {
        FakeOllamaClient client = new("""{"matches": true, "confidence": 1.0}""");
        OllamaImageClassifier sut = CreateSut(client);

        _ = await sut.ClassifyAsync(_photo, CreateCategory(), CancellationToken.None);

        Assert.Contains("Fotos vom Strand", client.LastPrompt, StringComparison.Ordinal);
    }

    private async Task<VisionVerdict> ClassifyAsync(string modelAnswer) =>
        await CreateSut(new FakeOllamaClient(modelAnswer))
            .ClassifyAsync(_photo, CreateCategory(), CancellationToken.None)
            .ConfigureAwait(false);

    private static OllamaImageClassifier CreateSut(FakeOllamaClient client) => new(
        client,
        new FakeVisionImageEncoder(),
        Options.Create(new OllamaOptions()),
        NullLogger<OllamaImageClassifier>.Instance);

    private static Category CreateCategory() => new("Strand", "Fotos vom Strand", CategoryKind.Topic);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
