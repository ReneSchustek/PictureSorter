using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Core.Interfaces;

/// <summary>
/// Persistenter Zwischenspeicher für berechnete Bild-Embeddings. Da das Erzeugen
/// eines Embeddings (Vision-Beschreibung + Vektor) je Foto teuer ist, kann ein
/// zweiter Lauf so unveränderte Fotos überspringen.
/// </summary>
public interface IEmbeddingCache
{
    /// <summary>
    /// Liefert ein zwischengespeichertes Embedding für den Schlüssel, falls vorhanden.
    /// </summary>
    /// <param name="key">Eindeutiger Schlüssel (Datei-Signatur + Modell).</param>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    /// <returns>Das Embedding oder <see langword="null"/>, wenn nichts zwischengespeichert ist.</returns>
    Task<ImageEmbedding?> GetAsync(string key, CancellationToken cancellationToken);

    /// <summary>
    /// Legt ein berechnetes Embedding unter dem Schlüssel ab.
    /// </summary>
    /// <param name="key">Eindeutiger Schlüssel (Datei-Signatur + Modell).</param>
    /// <param name="embedding">Das zu speichernde Embedding.</param>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    Task SetAsync(string key, ImageEmbedding embedding, CancellationToken cancellationToken);
}
