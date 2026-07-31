using PictureSorter.Core.Entities;
using PictureSorter.Core.Enums;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Core.Interfaces;

/// <summary>
/// Lernt aus bestätigten Beispielfotos das Profil einer Kategorie, indem für
/// jedes Beispiel ein Embedding berechnet wird.
/// </summary>
public interface ICategoryTrainer
{
    /// <summary>
    /// Erzeugt eine Kategorie aus ihrer Beschreibung und den bewerteten Beispielen.
    /// </summary>
    /// <param name="name">Name der Kategorie und Basis des Zielordnernamens.</param>
    /// <param name="description">Beschreibung der Kategorie in eigenen Worten.</param>
    /// <param name="kind">Art der Kategorie (Thema oder Ereignis).</param>
    /// <param name="examples">Die bewerteten Beispielfotos (mindestens ein positives).</param>
    /// <param name="progress">
    /// Meldet nach jedem Beispiel den Stand. Für jedes Bild läuft ein vollständiger
    /// Aufruf des Bild-Modells; ohne diese Meldungen stünde die Oberfläche minutenlang
    /// bei einer unveränderten Zeile und wäre nicht von einem Absturz zu unterscheiden.
    /// </param>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    /// <returns>Die angelernte Kategorie samt Beispiel-Embeddings.</returns>
    Task<Category> TrainAsync(
        string name,
        string description,
        CategoryKind kind,
        IReadOnlyList<TrainingExample> examples,
        IProgress<TrainingProgress>? progress,
        CancellationToken cancellationToken);
}
