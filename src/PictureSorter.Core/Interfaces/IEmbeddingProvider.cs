using PictureSorter.Core.Entities;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Core.Interfaces;

/// <summary>
/// Erzeugt Embeddings für Bilder über die lokale KI. Embeddings sind die
/// Grundlage der schnellen Ähnlichkeits-Vorsortierung.
/// </summary>
public interface IEmbeddingProvider
{
    /// <summary>
    /// Berechnet das Embedding für ein Foto.
    /// </summary>
    /// <param name="photo">Das zu beschreibende Foto.</param>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    /// <returns>Das Embedding des Bildes.</returns>
    /// <exception cref="Exceptions.AiUnavailableException">
    /// Wird ausgelöst, wenn die KI nicht erreichbar ist.
    /// </exception>
    Task<ImageEmbedding> CreateEmbeddingAsync(Photo photo, CancellationToken cancellationToken);
}
