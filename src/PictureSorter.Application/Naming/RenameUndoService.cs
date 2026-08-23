using Microsoft.Extensions.Logging;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Application.Naming;

/// <summary>
/// Gibt den Dateien eines Umbenennungs-Laufs ihre alten Namen zurück.
///
/// Das Zurücknehmen wiegt hier schwerer als beim Sortieren: Beim Sortieren liegt die
/// Datei im falschen Ordner und ist über ihren Namen wiederzufinden. Nach einem
/// Umbenennen ist der alte Name fort, und niemand könnte ihn aus dem Kopf
/// wiederherstellen. Er steht nur noch im Protokoll.
/// </summary>
public sealed class RenameUndoService : IRenameUndoService
{
    private readonly IRenameJournal _journal;
    private readonly IPhotoFileEditor _fileEditor;
    private readonly ILogger<RenameUndoService> _logger;

    /// <summary>
    /// Initialisiert den Dienst.
    /// </summary>
    /// <param name="journal">Das Protokoll der Umbenennungs-Läufe.</param>
    /// <param name="fileEditor">Benennt die einzelne Datei um.</param>
    /// <param name="logger">Der Logger.</param>
    public RenameUndoService(
        IRenameJournal journal,
        IPhotoFileEditor fileEditor,
        ILogger<RenameUndoService> logger)
    {
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(fileEditor);
        ArgumentNullException.ThrowIfNull(logger);

        _journal = journal;
        _fileEditor = fileEditor;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<RenameRun?> GetUndoableRunAsync(CancellationToken cancellationToken) =>
        _journal.GetLastUndoableAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<UndoResult?> UndoLastRunAsync(CancellationToken cancellationToken)
    {
        RenameRun? run = await _journal.GetLastUndoableAsync(cancellationToken).ConfigureAwait(false);
        if (run is null)
        {
            return null;
        }

        int restored = 0;
        int skipped = 0;

        foreach (RenameRunItem item in run.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            FileEditResult result = await _fileEditor
                .RenameAsync(item.NewPath, Path.GetFileName(item.OriginalPath), cancellationToken)
                .ConfigureAwait(false);

            if (result.Succeeded)
            {
                restored++;
            }
            else
            {
                // Wurde die Datei inzwischen von Hand umbenannt oder liegt unter dem
                // alten Namen wieder etwas, bleibt sie, wie sie ist. Überschrieben wird
                // nie.
                UndoRenameLog.RestoreFailed(_logger, Path.GetFileName(item.NewPath), result.Outcome);
                skipped++;
            }
        }

        // Der Lauf gilt auch dann als zurückgenommen, wenn einzelne Dateien ihren Namen
        // behalten mussten: Ein zweiter Versuch scheiterte an denselben Hindernissen
        // und führte die Nutzerin nur erneut in die Irre.
        await _journal.MarkUndoneAsync(run.Id, cancellationToken).ConfigureAwait(false);
        UndoRenameLog.Undone(_logger, restored, skipped);

        return new UndoResult { Restored = restored, Skipped = skipped };
    }
}

/// <summary>
/// Quellgenerierte Logmeldungen des Zurücknehmens.
/// </summary>
internal static partial class UndoRenameLog
{
    [LoggerMessage(EventId = 4310, Level = LogLevel.Information, Message = "{Restored} Namen zurückgegeben, {Skipped} übersprungen.")]
    public static partial void Undone(ILogger logger, int restored, int skipped);

    [LoggerMessage(EventId = 4311, Level = LogLevel.Warning, Message = "Datei {FileName} behielt ihren Namen: {Outcome}.")]
    public static partial void RestoreFailed(ILogger logger, string fileName, Core.Enums.FileEditOutcome outcome);
}
