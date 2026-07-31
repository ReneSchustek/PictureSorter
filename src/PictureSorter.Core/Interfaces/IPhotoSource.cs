using PictureSorter.Core.Entities;

namespace PictureSorter.Core.Interfaces;

/// <summary>
/// Liefert die Fotos eines Ordners samt Metadaten. Kapselt den Dateisystem- und
/// EXIF-Zugriff von der übrigen Anwendung.
/// </summary>
public interface IPhotoSource
{
    /// <summary>
    /// Liest die unterstützten Bilddateien eines Ordners ein.
    /// </summary>
    /// <param name="folderPath">Absoluter Pfad des Quellordners.</param>
    /// <param name="includeSubfolders">
    /// <see langword="true"/>, um Unterordner einzubeziehen.
    /// </param>
    /// <param name="skip">
    /// Wie viele passende Bilder am Anfang übersprungen werden. Damit lässt sich ein
    /// weiterer Schwung Beispiele holen, ohne die bereits gezeigten erneut einzulesen.
    /// </param>
    /// <param name="maxCount">
    /// Höchstzahl der einzulesenden Fotos, oder <see langword="null"/> für alle. Wer nur
    /// eine Handvoll Bilder braucht, soll auch nur diese einlesen: Das Ermitteln der
    /// Metadaten öffnet jede Datei einzeln, und bei einem Ordner, dessen Dateien erst
    /// aus der Cloud geholt werden (etwa die iCloud-Fotos unter Windows), zieht jedes
    /// Öffnen einen vollständigen Download nach sich.
    /// </param>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    /// <returns>Die gefundenen Fotos.</returns>
    Task<IReadOnlyList<Photo>> GetPhotosAsync(
        string folderPath,
        bool includeSubfolders,
        int skip,
        int? maxCount,
        CancellationToken cancellationToken);
}
