using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PictureSorter.Core.Entities;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Ollama;

/// <summary>
/// Beurteilt Grenzfälle mit dem Vision-Modell. Das Modell wird angewiesen, ein
/// kompaktes JSON-Urteil zu liefern, das maschinell ausgewertet wird
/// (Vorgabe: strukturierte statt freitextliche KI-Ausgabe).
/// </summary>
public sealed class OllamaImageClassifier : IImageClassifier
{
    private readonly IOllamaClient _client;
    private readonly OllamaOptions _options;
    private readonly ILogger<OllamaImageClassifier> _logger;

    /// <summary>
    /// Initialisiert den Klassifikator.
    /// </summary>
    /// <param name="client">Der Ollama-Client.</param>
    /// <param name="options">Die Ollama-Konfiguration.</param>
    /// <param name="logger">Der Logger.</param>
    public OllamaImageClassifier(
        IOllamaClient client,
        IOptions<OllamaOptions> options,
        ILogger<OllamaImageClassifier> logger)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<VisionVerdict> ClassifyAsync(
        Photo photo,
        Category category,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(photo);
        ArgumentNullException.ThrowIfNull(category);

        string prompt = BuildPrompt(category.Description, photo.DescribeMetadata());
        string imageBase64 = await ImagePayload.ToBase64Async(photo.FullPath, cancellationToken).ConfigureAwait(false);
        string answer = await _client
            .GenerateAsync(_options.VisionModel, prompt, [imageBase64], cancellationToken)
            .ConfigureAwait(false);

        return ParseVerdict(answer, photo.FileName);
    }

    private static string BuildPrompt(string categoryDescription, string metadata)
    {
        string metadataLine = string.IsNullOrWhiteSpace(metadata)
            ? string.Empty
            : $"Bekannte Bildinformationen: {metadata} ";

        return "Du prüfst, ob ein Foto zu einer Kategorie gehört. "
            + $"Kategorie-Beschreibung: \"{categoryDescription}\". "
            + metadataLine
            + "Berücksichtige sowohl das Bild als auch die bekannten Bildinformationen. "
            + "Antworte ausschließlich mit einem JSON-Objekt der Form "
            + "{\"matches\": true|false, \"confidence\": 0.0-1.0, \"reason\": \"kurz\"}. "
            + "Kein weiterer Text.";
    }

    private VisionVerdict ParseVerdict(string answer, string fileName)
    {
        string? json = ExtractJsonObject(answer);
        if (json is null)
        {
            VisionLog.Unparseable(_logger, fileName);
            return new VisionVerdict { Matches = false, Confidence = 0.0, Reason = "Unklare Modellantwort." };
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            bool matches = root.TryGetProperty("matches", out JsonElement matchesElement)
                && matchesElement.ValueKind is JsonValueKind.True or JsonValueKind.False
                && matchesElement.GetBoolean();

            double confidence = root.TryGetProperty("confidence", out JsonElement confidenceElement)
                && confidenceElement.ValueKind == JsonValueKind.Number
                    ? Math.Clamp(confidenceElement.GetDouble(), 0.0, 1.0)
                    : 0.0;

            string? reason = root.TryGetProperty("reason", out JsonElement reasonElement)
                && reasonElement.ValueKind == JsonValueKind.String
                    ? reasonElement.GetString()
                    : null;

            return new VisionVerdict { Matches = matches, Confidence = confidence, Reason = reason };
        }
        catch (JsonException)
        {
            VisionLog.Unparseable(_logger, fileName);
            return new VisionVerdict { Matches = false, Confidence = 0.0, Reason = "Unklare Modellantwort." };
        }
    }

    // Modelle umrahmen das JSON gelegentlich mit Fließtext; das erste vollständige
    // Objekt zwischen den äußersten geschweiften Klammern wird extrahiert.
    private static string? ExtractJsonObject(string text)
    {
        int start = text.IndexOf('{', StringComparison.Ordinal);
        int end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return null;
        }

        return text.Substring(start, end - start + 1);
    }
}

/// <summary>
/// Quellgenerierte Logmeldungen des Vision-Klassifikators.
/// </summary>
internal static partial class VisionLog
{
    [LoggerMessage(EventId = 2200, Level = LogLevel.Warning, Message = "Vision-Antwort für {FileName} war nicht auswertbar; als Ablehnung gewertet.")]
    public static partial void Unparseable(ILogger logger, string fileName);
}
