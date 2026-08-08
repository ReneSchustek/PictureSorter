using Microsoft.Extensions.Logging;
using PictureSorter.Core.Entities;
using PictureSorter.Core.Enums;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Application.Sorting;

/// <summary>
/// Holt die Vorschläge einer früheren Analyse aus dem Sortier-Gedächtnis zurück.
///
/// Das Gedächtnis wurde schon immer nach jedem einzelnen Foto beschrieben — es hält also
/// auch von einem Lauf, der abgestürzt ist oder stehenblieb, jedes gefällte Urteil fest.
/// Nur genutzt wurde davon bisher die Ablehnung: Ein Vorschlag, der nie angewendet wurde,
/// sollte bewusst erneut erscheinen, und dafür wurde er erneut berechnet.
///
/// Bei einem Ordner, dessen Bewertung Tage dauert, ist das der Unterschied zwischen
/// „nichts verloren" und „alles noch einmal". Diese Klasse ist deshalb bewusst auch für
/// Läufe zuständig, die vor dem Analyse-Protokoll entstanden sind: Für sie gibt es keinen
/// anderen Weg zurück.
/// </summary>
public sealed class SortMemoryRecovery
{
    private readonly ISortMemory _memory;
    private readonly IImageMetadataReader _metadataReader;
    private readonly ILogger<SortMemoryRecovery> _logger;

    /// <summary>
    /// Initialisiert die Wiederherstellung.
    /// </summary>
    /// <param name="memory">Das dauerhafte Sortier-Gedächtnis.</param>
    /// <param name="metadataReader">Leser für die Metadaten der wiedergefundenen Dateien.</param>
    /// <param name="logger">Der Logger.</param>
    public SortMemoryRecovery(
        ISortMemory memory,
        IImageMetadataReader metadataReader,
        ILogger<SortMemoryRecovery> logger)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(metadataReader);
        ArgumentNullException.ThrowIfNull(logger);

