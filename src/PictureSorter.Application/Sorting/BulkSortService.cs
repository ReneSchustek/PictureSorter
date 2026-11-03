using System.Diagnostics;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using PictureSorter.Core.Entities;
using PictureSorter.Core.Exceptions;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Application.Sorting;

/// <summary>
/// Abschnitt des Sammeldurchlaufs.
/// </summary>
public enum BulkSortPhase
{
    /// <summary>Die Modelldateien werden geladen; die Zahlen sind Bytes.</summary>
    Downloading,

    /// <summary>Die Bilder in den Ordnern werden als Beispiele gelesen.</summary>
    Examples,

    /// <summary>Die einzusortierenden Bilder werden gelesen.</summary>
    Photos,
}

/// <summary>
/// Stand des Sammeldurchlaufs.
/// </summary>
/// <param name="Phase">Der Abschnitt.</param>
/// <param name="Processed">Wie viel erledigt ist.</param>
/// <param name="Total">Wie viel es insgesamt ist; 0, solange das unbekannt ist.</param>
public readonly record struct BulkSortProgress(BulkSortPhase Phase, long Processed, long Total);

/// <summary>
/// Der Sammeldurchlauf für die Ansicht. Eine Schnittstelle, weil ihre Tests sonst Dateisystem und
/// Modell mitbringen müssten.
/// </summary>
public interface IBulkSortService
{
    /// <summary>
    /// Liest einen Ordner: Seine Unterordner beschreiben die Ziele, die Bilder direkt darin werden
    /// einsortiert. Bekannte Bilder kommen aus dem Zwischenspeicher, ein zweiter Lauf dauert deshalb
    /// Sekunden, auch nach einem Abbruch.
    /// </summary>
    /// <param name="sourceFolder">Der Ordner.</param>
    /// <param name="newFolderNames">Weitere Ziele, die noch nicht als Ordner bestehen.</param>
    /// <param name="progress">Meldet den Stand; darf fehlen.</param>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    /// <returns>Die Sitzung, aus der die Vorschläge kommen.</returns>
    /// <exception cref="AiUnavailableException">Das Modell ist nicht zu bekommen.</exception>
    Task<BulkSortSession> AnalyzeAsync(
        string sourceFolder,
        IReadOnlyList<string> newFolderNames,
        IProgress<BulkSortProgress>? progress,
        CancellationToken cancellationToken);
}

/// <summary>
/// Führt den Sammeldurchlauf aus. Er braucht kein Anlernen und keinen eigenen Lauf je Ziel: Jedes Bild
/// bekommt einmal seinen Vektor und wird gegen alle Ziele verglichen.
/// </summary>
public sealed class BulkSortService : IBulkSortService
{
    // Ein Ordner beschreibt sich mit mehr Bildern besser, der Gewinn flacht aber ab: AUC 0,78 bei 15, 0,81
    // bei 120 Bildern. Mehr als diese Grenze kostet nur Lernzeit nach jeder Korrektur.
    private const int MaxExamplesPerFolder = 200;

    // Gemessen mit 60 unsortierten Bildern als Gegenseite. Mehr davon gehörten zunehmend selbst in die Ordner.
    private const int WeakNegativeCount = 60;

    // Selten genug, dass das Abfragen nicht zählt, und oft genug bei rund 55 MB je Foto im schlimmsten Fall.
    private const int MemoryCheckInterval = 25;

    private readonly IPhotoSource _photoSource;
    private readonly IVisualEmbedder _embedder;
    private readonly IFolderLookup _folderLookup;
    private readonly ILogger<BulkSortService> _logger;
    private readonly Func<bool> _isMemoryLow;

