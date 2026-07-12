using Microsoft.Extensions.Logging;
using PictureSorter.Core.Entities;
using PictureSorter.Core.Enums;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Application.Sorting;

/// <summary>
/// Kapselt den Zugriff auf das dauerhafte Sortier-Gedächtnis für den Sortierablauf.
///
/// Zwei Gründe für diese Klasse: Sie hält den <see cref="PhotoSortingService"/>
/// frei von Persistenz-Details (Signaturbildung, Zeitstempel), und sie macht jeden
/// Zugriff fehlertolerant. Das Gedächtnis ist eine Beschleunigung, kein
/// Kernbestandteil — ist die Datenbank nicht verfügbar, sortiert die Anwendung
/// weiter, nur eben ohne Überspringen (Graceful Degradation).
/// </summary>
public sealed class SortMemoryGateway
{
    private readonly ISortMemory _memory;
    private readonly IClock _clock;
    private readonly ILogger<SortMemoryGateway> _logger;

    /// <summary>
    /// Initialisiert das Gateway.
    /// </summary>
    /// <param name="memory">Das dauerhafte Sortier-Gedächtnis.</param>
    /// <param name="clock">Testbare Zeitquelle für den Änderungszeitstempel.</param>
    /// <param name="logger">Der Logger.</param>
    public SortMemoryGateway(ISortMemory memory, IClock clock, ILogger<SortMemoryGateway> logger)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        _memory = memory;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>
    /// Prüft, ob für dieses Foto und diese Kategorie bereits eine endgültige
    /// Entscheidung vorliegt. Nur dann darf der teure KI-Aufruf entfallen.
    /// </summary>
    /// <param name="sourceFolder">Der Quellordner.</param>
    /// <param name="photo">Das zu prüfende Foto.</param>
    /// <param name="categoryName">Die Kategorie.</param>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    /// <returns>
    /// <see langword="true"/>, wenn das Foto übersprungen werden kann.
    /// </returns>
    public async Task<bool> IsSettledAsync(
        string sourceFolder,
        Photo photo,
        string categoryName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(photo);

        SortMemoryRecord? remembered = await TryGetAsync(
            sourceFolder,
            photo.ComputeSignature(),
            categoryName,
            cancellationToken).ConfigureAwait(false);

        // „Proposed" ist keine endgültige Entscheidung – ein Vorschlag, der nie
        // angewendet wurde, soll erneut erscheinen.
        return remembered?.Status is SortMemoryStatus.Sorted
            or SortMemoryStatus.Ignored
            or SortMemoryStatus.Rejected;
    }

    /// <summary>
    /// Merkt das Ergebnis einer KI-Bewertung: entweder den Vorschlag oder die
    /// Ablehnung. Nur ausgewertete Fotos werden gemerkt — bei einem KI-Ausfall darf
    /// nichts gespeichert werden, sonst gilt der Ausfall künftig als „gehört nicht
    /// dazu".
    /// </summary>
    /// <param name="sourceFolder">Der Quellordner.</param>
    /// <param name="photo">Das bewertete Foto.</param>
    /// <param name="categoryName">Die geprüfte Kategorie.</param>
    /// <param name="proposal">Der erzeugte Vorschlag, oder <see langword="null"/> bei Ablehnung.</param>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    public Task RememberEvaluationAsync(
        string sourceFolder,
        Photo photo,
        string categoryName,
        SortProposal? proposal,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(photo);

        return proposal is null
            ? RememberAsync(sourceFolder, photo, categoryName, SortMemoryStatus.Rejected, 0.0, cancellationToken)
            : RememberAsync(
                sourceFolder,
                photo,
                categoryName,
                SortMemoryStatus.Proposed,
                proposal.Confidence,
                cancellationToken);
    }

    /// <summary>
    /// Merkt einen angewendeten Vorschlag als erledigt.
    /// </summary>
    /// <param name="proposal">Der angewendete Vorschlag.</param>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    public Task MarkSortedAsync(SortProposal proposal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(proposal);

        return RememberAsync(
            proposal.SourceFolder,
            proposal.Photo,
            proposal.CategoryName,
            SortMemoryStatus.Sorted,
            proposal.Confidence,
            cancellationToken);
    }

    /// <summary>
    /// Merkt einen vom Nutzer abgewählten Vorschlag, damit er nicht erneut erscheint.
    /// </summary>
    /// <param name="proposal">Der abgewählte Vorschlag.</param>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    public Task MarkIgnoredAsync(SortProposal proposal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(proposal);

        return RememberAsync(
            proposal.SourceFolder,
            proposal.Photo,
            proposal.CategoryName,
            SortMemoryStatus.Ignored,
            proposal.Confidence,
            cancellationToken);
    }

    private async Task<SortMemoryRecord?> TryGetAsync(
        string sourceFolder,
        string signature,
        string categoryName,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _memory.GetAsync(sourceFolder, signature, categoryName, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (IsRecoverable(ex))
        {
            MemoryLog.ReadFailed(_logger, ex);
            return null;
        }
    }

    private async Task RememberAsync(
        string sourceFolder,
        Photo photo,
        string categoryName,
        SortMemoryStatus status,
        double confidence,
        CancellationToken cancellationToken)
    {
        SortMemoryRecord record = new()
        {
            FolderPath = sourceFolder,
            FileSignature = photo.ComputeSignature(),
            PhotoPath = photo.FullPath,
            CategoryName = categoryName,
            Status = status,
            Confidence = confidence,
            UpdatedAt = _clock.UtcNow,
        };

        try
        {
            await _memory.UpsertAsync(record, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsRecoverable(ex))
        {
            MemoryLog.WriteFailed(_logger, photo.FileName, ex);
        }
    }

    // Datenbankprobleme (gesperrte Datei, defektes Schema) dürfen einen laufenden
    // Sortiervorgang nicht abbrechen. Ein Abbruch durch den Nutzer schon.
    private static bool IsRecoverable(Exception exception) =>
        exception is not OperationCanceledException
        && exception is IOException or InvalidOperationException or TimeoutException;
}

/// <summary>
/// Quellgenerierte Logmeldungen des Gedächtnis-Zugriffs.
/// </summary>
internal static partial class MemoryLog
{
    [LoggerMessage(EventId = 3500, Level = LogLevel.Warning, Message = "Das Sortier-Gedächtnis konnte nicht gelesen werden; es wird ohne Überspringen weitergearbeitet.")]
    public static partial void ReadFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 3501, Level = LogLevel.Warning, Message = "Die Entscheidung zu {FileName} konnte nicht gemerkt werden.")]
    public static partial void WriteFailed(ILogger logger, string fileName, Exception exception);
}
