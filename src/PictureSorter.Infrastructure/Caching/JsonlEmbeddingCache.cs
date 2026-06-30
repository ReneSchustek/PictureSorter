using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Infrastructure.Caching;

/// <summary>
/// Datei-basierter Embedding-Cache im JSON-Lines-Format (eine JSON-Zeile je
/// Eintrag). Neue Einträge werden angehängt (kein Neuschreiben der ganzen Datei),
/// daher auch bei tausenden Fotos schnell. Beim Laden gewinnt der jeweils letzte
/// Eintrag eines Schlüssels. Der Zugriff ist über ein Semaphor serialisiert.
/// </summary>
public sealed class JsonlEmbeddingCache : IEmbeddingCache, IDisposable
{
    // Zusätzlicher Puffer an „toten" Zeilen, die wir tolerieren, bevor kompaktiert
    // wird – verhindert ständiges Neuschreiben bei kleinen Caches.
    private const int CompactionSlack = 100;

    private readonly string _path;
    private readonly ILogger<JsonlEmbeddingCache> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, ImageEmbedding> _entries = new(StringComparer.Ordinal);
    private bool _loaded;

    /// <summary>
    /// Initialisiert den Cache.
    /// </summary>
    /// <param name="dataDirectory">Verzeichnis der Cache-Datei.</param>
    /// <param name="logger">Der Logger.</param>
    public JsonlEmbeddingCache(string dataDirectory, ILogger<JsonlEmbeddingCache> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        ArgumentNullException.ThrowIfNull(logger);

        _path = Path.Combine(dataDirectory, "embedding-cache.jsonl");
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ImageEmbedding?> GetAsync(string key, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureLoaded();
            return _entries.GetValueOrDefault(key);
        }
        finally
        {
            _ = _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task SetAsync(string key, ImageEmbedding embedding, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(embedding);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureLoaded();
            _entries[key] = embedding;

            CacheLine line = new(key, embedding.Model, [.. embedding.Values]);
            string json = JsonSerializer.Serialize(line);
            await File.AppendAllTextAsync(_path, json + Environment.NewLine, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Der Cache ist nur eine Optimierung – Schreibfehler werden geloggt,
            // dürfen die Analyse aber nicht abbrechen.
            EmbeddingCacheLog.WriteFailed(_logger, ex);
        }
        finally
        {
            _ = _gate.Release();
        }
    }

    // Lädt die Cache-Datei einmalig in den Speicher. Beschädigte Zeilen werden
    // übersprungen, damit ein einzelner Fehler den Cache nicht unbrauchbar macht.
    private void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        if (!File.Exists(_path))
        {
            return;
        }

        try
        {
            int lineCount = 0;
            foreach (string rawLine in File.ReadLines(_path, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(rawLine))
                {
                    continue;
                }

                lineCount++;
                try
                {
                    CacheLine? line = JsonSerializer.Deserialize<CacheLine>(rawLine);
                    if (line is { Key: { } key, Model: { } model, Values.Count: > 0 })
                    {
                        _entries[key] = new ImageEmbedding(line.Values, model);
                    }
                }
                catch (JsonException)
                {
                    // Beschädigte Zeile überspringen.
                }
            }

            EmbeddingCacheLog.Loaded(_logger, _entries.Count);

            // Append-only-Datei wächst durch überschriebene/verwaiste Einträge. Sind
            // deutlich mehr Zeilen als gültige Einträge vorhanden, einmalig kompaktieren.
            if (lineCount > (_entries.Count * 2) + CompactionSlack)
            {
                Compact();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            EmbeddingCacheLog.ReadFailed(_logger, ex);
        }
    }

    // Schreibt je gültigem Eintrag genau eine Zeile in eine temporäre Datei und
    // ersetzt das Original atomar. Läuft unter dem Schreib-Gate (über EnsureLoaded).
    private void Compact()
    {
        string tempPath = _path + ".tmp";
        try
        {
            StringBuilder builder = new();
            foreach (KeyValuePair<string, ImageEmbedding> entry in _entries)
            {
                CacheLine line = new(entry.Key, entry.Value.Model, [.. entry.Value.Values]);
                _ = builder.Append(JsonSerializer.Serialize(line)).Append(Environment.NewLine);
            }

            File.WriteAllText(tempPath, builder.ToString(), Encoding.UTF8);
            File.Move(tempPath, _path, overwrite: true);
            EmbeddingCacheLog.Compacted(_logger, _entries.Count);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            EmbeddingCacheLog.WriteFailed(_logger, ex);
            TryDeleteTemp(tempPath);
        }
    }

    private static void TryDeleteTemp(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Eine zurückgebliebene Temp-Datei ist unkritisch.
        }
    }

    /// <summary>
    /// Gibt das Semaphor frei.
    /// </summary>
    public void Dispose() => _gate.Dispose();

    // Serialisierungsformat einer Cache-Zeile. Kurze Eigenschaftsnamen halten die
    // Datei kompakt.
    private sealed record CacheLine(
        [property: System.Text.Json.Serialization.JsonPropertyName("k")] string Key,
        [property: System.Text.Json.Serialization.JsonPropertyName("m")] string Model,
        [property: System.Text.Json.Serialization.JsonPropertyName("v")] IReadOnlyList<float> Values);
}

/// <summary>
/// Quellgenerierte Logmeldungen des Embedding-Caches.
/// </summary>
internal static partial class EmbeddingCacheLog
{
    [LoggerMessage(EventId = 2200, Level = LogLevel.Information, Message = "Embedding-Cache geladen: {Count} Einträge.")]
    public static partial void Loaded(ILogger logger, int count);

    [LoggerMessage(EventId = 2203, Level = LogLevel.Information, Message = "Embedding-Cache kompaktiert auf {Count} Einträge.")]
    public static partial void Compacted(ILogger logger, int count);

    [LoggerMessage(EventId = 2201, Level = LogLevel.Warning, Message = "Embedding-Cache konnte nicht gelesen werden.")]
    public static partial void ReadFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 2202, Level = LogLevel.Warning, Message = "Embedding-Cache-Eintrag konnte nicht geschrieben werden.")]
    public static partial void WriteFailed(ILogger logger, Exception exception);
}
