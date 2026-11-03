using PictureSorter.Core.Exceptions;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;
using PictureSorter.Infrastructure.FileSystem;

namespace PictureSorter.App.Services;

/// <summary>
/// Liest die Vorschaubilder der Listen, höchstens zwei zugleich, und hält fertige im Speicher.
/// </summary>
/// <remarks>
/// <c>BitmapImage</c> mit einem Dateipfad startet je Zeile einen eigenen Dekodiervorgang, ohne Grenze und ohne
/// Abbruch. Beim Scrollen durch 4.329 Fotos eines echten Bestands trieb das ein Programm von 310 MB auf über
/// 4 GB und 1.300 Threads, bis es nicht mehr reagierte. Hier wartet ein Bild, das aus dem Blick gescrollt wird,
/// gar nicht erst auf seinen Platz.
/// </remarks>
internal sealed class ThumbnailLoader : IDisposable
{
    // Rund 500 Vorschaubilder der Listengröße: Wer zurückscrollt, sieht sie sofort, und der Speicher bleibt klein.
    private const long CacheBytes = 32L * 1024 * 1024;

    // Zwei, weil der Dekoder selbst mehrere Kerne nutzt und die Liste trotzdem bedienbar bleiben soll.
    private const int DefaultParallelReads = 2;

    private static readonly TimeSpan DefaultReadTimeout = TimeSpan.FromSeconds(30);

    private readonly IScaledImageReader _reader;
    private readonly TimeSpan _readTimeout;
    private readonly SemaphoreSlim _slots;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, LinkedListNode<CacheEntry>> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<CacheEntry> _recent = new();
    private long _cachedBytes;

    /// <summary>
    /// Initialisiert den Lader.
    /// </summary>
    /// <param name="reader">Liest die Bilder.</param>
    /// <param name="parallelReads">Wie viele Bilder höchstens zugleich gelesen werden.</param>
    /// <param name="readTimeout">Wie lange ein Bild seinen Platz höchstens belegt.</param>
    public ThumbnailLoader(IScaledImageReader reader, int parallelReads = DefaultParallelReads, TimeSpan? readTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(parallelReads);

        _reader = reader;
        _readTimeout = readTimeout ?? DefaultReadTimeout;
        _slots = new SemaphoreSlim(parallelReads, parallelReads);
    }

    /// <summary>
    /// Liefert ein Vorschaubild.
    /// </summary>
    /// <param name="filePath">Der Pfad des Bildes.</param>
    /// <param name="shortEdge">Die Länge der kürzeren Kante in Bildpunkten.</param>
    /// <param name="cancellationToken">Bricht ab, sobald das Bild nicht mehr gebraucht wird.</param>
    /// <returns>
    /// Die Bildpunkte, oder <see langword="null"/>, wenn es kein Vorschaubild gibt: Die Datei liegt nur online, ist
    /// nicht lesbar oder antwortet nicht.
    /// </returns>
    public async Task<ScaledImage?> LoadAsync(string filePath, int shortEdge, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        string key = string.Concat(filePath, "|", shortEdge.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (TryGetCached(key, out ScaledImage? cached))
        {
            return cached;
        }

        // Das Öffnen einer Datei, die nur online liegt, löst den Download aus und kann Minuten blockieren.
        if (OnlineOnlyFiles.IsOnlineOnly(filePath))
        {
            return null;
        }

        await _slots.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Während des Wartens kann eine andere Zeile dasselbe Bild gelesen haben.
            if (TryGetCached(key, out cached))
            {
                return cached;
            }

            // Der Platz kann im selben Moment frei geworden sein, in dem die Zeile aus dem Blick ging.
            cancellationToken.ThrowIfCancellationRequested();

            ScaledImage image = await _reader
                .ReadAsync(filePath, shortEdge, cancellationToken)
                .WaitAsync(_readTimeout, cancellationToken)
                .ConfigureAwait(false);
            Remember(key, image);
            return image;
        }
        catch (ImageUnreadableException)
        {
            // Gemerkt, sonst dekodierte jedes Zurückscrollen die kaputte Datei erneut.
            Remember(key, image: null);
            return null;
        }
        catch (TimeoutException)
        {
            return null;
        }
        finally
        {
            _ = _slots.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose() => _slots.Dispose();

    private bool TryGetCached(string key, out ScaledImage? image)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out LinkedListNode<CacheEntry>? node))
            {
                _recent.Remove(node);
                _recent.AddFirst(node);
                image = node.Value.Image;
                return true;
            }
        }

        image = null;
        return false;
    }

    private void Remember(string key, ScaledImage? image)
    {
        lock (_gate)
        {
            if (_entries.ContainsKey(key))
            {
                return;
            }

            _entries[key] = _recent.AddFirst(new CacheEntry(key, image));
            _cachedBytes += image?.Rgba.Length ?? 0;

            while (_cachedBytes > CacheBytes && _recent.Last is { } oldest)
            {
                _recent.RemoveLast();
                _ = _entries.Remove(oldest.Value.Key);
                _cachedBytes -= oldest.Value.Image?.Rgba.Length ?? 0;
            }
        }
    }

    /// <summary>Ein gemerktes Bild; ohne Bildpunkte, wenn die Datei nicht lesbar war.</summary>
    private sealed record CacheEntry(string Key, ScaledImage? Image);
}
