using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Core.Interfaces;

/// <summary>
/// Dauerhaftes Protokoll der Sortierläufe. Es hält fest, welche Datei wohin
/// verschoben wurde, und ist damit die Grundlage des Rückgängigmachens. Weil es
/// dauerhaft ist, überlebt es einen Neustart: Was gestern sortiert wurde, lässt sich
/// heute noch zurücknehmen.
/// </summary>
public interface ISortJournal
{
    /// <summary>
    /// Schreibt einen abgeschlossenen Lauf ins Protokoll.
    /// </summary>
    /// <param name="run">Der Lauf samt seiner Verschiebungen.</param>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    Task RecordAsync(SortRun run, CancellationToken cancellationToken);

    /// <summary>
    /// Liefert den jüngsten Lauf, der noch nicht zurückgenommen wurde.
    /// </summary>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    /// <returns>Der Lauf, oder <see langword="null"/>, wenn es nichts zurückzunehmen gibt.</returns>
    Task<SortRun?> GetLastUndoableAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Markiert einen Lauf als zurückgenommen, sodass er nicht erneut angeboten wird.
    /// </summary>
    /// <param name="runId">Die Kennung des Laufs.</param>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    Task MarkUndoneAsync(Guid runId, CancellationToken cancellationToken);
}
