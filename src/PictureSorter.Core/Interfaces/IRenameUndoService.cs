using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Core.Interfaces;

/// <summary>
/// Nimmt einen Umbenennungs-Lauf zurück: Jede Datei bekommt ihren alten Namen wieder.
/// </summary>
/// <remarks>
/// Eigener Vertrag und nicht Teil des Umbenennens: Er wird zu einer anderen Zeit
/// ausgelöst — oft Tage später und nach einem Neustart — und arbeitet allein aus dem
/// Protokoll. Er braucht weder Ordner noch Vorschau.
/// </remarks>
public interface IRenameUndoService
{
    /// <summary>
    /// Liefert den jüngsten Lauf, der sich noch zurücknehmen lässt.
    /// </summary>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    /// <returns>Der Lauf, oder <see langword="null"/>, wenn es nichts zurückzunehmen gibt.</returns>
    Task<RenameRun?> GetUndoableRunAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Gibt allen Dateien des jüngsten Laufs ihren alten Namen zurück.
    /// </summary>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    /// <returns>
    /// Wie viele Namen zurückgegeben wurden, oder <see langword="null"/>, wenn es
    /// nichts zurückzunehmen gab.
    /// </returns>
    Task<UndoResult?> UndoLastRunAsync(CancellationToken cancellationToken);
}
