using System.Globalization;
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
    private readonly IVisionImageEncoder _encoder;
    private readonly OllamaOptions _options;
    private readonly ILogger<OllamaImageClassifier> _logger;

    /// <summary>
    /// Initialisiert den Klassifikator.
    /// </summary>
    /// <param name="client">Der Ollama-Client.</param>
    /// <param name="encoder">Bereitet das Foto für das Bild-Modell auf.</param>
    /// <param name="options">Die Ollama-Konfiguration.</param>
    /// <param name="logger">Der Logger.</param>
    public OllamaImageClassifier(
        IOllamaClient client,
        IVisionImageEncoder encoder,
        IOptions<OllamaOptions> options,
        ILogger<OllamaImageClassifier> logger)
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
    public async Task<VisionVerdict> ClassifyAsync(
        Photo photo,
        Category category,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(photo);
        ArgumentNullException.ThrowIfNull(category);

        string prompt = BuildPrompt(category.Description, photo.DescribeMetadata());

        // Aufbereitet statt roh: Das Modell liest kein HEIC, und ein Foto in voller
        // Auflösung kostet nur Speicher und Übertragung.
        byte[] jpeg = await _encoder.EncodeAsync(photo.FullPath, cancellationToken).ConfigureAwait(false);
        string imageBase64 = Convert.ToBase64String(jpeg);
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

            bool matches = ReadBoolean(root, "matches");
            double confidence = Math.Clamp(ReadNumber(root, "confidence"), 0.0, 1.0);

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

    // Sprachmodelle halten sich nur ungefähr an das geforderte Format: Der
    // Wahrheitswert kommt mal als true, mal als "true" oder "yes". Wird das nicht
    // verstanden, gilt das Foto still als „passt nicht" – ein Fehlurteil, das dem
    // Nutzer nie auffiele. Alles, was nicht als Zustimmung lesbar ist, bleibt eine
    // Ablehnung (die sichere Seite: das Foto bleibt liegen).
    private static bool ReadBoolean(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out JsonElement element))
        {
            return false;
        }

        return element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.String => element.GetString()?.Trim() is string text
                && (text.Equals("true", StringComparison.OrdinalIgnoreCase)
                    || text.Equals("yes", StringComparison.OrdinalIgnoreCase)
                    || text.Equals("ja", StringComparison.OrdinalIgnoreCase)),
            _ => false,
        };
    }

    // Ebenso die Konfidenz: „0.9" als Zeichenkette ist häufig. Manche Modelle
    // antworten auch in Prozent (95) – das Clamping des Aufrufers fängt das auf.
    private static double ReadNumber(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out JsonElement element))
        {
            return 0.0;
        }

        return element.ValueKind switch
        {
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.String => double.TryParse(
                element.GetString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double value) ? value : 0.0,
            _ => 0.0,
        };
    }

    // Modelle umrahmen das JSON gelegentlich mit Fließtext. Gesucht wird das erste
    // Objekt mit ausgeglichenen Klammern – nicht bis zur letzten Klammer im Text:
    // Folgt dem Urteil noch ein zweites Objekt oder ein Satz mit Klammer, klebte
    // sonst beides zu einem unlesbaren Bereich zusammen.
    private static string? ExtractJsonObject(string text)
    {
        int start = text.IndexOf('{', StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        int depth = 0;
        for (int index = start; index < text.Length; index++)
        {
            depth += text[index] switch { '{' => 1, '}' => -1, _ => 0 };
            if (depth == 0)
            {
                return text[start..(index + 1)];
            }
        }

        return null;
    }
}

/// <summary>
/// Quellgenerierte Logmeldungen des Vision-Klassifikators.
/// </summary>
internal static partial class VisionLog
{
    [LoggerMessage(EventId = 2620, Level = LogLevel.Warning, Message = "Vision-Antwort für {FileName} war nicht auswertbar; als Ablehnung gewertet.")]
    public static partial void Unparseable(ILogger logger, string fileName);
}
