using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PictureSorter.Core.Diagnostics;
using PictureSorter.Core.Entities;
using PictureSorter.Core.Enums;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Application.Duplicates;

/// <summary>
/// Durchsucht einen Ordner nach Duplikaten. Bit-identische Dateien werden über
/// den Inhalts-Hash gruppiert, visuell ähnliche Bilder über die Hamming-Distanz
/// ihrer Wahrnehmungs-Hashes.
/// </summary>
public sealed class DuplicateScanService : IDuplicateScanner
{
    private readonly IPhotoSource _photoSource;
    private readonly IPerceptualHasher _hasher;
    private readonly DuplicateScanOptions _options;
    private readonly ILogger<DuplicateScanService> _logger;

    /// <summary>
    /// Initialisiert den Duplikat-Scanner.
    /// </summary>
    /// <param name="photoSource">Quelle der Fotos.</param>
    /// <param name="hasher">Berechnet die Fingerabdrücke.</param>
    /// <param name="options">Einstellungen der Duplikat-Suche.</param>
    /// <param name="logger">Der Logger.</param>
    public DuplicateScanService(
        IPhotoSource photoSource,
        IPerceptualHasher hasher,
        IOptions<DuplicateScanOptions> options,
        ILogger<DuplicateScanService> logger)
    {
        ArgumentNullException.ThrowIfNull(photoSource);
        ArgumentNullException.ThrowIfNull(hasher);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _photoSource = photoSource;
        _hasher = hasher;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DuplicateGroup>> ScanAsync(
        string folderPath,
        bool includeSubfolders,
        IProgress<DuplicateScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);

        IReadOnlyList<Photo> photos = await _photoSource
            .GetPhotosAsync(folderPath, includeSubfolders, skip: 0, maxCount: null, cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<FingerprintedPhoto> fingerprinted =
            await FingerprintAsync(photos, progress, cancellationToken).ConfigureAwait(false);

        List<DuplicateGroup> groups = [];
        HashSet<string> consumed = [];

        AddExactGroups(fingerprinted, groups, consumed);
        if (_options.DetectSimilar)
        {
            AddSimilarGroups(fingerprinted, groups, consumed);
        }

        string redactedFolder = LogPaths.Redact(folderPath);
        DuplicateLog.Scanned(_logger, groups.Count, redactedFolder);
        return groups;
    }

    private async Task<IReadOnlyList<FingerprintedPhoto>> FingerprintAsync(
        IReadOnlyList<Photo> photos,
        IProgress<DuplicateScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        List<FingerprintedPhoto> result = new(photos.Count);
        for (int index = 0; index < photos.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Photo photo = photos[index];
            try
            {
                ImageFingerprint fingerprint = await _hasher.ComputeAsync(photo.FullPath, cancellationToken).ConfigureAwait(false);
                result.Add(new FingerprintedPhoto(photo, fingerprint));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Eine einzelne unlesbare Datei darf den ganzen Lauf nicht beenden. Der
                // Fall tritt im Alltag auf: Ein Virenscanner zieht die Datei waehrend des
                // Laufs weg, ein Cloud-Ordner hat sie noch nicht heruntergeladen, oder die
                // Rechte fehlen. Ohne diese Behandlung stand die Nutzerin nach Minuten
                // Wartezeit vor einer Fehlermeldung statt vor einem Ergebnis.
                DuplicateLog.Skipped(_logger, LogPaths.Redact(photo.FullPath), ex);
            }

            progress?.Report(new DuplicateScanProgress(index + 1, photos.Count));
        }

        return result;
    }

    private static void AddExactGroups(
        IReadOnlyList<FingerprintedPhoto> items,
        List<DuplicateGroup> groups,
        HashSet<string> consumed)
    {
        IEnumerable<IGrouping<string, FingerprintedPhoto>> byContent =
            items.GroupBy(item => item.Fingerprint.ContentHash, StringComparer.Ordinal);

        foreach (IGrouping<string, FingerprintedPhoto> group in byContent)
        {
            List<FingerprintedPhoto> members = [.. group];
            if (members.Count < 2)
            {
                continue;
            }

            groups.Add(new DuplicateGroup(DuplicateKind.Exact, OrderByQuality(members)));
            foreach (FingerprintedPhoto member in members)
            {
                _ = consumed.Add(member.Photo.FullPath);
            }
        }
    }

    private void AddSimilarGroups(
        IReadOnlyList<FingerprintedPhoto> items,
        List<DuplicateGroup> groups,
        HashSet<string> consumed)
    {
        List<FingerprintedPhoto> candidates =
            [.. items.Where(item => item.Fingerprint.Perceptual is not null && !consumed.Contains(item.Photo.FullPath))];

        bool[] clustered = new bool[candidates.Count];
        for (int seed = 0; seed < candidates.Count; seed++)
        {
            if (clustered[seed])
            {
                continue;
            }

            List<FingerprintedPhoto> cluster = CollectSimilar(candidates, clustered, seed);
            if (cluster.Count >= 2)
            {
                groups.Add(new DuplicateGroup(DuplicateKind.Similar, OrderByQuality(cluster)));
            }
        }
    }

    private List<FingerprintedPhoto> CollectSimilar(
        List<FingerprintedPhoto> candidates,
        bool[] clustered,
        int seed)
    {
        PerceptualHash seedHash = candidates[seed].Fingerprint.Perceptual!.Value;
        List<FingerprintedPhoto> cluster = [candidates[seed]];
        clustered[seed] = true;

        for (int other = seed + 1; other < candidates.Count; other++)
        {
            if (clustered[other])
            {
                continue;
            }

            PerceptualHash otherHash = candidates[other].Fingerprint.Perceptual!.Value;
            if (seedHash.DistanceTo(otherHash) <= _options.MaxHammingDistance)
            {
                cluster.Add(candidates[other]);
                clustered[other] = true;
            }
        }

        return cluster;
    }

    // Bestes (höchstauflösendes, größtes) Bild zuerst, damit die Oberfläche es
    // standardmäßig behält und die übrigen zum Löschen vormerkt.
    private static IReadOnlyList<Photo> OrderByQuality(IEnumerable<FingerprintedPhoto> items) =>
        [.. items
            .OrderByDescending(item => (long)(item.Photo.Width ?? 0) * (item.Photo.Height ?? 0))
            .ThenByDescending(item => item.Photo.SizeBytes)
            .ThenBy(item => item.Photo.FileName, StringComparer.OrdinalIgnoreCase)
            .Select(item => item.Photo)];

    private sealed record FingerprintedPhoto(Photo Photo, ImageFingerprint Fingerprint);
}

/// <summary>
/// Quellgenerierte Logmeldungen der Duplikat-Suche.
/// </summary>
internal static partial class DuplicateLog
{
    [LoggerMessage(EventId = 3200, Level = LogLevel.Information, Message = "{Count} Duplikat-Gruppen in {Folder} gefunden.")]
    public static partial void Scanned(ILogger logger, int count, string folder);

    [LoggerMessage(EventId = 3201, Level = LogLevel.Warning, Message = "Foto {File} bei der Duplikat-Suche übersprungen (nicht lesbar).")]
    public static partial void Skipped(ILogger logger, string file, Exception exception);
}