    /// <summary>
    /// Initialisiert den Dienst.
    /// </summary>
    /// <param name="photoSource">Die Quelle der Fotos.</param>
    /// <param name="embedder">Berechnet die Vektoren.</param>
    /// <param name="folderLookup">Nennt die Unterordner.</param>
    /// <param name="logger">Der Logger.</param>
    /// <param name="isMemoryLow">Meldet, dass der Speicher knapp wird; ohne Angabe gilt der Prozess im Verhältnis zum Rechner.</param>
    public BulkSortService(
        IPhotoSource photoSource,
        IVisualEmbedder embedder,
        IFolderLookup folderLookup,
        ILogger<BulkSortService> logger,
        Func<bool>? isMemoryLow = null)
    {
        ArgumentNullException.ThrowIfNull(photoSource);
        ArgumentNullException.ThrowIfNull(embedder);
        ArgumentNullException.ThrowIfNull(folderLookup);
        ArgumentNullException.ThrowIfNull(logger);

        _photoSource = photoSource;
        _embedder = embedder;
        _folderLookup = folderLookup;
        _logger = logger;
        _isMemoryLow = isMemoryLow ?? IsProcessNearPhysicalMemory;
    }

    /// <inheritdoc />
    public async Task<BulkSortSession> AnalyzeAsync(
        string sourceFolder,
        IReadOnlyList<string> newFolderNames,
        IProgress<BulkSortProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFolder);
        ArgumentNullException.ThrowIfNull(newFolderNames);

        await _embedder
            .PrepareAsync(ToDownloadProgress(progress), cancellationToken)
            .ConfigureAwait(false);

        List<BulkCategory> categories = await ReadFoldersAsync(sourceFolder, progress, cancellationToken).ConfigureAwait(false);
        AddNamedFolders(categories, newFolderNames);

        (IReadOnlyList<BulkPhoto> photos, int unreadable) = await ReadPhotosAsync(sourceFolder, progress, cancellationToken).ConfigureAwait(false);

        // Sortiert nach Inhalt statt nach Einlesereihenfolge: Die Bilder kommen fertig, wie sie gelesen
        // sind, und sonst gäbe jeder Lauf eine andere Gegenseite und damit andere Vorschläge.
        IReadOnlyList<float[]> weakNegatives = [.. photos
            .OrderBy(static entry => entry.Photo.ComputeSignature(), StringComparer.Ordinal)
            .Take(WeakNegativeCount)
            .Select(static entry => entry.Vector)];

