using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Core.Interfaces;

/// <summary>
/// Dauerhaftes Protokoll der Umbenennungs-Läufe. Es hält fest, welche Datei von
/// welchem Namen auf welchen kam, und ist damit die Grundlage des Zurücknehmens.
/// Weil es dauerhaft ist, überlebt es einen Neustart: Was gestern umbenannt wurde,
/// lässt sich heute noch zurückgeben.
/// </summary>
public interface IRenameJournal
{
    /// <summary>
    /// Schreibt einen abgeschlossenen Lauf ins Protokoll.
    /// </summary>
    /// <param name="run">Der Lauf samt seiner Umbenennungen.</param>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    Task RecordAsync(RenameRun run, CancellationToken cancellationToken);

    /// <summary>
    /// Liefert den jüngsten Lauf, der noch nicht zurückgenommen wurde.
    /// </summary>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    /// <returns>Der Lauf, oder <see langword="null"/>, wenn es nichts zurückzunehmen gibt.</returns>
    Task<RenameRun?> GetLastUndoableAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Markiert einen Lauf als zurückgenommen, sodass er nicht erneut angeboten wird.
    /// </summary>
    /// <param name="runId">Die Kennung des Laufs.</param>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    Task MarkUndoneAsync(Guid runId, CancellationToken cancellationToken);
}
