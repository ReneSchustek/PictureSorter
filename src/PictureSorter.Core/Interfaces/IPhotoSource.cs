using PictureSorter.Core.Entities;

namespace PictureSorter.Core.Interfaces;

/// <summary>
/// Liefert die Fotos eines Ordners samt Metadaten. Kapselt den Dateisystem- und
/// EXIF-Zugriff von der übrigen Anwendung.
/// </summary>
public interface IPhotoSource
{
    /// <summary>
    /// Liest alle unterstützten Bilddateien eines Ordners ein.
    /// </summary>
    /// <param name="folderPath">Absoluter Pfad des Quellordners.</param>
    /// <param name="includeSubfolders">
    /// <see langword="true"/>, um Unterordner einzubeziehen.
    /// </param>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    /// <returns>Die gefundenen Fotos.</returns>
    Task<IReadOnlyList<Photo>> GetPhotosAsync(
        string folderPath,
        bool includeSubfolders,
        CancellationToken cancellationToken);
}
