using PictureSorter.Core.Entities;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Core.Interfaces;

/// <summary>
/// Prüft per Vision-Modell, ob ein Bild zu einer Kategorie passt. Dient der
/// genauen Beurteilung von Grenzfällen, die die Embedding-Vorsortierung offenlässt.
/// </summary>
public interface IImageClassifier
{
    /// <summary>
    /// Beurteilt ein Foto anhand der Kategoriebeschreibung.
    /// </summary>
    /// <param name="photo">Das zu prüfende Foto.</param>
    /// <param name="category">Die Kategorie samt Beschreibung.</param>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    /// <returns>Das Urteil des Vision-Modells.</returns>
    /// <exception cref="Exceptions.AiUnavailableException">
    /// Wird ausgelöst, wenn die KI nicht erreichbar ist.
    /// </exception>
    Task<VisionVerdict> ClassifyAsync(
        Photo photo,
        Category category,
        CancellationToken cancellationToken);
}
