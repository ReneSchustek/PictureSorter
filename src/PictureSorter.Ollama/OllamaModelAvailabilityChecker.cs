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

        try
        {
            IReadOnlyList<string> installed = await _client.ListModelsAsync(cancellationToken).ConfigureAwait(false);
            IReadOnlyList<string> missing = [.. required.Where(model => !IsInstalled(model, installed))];

            if (missing.Count > 0)
            {
                ModelLog.Missing(_logger, string.Join(", ", missing));
            }

            return new ModelAvailability { IsReachable = true, RequiredModels = required, MissingModels = missing };
        }
        catch (AiUnavailableException ex)
        {
            ModelLog.Unreachable(_logger, ex);
            return new ModelAvailability { IsReachable = false, RequiredModels = required, MissingModels = required };
        }
    }

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
}
