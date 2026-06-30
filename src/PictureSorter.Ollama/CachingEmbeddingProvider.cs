using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PictureSorter.Core.Entities;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Ollama;

/// <summary>
/// Legt sich um einen echten <see cref="IEmbeddingProvider"/> und bedient
/// gleiche, unveränderte Fotos aus dem <see cref="IEmbeddingCache"/>. Der
/// Schlüssel umfasst Pfad, Dateigröße, Änderungszeit sowie die verwendeten
/// Modelle – ändert sich davon etwas, wird neu berechnet.
/// </summary>
public sealed class CachingEmbeddingProvider : IEmbeddingProvider
{
    private readonly IEmbeddingProvider _inner;
    private readonly IEmbeddingCache _cache;
    private readonly OllamaOptions _options;
    private readonly ILogger<CachingEmbeddingProvider> _logger;

    /// <summary>
    /// Initialisiert den Decorator.
    /// </summary>
    /// <param name="inner">Der echte Embedding-Provider.</param>
    /// <param name="cache">Der Embedding-Cache.</param>
    /// <param name="options">Ollama-Optionen (Modellnamen für den Schlüssel).</param>
    /// <param name="logger">Der Logger.</param>
    public CachingEmbeddingProvider(
        IEmbeddingProvider inner,
        IEmbeddingCache cache,
        IOptions<OllamaOptions> options,
        ILogger<CachingEmbeddingProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _inner = inner;
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ImageEmbedding> CreateEmbeddingAsync(Photo photo, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(photo);

        string key = BuildKey(photo);
        ImageEmbedding? cached = await _cache.GetAsync(key, cancellationToken).ConfigureAwait(false);
        if (cached is not null)
        {
            CachingEmbeddingLog.CacheHit(_logger, photo.FileName);
            return cached;
        }

        ImageEmbedding embedding = await _inner.CreateEmbeddingAsync(photo, cancellationToken).ConfigureAwait(false);
        await _cache.SetAsync(key, embedding, cancellationToken).ConfigureAwait(false);
        return embedding;
    }

    // Schlüssel = Hash über Pfad, Größe, Änderungszeit und Modelle. Ändert sich die
    // Datei oder ein Modell, entsteht ein neuer Schlüssel und es wird neu berechnet.
    private string BuildKey(Photo photo)
    {
        long lastWriteTicks = 0;
        try
        {
            lastWriteTicks = File.GetLastWriteTimeUtc(photo.FullPath).Ticks;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Ohne Änderungszeit wird allein über Pfad/Größe/Modell unterschieden.
        }

        string raw = string.Join(
            '|',
            photo.FullPath,
            photo.SizeBytes,
            lastWriteTicks,
            _options.VisionModel,
            _options.EmbeddingModel);
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash);
    }
}

/// <summary>
/// Quellgenerierte Logmeldungen des Caching-Embedding-Providers.
/// </summary>
internal static partial class CachingEmbeddingLog
{
    [LoggerMessage(EventId = 2210, Level = LogLevel.Debug, Message = "Embedding aus Cache für {FileName}.")]
    public static partial void CacheHit(ILogger logger, string fileName);
}
