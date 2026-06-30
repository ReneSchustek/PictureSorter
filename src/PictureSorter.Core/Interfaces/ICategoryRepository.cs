using PictureSorter.Core.Entities;

namespace PictureSorter.Core.Interfaces;

/// <summary>
/// Lädt und speichert die definierten Kategorien samt ihrer gelernten Profile.
/// </summary>
public interface ICategoryRepository
{
    /// <summary>
    /// Lädt alle gespeicherten Kategorien.
    /// </summary>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    /// <returns>Die geladenen Kategorien (leer, wenn noch keine existieren).</returns>
    Task<IReadOnlyList<Category>> LoadAllAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Speichert den vollständigen Satz an Kategorien (ersetzt den bisherigen Stand).
    /// </summary>
    /// <param name="categories">Die zu speichernden Kategorien.</param>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    Task SaveAllAsync(IReadOnlyList<Category> categories, CancellationToken cancellationToken);
}
