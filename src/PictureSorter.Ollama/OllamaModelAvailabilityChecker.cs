using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PictureSorter.Core.Exceptions;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Ollama;

/// <summary>
/// Prüft über die Ollama-API, ob die konfigurierten Vision- und Embedding-Modelle
/// installiert sind. Ist Ollama nicht erreichbar, wird dies als nicht erreichbar
/// gemeldet statt eine Ausnahme nach außen zu reichen.
/// </summary>
public sealed class OllamaModelAvailabilityChecker : IModelAvailabilityChecker
{
    private readonly IOllamaClient _client;
    private readonly OllamaOptions _options;
    private readonly ILogger<OllamaModelAvailabilityChecker> _logger;

    /// <summary>
    /// Initialisiert den Prüfer.
    /// </summary>
    /// <param name="client">Der Ollama-Client.</param>
    /// <param name="options">Die Ollama-Konfiguration (für die Modellnamen).</param>
    /// <param name="logger">Der Logger.</param>
    public OllamaModelAvailabilityChecker(
        IOllamaClient client,
        IOptions<OllamaOptions> options,
        ILogger<OllamaModelAvailabilityChecker> logger)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ModelAvailability> CheckAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<string> required = [_options.VisionModel, _options.EmbeddingModel];

        // Eigenes, kurzes Zeitlimit statt des Limits einer Bildbeschreibung: Ein Ollama,
        // das die Verbindung annimmt, aber nicht antwortet – etwa während seiner eigenen
        // Aktualisierung – hielte die Oberfläche sonst über Minuten bei „Zustand der KI
        // wird geprüft" fest, weil das lange Limit noch dreimal wiederholt wird.
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(_options.AvailabilityTimeoutSeconds));

        try
        {
            IReadOnlyList<string> installed = await _client.ListModelsAsync(deadline.Token).ConfigureAwait(false);
            IReadOnlyList<string> missing = [.. required.Where(model => !IsInstalled(model, installed))];

            if (missing.Count > 0)
            {
                ModelLog.Missing(_logger, string.Join(", ", missing));
            }

            return new ModelAvailability { IsReachable = true, RequiredModels = required, MissingModels = missing };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Nur das eigene Zeitlimit ist abgelaufen; ein Abbruch durch den Aufrufer
            // bleibt ein Abbruch und wird weitergereicht.
            ModelLog.TimedOut(_logger, _options.AvailabilityTimeoutSeconds);
            return Unreachable(required);
        }
        catch (Exception ex) when (ex is AiUnavailableException or HttpRequestException or InvalidOperationException)
        {
            // Der Prüfer gibt immer eine Antwort. Käme hier eine Ausnahme heraus, bliebe
            // in der Oberfläche der Text „wird geprüft" stehen – ohne dass die Nutzerin
            // je erführe, dass die Prüfung längst gescheitert ist.
            ModelLog.Unreachable(_logger, ex);
            return Unreachable(required);
        }
    }

    private static ModelAvailability Unreachable(IReadOnlyList<string> required) =>
        new() { IsReachable = false, RequiredModels = required, MissingModels = required };

    // Ollama meldet Modelle mit Tag (z. B. „llava:latest"). Ein ohne Tag
    // konfiguriertes Modell („llava") gilt als vorhanden, wenn ein installiertes
    // Modell denselben Basisnamen trägt.
    private static bool IsInstalled(string requiredModel, IReadOnlyList<string> installed) =>
        installed.Any(name =>
            string.Equals(name, requiredModel, StringComparison.OrdinalIgnoreCase)
            || string.Equals(BaseName(name), BaseName(requiredModel), StringComparison.OrdinalIgnoreCase));

    private static string BaseName(string model)
    {
        int separator = model.IndexOf(':', StringComparison.Ordinal);
        return separator < 0 ? model : model[..separator];
    }
}

/// <summary>
/// Quellgenerierte Logmeldungen des Modell-Prüfers.
/// </summary>
internal static partial class ModelLog
{
    [LoggerMessage(EventId = 2600, Level = LogLevel.Warning, Message = "Ollama erreichbar, aber Modelle fehlen: {Missing}.")]
    public static partial void Missing(ILogger logger, string missing);

    [LoggerMessage(EventId = 2601, Level = LogLevel.Warning, Message = "Ollama ist beim Start nicht erreichbar.")]
    public static partial void Unreachable(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 2602, Level = LogLevel.Warning, Message = "Ollama hat die Verfügbarkeitsprüfung nicht innerhalb von {Seconds} Sekunden beantwortet.")]
    public static partial void TimedOut(ILogger logger, int seconds);
}
