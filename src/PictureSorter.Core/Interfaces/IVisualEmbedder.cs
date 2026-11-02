using PictureSorter.Core.Entities;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Core.Interfaces;

/// <summary>
/// Wandelt Bilder direkt in vergleichbare Vektoren, ohne den Umweg über eine
/// Bildbeschreibung. Auf einem Rechner ohne Grafikkarte macht das aus Tagen Minuten.
/// </summary>
public interface IVisualEmbedder
{
    /// <summary>
    /// Sorgt dafür, dass die Modelldateien bereitliegen, und lädt sie beim ersten Mal.
    /// </summary>
    /// <param name="progress">Meldet den Stand des Ladens; darf fehlen.</param>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    /// <returns>Eine Aufgabe, die endet, sobald das Modell benutzbar ist.</returns>
    /// <exception cref="Exceptions.AiUnavailableException">Die Modelldateien sind nicht zu bekommen.</exception>
    Task PrepareAsync(IProgress<ModelDownloadProgress>? progress, CancellationToken cancellationToken);

    /// <summary>
    /// Berechnet den Vektor eines Fotos. Ein unverändertes Foto kommt aus dem Zwischenspeicher.
    /// </summary>
    /// <param name="photo">Das Foto.</param>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    /// <returns>Der Vektor.</returns>
    /// <exception cref="Exceptions.ImageUnreadableException">Das Bild lässt sich nicht lesen.</exception>
    Task<ImageEmbedding> EmbedPhotoAsync(Photo photo, CancellationToken cancellationToken);
}