        BulkSortLog.Analyzed(_logger, categories.Count, photos.Count);
        return new BulkSortSession(sourceFolder, categories, photos, weakNegatives)
        {
            UnreadablePhotoCount = unreadable,
            OnlineOnlyPhotoCount = _folderLookup.CountOnlineOnlyPhotos(sourceFolder, includeSubfolders: false),
            UnsupportedFileCount = _folderLookup.CountUnsupportedFiles(sourceFolder),
        };
    }

    private static Progress<ModelDownloadProgress>? ToDownloadProgress(IProgress<BulkSortProgress>? progress) =>
        progress is null
            ? null
            : new Progress<ModelDownloadProgress>(value =>
                progress.Report(new BulkSortProgress(BulkSortPhase.Downloading, value.Received, value.Total)));

    private async Task<List<BulkCategory>> ReadFoldersAsync(
        string sourceFolder,
        IProgress<BulkSortProgress>? progress,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<string> names = _folderLookup.ListSubfolders(sourceFolder);
        List<BulkCategory> categories = [];

        for (int index = 0; index < names.Count; index++)
        {
            progress?.Report(new BulkSortProgress(BulkSortPhase.Examples, index, names.Count));

            List<float[]> examples = [];
            await foreach (ScannedPhoto scanned in _photoSource
                .StreamPhotosAsync(Path.Combine(sourceFolder, names[index]), includeSubfolders: false, skip: 0, MaxExamplesPerFolder, null, cancellationToken)
                .ConfigureAwait(false))
            {
                float[]? vector = await TryEmbedAsync(scanned.Photo, cancellationToken).ConfigureAwait(false);
                if (vector is not null)
                {
                    examples.Add(vector);
                }
            }

            categories.Add(new BulkCategory(names[index], examples));
        }

        return categories;
    }

    private static void AddNamedFolders(
        List<BulkCategory> categories,
        IReadOnlyList<string> newFolderNames)
    {
        foreach (string name in newFolderNames.Select(static name => name.Trim()).Where(static name => name.Length > 0))
        {
            if (categories.Exists(category => string.Equals(category.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            categories.Add(new BulkCategory(name, []));
        }
    }

    private async Task<(IReadOnlyList<BulkPhoto> Photos, int Unreadable)> ReadPhotosAsync(
        string sourceFolder,
        IProgress<BulkSortProgress>? progress,
        CancellationToken cancellationToken)
    {
        ConcurrentBag<(int Index, BulkPhoto Entry)> results = [];
        int processed = 0;
        int unreadable = 0;
        int total = 0;

        // Das Dekodieren ist der teure Teil und läuft im Betriebssystem; das Modell nutzt selbst alle Kerne.
        // Die Hälfte der Kerne hält den Rechner bedienbar, der in der Zeit womöglich anderes tut.
        ParallelOptions options = new()
        {
            MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2),
            CancellationToken = cancellationToken,
        };

        await Parallel.ForEachAsync(
            _photoSource.StreamPhotosAsync(sourceFolder, includeSubfolders: false, skip: 0, maxCount: null, null, cancellationToken),
            options,
            async (scanned, token) =>
            {
                float[]? vector = await TryEmbedAsync(scanned.Photo, token).ConfigureAwait(false);
                if (vector is not null)
                {
                    results.Add((scanned.Index, new BulkPhoto(scanned.Photo, vector)));
                }
                else
                {
                    _ = Interlocked.Increment(ref unreadable);
                }

                _ = Interlocked.Exchange(ref total, scanned.Total);
                int done = Interlocked.Increment(ref processed);
                if (done % MemoryCheckInterval == 0 && _isMemoryLow())
                {
                    // Lieber eine klare Meldung als ein Absturz, der den Lauf ohne ein Wort beendet.
                    throw new InsufficientMemoryException("Der Arbeitsspeicher wird knapp. Bitte schließe andere Programme und starte den Durchlauf neu; bereits berechnete Fotos gehen nicht verloren.");
                }

                progress?.Report(new BulkSortProgress(BulkSortPhase.Photos, done, scanned.Total));
            }).ConfigureAwait(false);

        // Was schon beim Einlesen ausfiel, kommt hier gar nicht an. Die Gesamtzahl der Quelle zählt es mit.
        int notDelivered = Math.Max(0, total - processed);
        return ([.. results.OrderBy(static result => result.Index).Select(static result => result.Entry)], unreadable + notDelivered);
    }

    // Der Speicher der Bilddecoder liegt außerhalb von .NET, die Zahlen des GC sehen ihn nicht.
    private static bool IsProcessNearPhysicalMemory()
    {
        using Process process = Process.GetCurrentProcess();
        return process.PrivateMemorySize64 > GC.GetGCMemoryInfo().TotalAvailableMemoryBytes * 3 / 4;
    }

    private async Task<float[]?> TryEmbedAsync(Photo photo, CancellationToken cancellationToken)
    {
        try
        {
            ImageEmbedding embedding = await _embedder.EmbedPhotoAsync(photo, cancellationToken).ConfigureAwait(false);
            return [.. embedding.Values];
        }
        catch (ImageUnreadableException ex)
        {
            // Ein Bild, das sich nicht lesen lässt, bleibt liegen. Es darf den Lauf über Tausende nicht
            // abbrechen, und ohne Vektor lässt es sich nicht einordnen.
            BulkSortLog.PhotoSkipped(_logger, photo.FileName, ex);
            return null;
        }
    }
}

/// <summary>
/// Quellgenerierte Logmeldungen des Sammeldurchlaufs.
/// </summary>
internal static partial class BulkSortLog
{
    [LoggerMessage(EventId = 3450, Level = LogLevel.Information, Message = "Sammeldurchlauf: {Categories} Ziele, {Photos} Fotos.")]
    public static partial void Analyzed(ILogger logger, int categories, int photos);

    [LoggerMessage(EventId = 3451, Level = LogLevel.Warning, Message = "Foto {FileName} übersprungen (nicht lesbar).")]
    public static partial void PhotoSkipped(ILogger logger, string fileName, Exception exception);
}
