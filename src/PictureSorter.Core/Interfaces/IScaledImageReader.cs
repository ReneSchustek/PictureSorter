using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Core.Interfaces;

/// <summary>
/// Liest ein Bild verkleinert als Bildpunkte. Das Bildformat und die Windows-Dekoder bleiben damit
/// hinter dieser Schnittstelle.
/// </summary>
public interface IScaledImageReader
{
    /// <summary>
    /// Liest ein Bild so, dass seine kürzere Kante die angegebene Länge hat, richtig gedreht.
    /// </summary>
    /// <param name="filePath">Der Pfad des Bildes.</param>
    /// <param name="shortEdge">Die gewünschte Länge der kürzeren Kante.</param>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    /// <returns>Die Bildpunkte.</returns>
    /// <exception cref="Exceptions.ImageUnreadableException">Das Bild lässt sich nicht lesen.</exception>
    Task<ScaledImage> ReadAsync(string filePath, int shortEdge, CancellationToken cancellationToken);
}
