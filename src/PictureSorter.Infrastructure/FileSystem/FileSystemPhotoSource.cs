using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PictureSorter.Core.Diagnostics;
using PictureSorter.Core.Entities;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Infrastructure.FileSystem;

/// <summary>
/// Liest Fotos aus dem Dateisystem und reichert sie mit den EXIF-Metadaten an
/// (Aufnahmedatum, Ort, Kamera, Abmessungen). Unterstützt werden die gängigen
/// Bildformate.
/// </summary>
public sealed class FileSystemPhotoSource : IPhotoSource
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp", ".heic", ".tif", ".tiff",
    };

    private readonly IImageMetadataReader _metadataReader;
    private readonly PhotoSourceOptions _options;
    private readonly ILogger<FileSystemPhotoSource> _logger;

    /// <summary>
    /// Initialisiert die Quelle.
    /// </summary>
    /// <param name="metadataReader">Leser für die EXIF-Metadaten.</param>
    /// <param name="options">Einstellungen des Einlesens (Grad der Gleichzeitigkeit).</param>
    /// <param name="logger">Der Logger.</param>
    public FileSystemPhotoSource(
        IImageMetadataReader metadataReader,
        IOptions<PhotoSourceOptions> options,
        ILogger<FileSystemPhotoSource> logger)
    {
        ArgumentNullException.ThrowIfNull(metadataReader);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _metadataReader = metadataReader;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Photo>> GetPhotosAsync(
        string folderPath,
        bool includeSubfolders,
        int skip,
        int? maxCount,
        IProgress<PhotoScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        // Dieselbe Kette, nur bis zum Ende ausgelesen. Die Bilder kommen dabei in der
        // Reihenfolge, in der sie fertig werden; über ihren Index kommen sie wieder in
        // die Reihenfolge des Ordners. Die Beispielauswahl blättert über einen Startpunkt
        // durch den Ordner und bekäme sonst Bilder doppelt zu sehen und andere nie.
        List<ScannedPhoto> collected = [];
        await foreach (ScannedPhoto scanned in StreamPhotosAsync(
            folderPath, includeSubfolders, skip, maxCount, progress, cancellationToken).ConfigureAwait(false))
        {
            collected.Add(scanned);
        }

        return [.. collected.OrderBy(item => item.Index).Select(item => item.Photo)];
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ScannedPhoto> StreamPhotosAsync(
        string folderPath,
        bool includeSubfolders,
        int skip,
        int? maxCount,
        IProgress<PhotoScanProgress>? progress,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        ArgumentOutOfRangeException.ThrowIfNegative(skip);
        if (maxCount is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCount), maxCount, "Die Höchstzahl muss größer als null sein.");
        }

        string fullFolderPath = Path.GetFullPath(folderPath);
        if (!Directory.Exists(fullFolderPath))
        {
            throw new DirectoryNotFoundException($"Der Ordner „{fullFolderPath}\" existiert nicht.");
        }

        List<string> paths = EnumerateImagePaths(fullFolderPath, includeSubfolders, skip, maxCount, cancellationToken);

        // Die Gesamtzahl steht jetzt fest und wird sofort gemeldet – noch bevor die erste
        // Datei geöffnet ist. Damit steht die Zahl, um die es geht („von 1100"), von
        // Anfang an in der Statusleiste statt erst nach dem letzten Bild.
        progress?.Report(new PhotoScanProgress(0, paths.Count));

        // Ohne Grenze läuft das Laden durch und die Bewertung nimmt sich, was fertig ist –
        // die beiden Abschnitte sind dann wirklich entkoppelt. Mit Grenze wartet das Laden,
        // sobald der Vorrat voll ist; das schont die Leitung bei einem frühen Abbruch,
        // koppelt seine Geschwindigkeit aber an die der KI (siehe PhotoSourceOptions).
        //
        // Gepuffert wird nur das Photo mit seinen Metadaten, nie der Bildinhalt. Ein großer
        // Vorrat kostet deshalb Speicher im zweistelligen Megabyte-Bereich, nicht mehr.
        Channel<ScannedPhoto> channel = _options.PrefetchBuffer <= 0
            ? Channel.CreateUnbounded<ScannedPhoto>(
                new UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = false,
                })
            : Channel.CreateBounded<ScannedPhoto>(
                new BoundedChannelOptions(_options.PrefetchBuffer)
                {
                    FullMode = BoundedChannelFullMode.Wait,
                    SingleReader = true,
                    SingleWriter = false,
                });

        // Eigener Abbruch für die Leser: Bricht der Verbraucher vorzeitig ab (Abbruch
        // durch die Nutzerin), stünden sie sonst für immer am vollen Puffer.
        using CancellationTokenSource readerCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        Task producer = ReadAllIntoAsync(paths, channel.Writer, progress, readerCancellation.Token);

        try
        {
            // Die Fehler der Leser kommen über den Abschluss des Kanals hier an – deshalb
            // muss der Erzeuger nicht eigens abgewartet werden.
            await foreach (ScannedPhoto scanned in channel.Reader
                .ReadAllAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                yield return scanned;
            }
        }
        finally
        {
            await readerCancellation.CancelAsync().ConfigureAwait(false);
            await producer.ConfigureAwait(false);
        }

        string redactedFolder = LogPaths.Redact(fullFolderPath);
        PhotoSourceLog.Scanned(_logger, paths.Count, redactedFolder);
    }

    // Liest alle Dateien – mehrere gleichzeitig – und legt jedes fertige Bild in den
    // Kanal. Der Schritt wartet fast nur: auf die Platte, bei einem Cloud-Ordner auf den
    // Download der Datei. Genau hier lag die Wartezeit vor der Analyse; jede Datei wird
    // einmal geöffnet, und bei iCloud-Fotos oder OneDrive zieht das je Datei einen
    // vollständigen Download nach sich.
    [SuppressMessage(
        "Design",
        "CA1031:Keine allgemeinen Ausnahmetypen abfangen",
        Justification = "Die Ausnahme wird nicht geschluckt, sondern an den Abschluss des Kanals gehängt und dem Verbraucher dort erneut ausgelöst. Bliebe sie hier liegen, wartete der Verbraucher auf einen Kanal, der nie mehr etwas liefert – ein Stillstand statt einer Fehlermeldung.")]
    private async Task ReadAllIntoAsync(
        List<string> paths,
        ChannelWriter<ScannedPhoto> writer,
        IProgress<PhotoScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        int processed = 0;
        Lock progressGate = new();
        Exception? failure = null;

        try
        {
            await Parallel.ForEachAsync(
                Enumerable.Range(0, paths.Count),
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = _options.MaxParallelReads,
                    CancellationToken = cancellationToken,
                },
                async (index, token) =>
                {
                    Photo? photo = await TryReadPhotoAsync(paths[index], token).ConfigureAwait(false);

                    // Erst zählen, dann weiterreichen — diese Reihenfolge ist der Grund für
                    // beide Balken: Ein Bild gilt als geladen, bevor es überhaupt bewertet
                    // werden kann. Andersherum könnte die Bewertung es aufgreifen und melden,
                    // ehe der Ladebalken davon weiß; der Analysebalken stünde dann vor dem
                    // Ladebalken, und die Anzeige behauptete, es sei mehr ausgewertet als
                    // geladen.
                    //
                    // Zählen und Melden unter einem Schloss: Sonst überholten sich die
                    // Meldungen gegenseitig und der Zählstand spränge sichtbar zurück. Das
                    // Schloss ist billig – gemeldet wird nur eine Zahl.
                    lock (progressGate)
                    {
                        processed++;

                        // Gezählt werden die bearbeiteten Dateien, nicht die brauchbaren: Sonst
                        // bliebe der Balken bei jeder übersprungenen Datei stehen und käme nie
                        // bei 100 % an.
                        progress?.Report(new PhotoScanProgress(processed, paths.Count));
                    }

                    if (photo is not null)
                    {
                        // Blockiert, wenn der Vorlauf voll ist. Die Datei ist dann bereits
                        // geladen und wartet nur darauf, abgeholt zu werden – der Ladebalken
                        // darf sie deshalb schon zählen.
                        await writer.WriteAsync(new ScannedPhoto(photo, index, paths.Count), token)
                            .ConfigureAwait(false);
                    }
                }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Bewusst jede Ausnahme: Sie wird nicht geschluckt, sondern an den Abschluss
            // des Kanals gehängt und dort dem Verbraucher erneut ausgelöst. Bliebe sie
            // hier liegen, wartete der Verbraucher auf einen Kanal, der nie mehr etwas
            // liefert – ein Stillstand statt einer Fehlermeldung.
            failure = ex;
        }
        finally
        {
            _ = writer.TryComplete(failure);
        }
    }

    // Das Aufzählen selbst liest nur die Verzeichniseinträge und ist auch bei einem
    // Cloud-Ordner billig – teuer wird erst das Öffnen jeder Datei im Anschluss.
    // Deshalb hört schon das Aufzählen bei der Höchstzahl auf.
    private static List<string> EnumerateImagePaths(
        string folderPath,
        bool includeSubfolders,
        int skip,
        int? maxCount,
        CancellationToken cancellationToken)
    {
        SearchOption searchOption = includeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        List<string> paths = [];
        int seen = 0;

        foreach (string path in Directory.EnumerateFiles(folderPath, "*", searchOption))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!SupportedExtensions.Contains(Path.GetExtension(path)))
            {
                continue;
            }

            // Übersprungen wird beim Aufzählen, nicht erst danach: Sonst würden für die
            // bereits gezeigten Bilder erneut die Dateien geöffnet.
            if (seen++ < skip)
            {
                continue;
            }

            paths.Add(path);
            if (paths.Count == maxCount)
            {
                break;
            }
        }

        return paths;
    }

    // Zwischen dem Auflisten und dem Lesen kann die Datei verschwinden: Ein
    // Virenscanner schiebt sie in Quarantäne, ein Cloud-Ordner räumt sie weg, jemand
    // löscht sie im Explorer. Bis hierher riss der Zugriff auf die Dateigröße dann den
    // gesamten Lauf mit – für alle Funktionen, die Fotos einlesen.
    private async Task<Photo?> TryReadPhotoAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            return await ReadPhotoAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            PhotoSourceLog.Skipped(_logger, LogPaths.Redact(path), ex);
            return null;
        }
    }

    private async Task<Photo> ReadPhotoAsync(string path, CancellationToken cancellationToken)
    {
        FileInfo info = new(path);
        PhotoMetadata? metadata = await _metadataReader.ReadAsync(path, cancellationToken).ConfigureAwait(false);

        // Liegt kein EXIF-Aufnahmedatum vor, dient die letzte Änderungszeit als Annäherung.
        DateTimeOffset capturedAt = metadata?.CapturedAt
            ?? new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero);

        return new Photo
        {
            FullPath = info.FullName,
            FileName = info.Name,
            SizeBytes = info.Length,
            CapturedAt = capturedAt,
            Width = metadata?.Width,
            Height = metadata?.Height,
            CameraModel = metadata?.CameraModel,
            Latitude = metadata?.Latitude,
            Longitude = metadata?.Longitude,
        };
    }
}

/// <summary>
/// Quellgenerierte Logmeldungen der Dateisystem-Quelle.
/// </summary>
internal static partial class PhotoSourceLog
{
    [LoggerMessage(EventId = 2300, Level = LogLevel.Information, Message = "{Count} Fotos in {Folder} gefunden.")]
    public static partial void Scanned(ILogger logger, int count, string folder);

    [LoggerMessage(EventId = 2301, Level = LogLevel.Warning, Message = "Foto {File} übersprungen: beim Einlesen nicht mehr erreichbar.")]
    public static partial void Skipped(ILogger logger, string file, Exception exception);
}