        _memory = memory;
        _metadataReader = metadataReader;
        _logger = logger;
    }

    /// <summary>
    /// Baut aus den gemerkten Urteilen die Vorschläge einer früheren Analyse neu auf.
    /// </summary>
    /// <param name="sourceFolder">Der durchsuchte Ordner.</param>
    /// <param name="categoryName">Die Kategorie beziehungsweise der Zielordnername.</param>
    /// <param name="kind">
    /// Art der Kategorie. Bei einem Ereignis trägt der Zielordner das Aufnahmedatum —
    /// derselbe Name muss dabei herauskommen wie beim ursprünglichen Lauf.
    /// </param>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    /// <returns>
    /// Die wiederhergestellten Vorschläge in der Reihenfolge ihrer Dateinamen; leer, wenn
    /// nichts zu finden war.
    /// </returns>
    public async Task<IReadOnlyList<SortProposal>> RecoverAsync(
        string sourceFolder,
        string categoryName,
        CategoryKind kind,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFolder);
        ArgumentException.ThrowIfNullOrWhiteSpace(categoryName);

        IReadOnlyList<SortMemoryRecord> records =
            await _memory.GetForFolderAsync(sourceFolder, cancellationToken).ConfigureAwait(false);

        List<SortProposal> proposals = [];
        int missing = 0;

        foreach (SortMemoryRecord record in records)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Nur der offene Vorschlag kommt zurück. „Einsortiert" liegt längst im
            // Zielordner, „abgelehnt" und „abgewählt" sind Entscheidungen, die zu achten
            // sind — sie erneut vorzuschlagen wäre das Gegenteil eines Gedächtnisses.
            if (record.Status is not SortMemoryStatus.Proposed
                || !string.Equals(record.CategoryName, categoryName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Photo? photo = await TryReadAsync(record, cancellationToken).ConfigureAwait(false);
            if (photo is null)
            {
                missing++;
                continue;
            }

            proposals.Add(new SortProposal
            {
                Photo = photo,
                CategoryName = categoryName,
                SourceFolder = sourceFolder,
                TargetFolderPath = BuildTargetFolder(sourceFolder, categoryName, kind, photo),
                Confidence = record.Confidence,
                Method = ClassificationMethod.Embedding,
            });
        }

        // Nach Dateiname sortiert: Das Gedächtnis kennt keine Reihenfolge, und eine
        // Vorschau, deren Reihenfolge sich bei jedem Aufruf ändert, ist nicht prüfbar.
        proposals.Sort((first, second) =>
            string.Compare(first.Photo.FullPath, second.Photo.FullPath, StringComparison.OrdinalIgnoreCase));

        RecoveryLog.Recovered(_logger, proposals.Count, categoryName);
        if (missing > 0)
        {
            RecoveryLog.PhotosGone(_logger, missing);
        }

        return proposals;
    }

    // Die Datei muss noch da und unverändert sein. Die Signatur schließt Pfad, Größe und
    // Aufnahmezeit ein: Stimmt sie nicht mehr, ist es nicht mehr dasselbe Foto, und das
    // gemerkte Urteil gilt nicht für das, was jetzt dort liegt.
    private async Task<Photo?> TryReadAsync(SortMemoryRecord record, CancellationToken cancellationToken)
    {
        try
        {
            FileInfo info = new(record.PhotoPath);
            if (!info.Exists)
            {
                return null;
            }

            PhotoMetadata? metadata = await _metadataReader
                .ReadAsync(record.PhotoPath, cancellationToken)
                .ConfigureAwait(false);

            Photo photo = new()
            {
                FullPath = info.FullName,
                FileName = info.Name,
                SizeBytes = info.Length,
                CapturedAt = metadata?.CapturedAt ?? new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero),
                Width = metadata?.Width,
                Height = metadata?.Height,
                CameraModel = metadata?.CameraModel,
                Latitude = metadata?.Latitude,
                Longitude = metadata?.Longitude,
            };

            return string.Equals(photo.ComputeSignature(), record.FileSignature, StringComparison.Ordinal)
                ? photo
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            RecoveryLog.PhotoUnreadable(_logger, ex);
            return null;
        }
    }

    // Muss denselben Namen ergeben wie der ursprüngliche Lauf — sonst landeten die Fotos
    // in einem zweiten Ordner neben dem, den es schon gibt.
    private static string BuildTargetFolder(
        string sourceFolder,
        string categoryName,
        CategoryKind kind,
        Photo photo)
    {
        string folderName = TargetFolderNaming.SanitizeFolderName(categoryName);
        if (kind == CategoryKind.Event && photo.CapturedAt is { } captured)
        {
            folderName = $"{folderName} {captured.ToString("dd.MM.yy", System.Globalization.CultureInfo.InvariantCulture)}";
        }

        return Path.Combine(sourceFolder, folderName);
    }
}

/// <summary>
/// Quellgenerierte Logmeldungen der Wiederherstellung aus dem Gedächtnis.
/// </summary>
internal static partial class RecoveryLog
{
    [LoggerMessage(EventId = 5210, Level = LogLevel.Information, Message = "{Count} Vorschläge für {Category} aus dem Gedächtnis wiederhergestellt; die KI wurde dafür nicht befragt.")]
    public static partial void Recovered(ILogger logger, int count, string category);

    [LoggerMessage(EventId = 5211, Level = LogLevel.Warning, Message = "{Count} gemerkte Fotos sind nicht mehr an ihrem Platz oder wurden verändert und konnten nicht wiederhergestellt werden.")]
    public static partial void PhotosGone(ILogger logger, int count);

    [LoggerMessage(EventId = 5212, Level = LogLevel.Warning, Message = "Ein gemerktes Foto konnte beim Wiederherstellen nicht gelesen werden.")]
    public static partial void PhotoUnreadable(ILogger logger, Exception exception);
}
