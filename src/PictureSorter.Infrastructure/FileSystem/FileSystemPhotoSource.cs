using Microsoft.Extensions.Logging;
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
    private readonly ILogger<FileSystemPhotoSource> _logger;

    /// <summary>
    /// Initialisiert die Quelle.
    /// </summary>
    /// <param name="metadataReader">Leser für die EXIF-Metadaten.</param>
    /// <param name="logger">Der Logger.</param>
    public FileSystemPhotoSource(IImageMetadataReader metadataReader, ILogger<FileSystemPhotoSource> logger)
    {
        ArgumentNullException.ThrowIfNull(metadataReader);
        ArgumentNullException.ThrowIfNull(logger);
        _metadataReader = metadataReader;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Photo>> GetPhotosAsync(
        string folderPath,
        bool includeSubfolders,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);

        string fullFolderPath = Path.GetFullPath(folderPath);
        if (!Directory.Exists(fullFolderPath))
        {
            throw new DirectoryNotFoundException($"Der Ordner „{fullFolderPath}\" existiert nicht.");
        }

        List<string> paths = EnumerateImagePaths(fullFolderPath, includeSubfolders, cancellationToken);

        List<Photo> photos = new(paths.Count);
        foreach (string path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            photos.Add(await ReadPhotoAsync(path, cancellationToken).ConfigureAwait(false));
        }

        string redactedFolder = LogPaths.Redact(fullFolderPath);
        PhotoSourceLog.Scanned(_logger, photos.Count, redactedFolder);
        return photos;
    }

    private static List<string> EnumerateImagePaths(
        string folderPath,
        bool includeSubfolders,
        CancellationToken cancellationToken)
    {
        SearchOption searchOption = includeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        List<string> paths = [];

        foreach (string path in Directory.EnumerateFiles(folderPath, "*", searchOption))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (SupportedExtensions.Contains(Path.GetExtension(path)))
            {
                paths.Add(path);
            }
        }

        return paths;
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
}
