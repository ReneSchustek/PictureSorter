using Microsoft.Extensions.Logging;
using PictureSorter.Core.Enums;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Application.Sorting;

/// <summary>
/// Nimmt den letzten Sortierlauf zurück. Ein Sortierlauf verschiebt Fotos – für die
/// Zielnutzerin ist das der einzige Schritt der Anwendung, der ihre Dateien wirklich
/// bewegt, und er soll umkehrbar sein.
///
/// Zurückgeholt wird nur, was sich zweifelsfrei zuordnen lässt: Die Datei muss noch
/// dort liegen, wo der Lauf sie hingelegt hat, und ihr Ursprungsort muss frei sein.
/// Überschrieben wird nie – sonst wäre ausgerechnet das Rückgängig die Stelle, an der
/// Daten verloren gehen. Was nicht zurückkann, wird gezählt und gemeldet.
/// </summary>
public sealed class SortUndoService : ISortUndoService
{
    private readonly SortJournalGateway _journal;
    private readonly SortMemoryGateway _memory;
    private readonly IFileOrganizer _fileOrganizer;
    private readonly ILogger<SortUndoService> _logger;

    /// <summary>
    /// Initialisiert den Dienst.
    /// </summary>
    /// <param name="journal">Das Protokoll der Sortierläufe.</param>
    /// <param name="memory">Das Sortier-Gedächtnis.</param>
    /// <param name="fileOrganizer">Das Zurückholen der Dateien.</param>
    /// <param name="logger">Der Logger.</param>
    public SortUndoService(
        SortJournalGateway journal,
        SortMemoryGateway memory,
        IFileOrganizer fileOrganizer,
        ILogger<SortUndoService> logger)
    {
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(fileOrganizer);
        ArgumentNullException.ThrowIfNull(logger);

        _journal = journal;
        _memory = memory;
        _fileOrganizer = fileOrganizer;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<SortRun?> GetUndoableRunAsync(CancellationToken cancellationToken) =>
        _journal.GetLastUndoableAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<UndoResult?> UndoLastRunAsync(CancellationToken cancellationToken)
    {
        SortRun? run = await _journal.GetLastUndoableAsync(cancellationToken).ConfigureAwait(false);
        if (run is null)
        {
            return null;
        }

        int restored = 0;
        int skipped = 0;

        foreach (SortRunItem item in run.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            bool moved;
            try
            {
                // Nach einem Kopierlauf liegt das Original noch im Quellordner.
                // „Zurückholen" hieße dort, es ein zweites Mal hinzulegen – rückgängig
                // ist hier das Entfernen der Kopie, und das nur, wenn sie noch
                // unverändert ist.
                moved = run.Operation is FileOperationMode.Copy
                    ? await _fileOrganizer
                        .DiscardCopyAsync(item.TargetPath, item.TargetLength, item.TargetLastWriteUtc, cancellationToken)
                        .ConfigureAwait(false)
                    : await _fileOrganizer
                        .RestoreAsync(item.TargetPath, item.SourcePath, cancellationToken)
                        .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Eine gesperrte Datei darf das Zurückholen der übrigen nicht verhindern.
                UndoLog.RestoreFailed(_logger, Path.GetFileName(item.TargetPath), ex);
                skipped++;
                continue;
            }

            if (!moved)
            {
                skipped++;
                continue;
            }

            // Das Foto liegt wieder allein im Quellordner. Bliebe es als „einsortiert"
            // gemerkt, würde es nie wieder vorgeschlagen – die Anwendung hätte ein
            // Gedächtnis an eine Sortierung, die es nicht mehr gibt.
            await _memory
                .ForgetAsync(run.SourceFolder, item.FileSignature, run.CategoryName, cancellationToken)
                .ConfigureAwait(false);
            restored++;
        }

        await RemoveEmptyTargetFoldersAsync(run, cancellationToken).ConfigureAwait(false);

        // Der Lauf gilt auch dann als zurückgenommen, wenn einzelne Dateien nicht
        // zurückkonnten: Ein zweiter Versuch würde an denselben Hindernissen scheitern
        // und die Nutzerin nur erneut in die Irre führen.
        await _journal.MarkUndoneAsync(run.Id, cancellationToken).ConfigureAwait(false);
        UndoLog.Undone(_logger, restored, skipped);

        return new UndoResult { Restored = restored, Skipped = skipped };
    }

    // Nach dem Zurückholen bleibt der Kategorie-Ordner leer zurück. Ihn stehen zu
    // lassen weckt den Eindruck, es sei noch etwas einsortiert.
    private async Task RemoveEmptyTargetFoldersAsync(SortRun run, CancellationToken cancellationToken)
    {
        IEnumerable<string> folders = run.Items
            .Select(item => Path.GetDirectoryName(item.TargetPath))
            .Where(folder => !string.IsNullOrEmpty(folder))
            .Distinct(StringComparer.OrdinalIgnoreCase)!;

        foreach (string folder in folders)
        {
            await _fileOrganizer.RemoveFolderIfEmptyAsync(folder, cancellationToken).ConfigureAwait(false);
        }
    }
}

/// <summary>
/// Quellgenerierte Logmeldungen des Rückgängigmachens.
/// </summary>
internal static partial class UndoLog
{
    [LoggerMessage(EventId = 3800, Level = LogLevel.Information, Message = "Sortierlauf zurückgenommen: {Restored} Fotos zurückgeholt, {Skipped} übersprungen.")]
    public static partial void Undone(ILogger logger, int restored, int skipped);

    [LoggerMessage(EventId = 3801, Level = LogLevel.Warning, Message = "{FileName} konnte nicht zurückgeholt werden.")]
    public static partial void RestoreFailed(ILogger logger, string fileName, Exception exception);
}
