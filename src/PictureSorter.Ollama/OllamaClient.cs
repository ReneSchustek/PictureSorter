using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PictureSorter.Core.Exceptions;

namespace PictureSorter.Ollama;

/// <summary>
/// HTTP-Implementierung von <see cref="IOllamaClient"/> auf Basis eines getypten
/// <see cref="HttpClient"/>. Verbindungsfehler werden in eine
/// <see cref="AiUnavailableException"/> übersetzt, damit die Anwendung den
/// KI-Schritt überspringen kann statt zu blockieren.
/// </summary>
public sealed class OllamaClient : IOllamaClient
{
    private const int MaxAttempts = 3;
    private static readonly TimeSpan BaseRetryDelay = TimeSpan.FromMilliseconds(500);

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _httpClient;
    private readonly OllamaOptions _options;
    private readonly ILogger<OllamaClient> _logger;

    /// <summary>
    /// Initialisiert den Client mit dem getypten <see cref="HttpClient"/>,
    /// der Konfiguration und dem Logger.
    /// </summary>
    /// <param name="httpClient">Der von der Factory bereitgestellte Client.</param>
    /// <param name="options">Die Ollama-Konfiguration.</param>
    /// <param name="logger">Der Logger.</param>
    public OllamaClient(HttpClient httpClient, IOptions<OllamaOptions> options, ILogger<OllamaClient> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<float>> EmbedAsync(
        string model,
        string text,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentNullException.ThrowIfNull(text);

        EmbeddingRequest request = new(model, text, _options.KeepAlive);
        EmbeddingResponse response = await PostAsync<EmbeddingRequest, EmbeddingResponse>(
            "/api/embeddings",
            request,
            cancellationToken).ConfigureAwait(false);

        if (response.Embedding is null || response.Embedding.Count == 0)
        {
            throw new AiUnavailableException("Ollama lieferte ein leeres Embedding zurück.");
        }

        return response.Embedding;
    }

    /// <inheritdoc />
    public async Task<string> GenerateAsync(
        string model,
        string prompt,
        IReadOnlyList<string> imagesBase64,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        ArgumentNullException.ThrowIfNull(imagesBase64);

        GenerateRequest request = new(model, prompt, imagesBase64, Stream: false, _options.KeepAlive);
        GenerateResponse response = await PostAsync<GenerateRequest, GenerateResponse>(
            "/api/generate",
            request,
            cancellationToken).ConfigureAwait(false);

        return response.Response ?? string.Empty;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken)
    {
        TagsResponse response = await ExecuteWithRetryAsync(
            "/api/tags",
            () => GetAsync<TagsResponse>("/api/tags", cancellationToken),
            cancellationToken).ConfigureAwait(false);

        if (response.Models is null)
        {
            return [];
        }

        return [.. response.Models.Select(static model => model.Name).Where(static name => !string.IsNullOrWhiteSpace(name))];
    }

    private Task<TResponse> PostAsync<TRequest, TResponse>(
        string path,
        TRequest request,
        CancellationToken cancellationToken) =>
        ExecuteWithRetryAsync(path, () => SendAsync<TRequest, TResponse>(path, request, cancellationToken), cancellationToken);

    private async Task<TResponse> ExecuteWithRetryAsync<TResponse>(
        string path,
        Func<Task<TResponse>> send,
        CancellationToken cancellationToken)
    {
        // Wiederholung mit exponentiellem Backoff bei vorübergehenden Fehlern.
        // Der Aufrufer-Abbruch wird nie wiederholt.
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return await send().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
                && !cancellationToken.IsCancellationRequested)
            {
                if (attempt >= MaxAttempts)
                {
                    OllamaLog.RequestFailed(_logger, path, ex);
                    throw new AiUnavailableException("Ollama ist nicht erreichbar.", ex);
                }

                OllamaLog.RetryScheduled(_logger, path, attempt, ex);
                TimeSpan delay = BaseRetryDelay * Math.Pow(2, attempt - 1);
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task<TResponse> GetAsync<TResponse>(string path, CancellationToken cancellationToken)
    {
        try
        {
            TResponse? result = await _httpClient
                .GetFromJsonAsync<TResponse>(path, SerializerOptions, cancellationToken)
                .ConfigureAwait(false);

            return result ?? throw new AiUnavailableException("Ollama lieferte keine verwertbare Antwort.");
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            // Unerwartete oder fremde Antwort (z. B. ein anderer Dienst auf dem Port).
            throw new AiUnavailableException("Ollama lieferte eine unerwartete Antwort.", ex);
        }
    }

    private async Task<TResponse> SendAsync<TRequest, TResponse>(
        string path,
        TRequest request,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage message = await _httpClient
            .PostAsJsonAsync(path, request, SerializerOptions, cancellationToken)
            .ConfigureAwait(false);
        _ = message.EnsureSuccessStatusCode();

        TResponse? result = await message.Content
            .ReadFromJsonAsync<TResponse>(SerializerOptions, cancellationToken)
            .ConfigureAwait(false);

        return result ?? throw new AiUnavailableException("Ollama lieferte keine verwertbare Antwort.");
    }

    private sealed record EmbeddingRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("prompt")] string Prompt,
        [property: JsonPropertyName("keep_alive")] string KeepAlive);

    private sealed record EmbeddingResponse(
        [property: JsonPropertyName("embedding")] IReadOnlyList<float>? Embedding);

    private sealed record GenerateRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("prompt")] string Prompt,
        [property: JsonPropertyName("images")] IReadOnlyList<string> Images,
        [property: JsonPropertyName("stream")] bool Stream,
        [property: JsonPropertyName("keep_alive")] string KeepAlive);

    private sealed record GenerateResponse(
        [property: JsonPropertyName("response")] string? Response);

    private sealed record TagsResponse(
        [property: JsonPropertyName("models")] IReadOnlyList<TagModel>? Models);

    private sealed record TagModel(
        [property: JsonPropertyName("name")] string Name);
}

/// <summary>
/// Quellgenerierte Logmeldungen des Ollama-Clients.
/// </summary>
internal static partial class OllamaLog
{
    [LoggerMessage(EventId = 2000, Level = LogLevel.Warning, Message = "Ollama-Anfrage an {Path} endgültig fehlgeschlagen.")]
    public static partial void RequestFailed(ILogger logger, string path, Exception exception);

    [LoggerMessage(EventId = 2001, Level = LogLevel.Debug, Message = "Ollama-Anfrage an {Path} fehlgeschlagen (Versuch {Attempt}); neuer Versuch folgt.")]
    public static partial void RetryScheduled(ILogger logger, string path, int attempt, Exception exception);
}
