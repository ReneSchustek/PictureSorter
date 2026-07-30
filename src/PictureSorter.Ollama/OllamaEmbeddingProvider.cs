using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PictureSorter.Core.Entities;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Ollama;

/// <summary>
/// Erzeugt Embeddings, indem das Vision-Modell das Bild zunächst kurz beschreibt
/// und das Embedding-Modell diese Beschreibung anschließend in einen Vektor
/// überführt. So werden Bildinhalte für den Ähnlichkeitsvergleich nutzbar.
/// </summary>
public sealed class OllamaEmbeddingProvider : IEmbeddingProvider
{
    private const string DescribePrompt =
        "Beschreibe den Inhalt dieses Fotos sachlich in ein bis zwei Sätzen. "
        + "Nenne Personen, Orte, Anlass und Stimmung, soweit erkennbar.";

    private readonly IOllamaClient _client;
    private readonly IVisionImageEncoder _encoder;
    private readonly OllamaOptions _options;
    private readonly ILogger<OllamaEmbeddingProvider> _logger;

    /// <summary>
    /// Initialisiert den Provider.
    /// </summary>
    /// <param name="client">Der Ollama-Client.</param>
    /// <param name="encoder">Bereitet das Foto für das Bild-Modell auf.</param>
    /// <param name="options">Die Ollama-Konfiguration.</param>
    /// <param name="logger">Der Logger.</param>
    public OllamaEmbeddingProvider(
        IOllamaClient client,
        IVisionImageEncoder encoder,
        IOptions<OllamaOptions> options,
        ILogger<OllamaEmbeddingProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(encoder);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _client = client;
        _encoder = encoder;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ImageEmbedding> CreateEmbeddingAsync(Photo photo, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(photo);

        EmbeddingLog.Embedding(_logger, photo.FileName);

        // Auch die Vorsortierung sieht das Bild nur über das Bild-Modell. Ohne
        // Aufbereitung bekäme es bei einem Handyfoto einen HEIC-Container, den es nicht
        // öffnen kann – und der Vergleichsvektor entstünde aus einer Beschreibung, die
        // auf keinem Bild beruht.
        byte[] jpeg = await _encoder.EncodeAsync(photo.FullPath, cancellationToken).ConfigureAwait(false);
        string imageBase64 = Convert.ToBase64String(jpeg);
        string description = await _client
            .GenerateAsync(_options.VisionModel, DescribePrompt, [imageBase64], cancellationToken)
            .ConfigureAwait(false);

        // Bildinhalt UND Metadaten (Datum, Ort, Kamera) bestimmen gemeinsam den
        // Vergleichsvektor, damit z. B. Aufnahmeort und -zeit die Ähnlichkeit prägen.
        string embeddingText = CombineWithMetadata(description, photo);
        IReadOnlyList<float> vector = await _client
            .EmbedAsync(_options.EmbeddingModel, embeddingText, cancellationToken)
            .ConfigureAwait(false);

        return new ImageEmbedding(vector, _options.EmbeddingModel);
    }

    private static string CombineWithMetadata(string description, Photo photo)
    {
        string metadata = photo.DescribeMetadata();
        return string.IsNullOrWhiteSpace(metadata) ? description : $"{description}\n{metadata}";
    }
}

/// <summary>
/// Quellgenerierte Logmeldungen des Embedding-Providers.
/// </summary>
internal static partial class EmbeddingLog
{
    [LoggerMessage(EventId = 2100, Level = LogLevel.Debug, Message = "Erzeuge Embedding für {FileName}.")]
    public static partial void Embedding(ILogger logger, string fileName);
}
