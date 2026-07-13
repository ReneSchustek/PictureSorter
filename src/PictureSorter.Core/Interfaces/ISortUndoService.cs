using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Core.Interfaces;

/// <summary>
/// Nimmt den letzten Sortierlauf zurück: holt die verschobenen Fotos an ihren
/// Ursprungsort und lässt die Anwendung vergessen, dass sie einsortiert waren.
/// </summary>
public interface ISortUndoService
{
    /// <summary>
    /// Liefert den Lauf, der zurückgenommen werden kann.
    /// </summary>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    /// <returns>Der Lauf, oder <see langword="null"/>, wenn es nichts zurückzunehmen gibt.</returns>
    Task<SortRun?> GetUndoableRunAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Nimmt den letzten Sortierlauf zurück.
    /// </summary>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    /// <returns>
    /// Wie viele Fotos zurückgeholt und wie viele übersprungen wurden;
    /// <see langword="null"/>, wenn es nichts zurückzunehmen gab.
    /// </returns>
    Task<UndoResult?> UndoLastRunAsync(CancellationToken cancellationToken);
}
