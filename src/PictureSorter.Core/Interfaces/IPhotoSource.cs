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
    /// <param name="progress">
    /// Optionaler Fortschritt des Einlesens. Er wird gebraucht, weil dieser Schritt bei
    /// einem großen Ordner der langwierigste des ganzen Laufs sein kann: Für jede Datei
    /// wird sie einmal geöffnet. Ohne Meldung stünde die Oberfläche die ganze Zeit bei
    /// einer unbestimmten Anzeige, noch bevor das erste Bild bewertet ist.
    /// </param>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    /// <returns>Die gefundenen Fotos.</returns>
    Task<IReadOnlyList<Photo>> GetPhotosAsync(
        string folderPath,
        bool includeSubfolders,
        int skip,
        int? maxCount,
        IProgress<PhotoScanProgress>? progress,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reicht die Bilder eines Ordners einzeln weiter, sobald sie eingelesen sind, statt
    /// erst alle zu sammeln.
    ///
    /// Das ist der Unterschied zwischen „erst zwanzig Minuten laden, dann bewerten" und
    /// einer Kette, in der beides nebeneinander läuft: Die Bewertung beginnt beim ersten
    /// fertigen Bild. Wer alle Bilder auf einmal braucht, nimmt <see cref="GetPhotosAsync"/>.
    ///
    /// Die Bilder kommen in der Reihenfolge, in der sie fertig werden — mehrere Dateien
    /// werden gleichzeitig gelesen. Wer die Reihenfolge des Ordners braucht, ordnet nach
    /// <see cref="ScannedPhoto.Index"/>.
    /// </summary>
    /// <param name="folderPath">Absoluter Pfad des Quellordners.</param>
    /// <param name="includeSubfolders">
    /// <see langword="true"/>, um Unterordner einzubeziehen.
    /// </param>
    /// <param name="skip">Wie viele passende Bilder am Anfang übersprungen werden.</param>
    /// <param name="maxCount">
    /// Höchstzahl der einzulesenden Fotos, oder <see langword="null"/> für alle.
    /// </param>
    /// <param name="progress">Optionaler Fortschritt des Einlesens.</param>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    /// <returns>Die Bilder, sobald sie gelesen sind.</returns>
    IAsyncEnumerable<ScannedPhoto> StreamPhotosAsync(
        string folderPath,
        bool includeSubfolders,
        int skip,
        int? maxCount,
        IProgress<PhotoScanProgress>? progress,
        CancellationToken cancellationToken);
}

/// <summary>
/// Ein eingelesenes Foto samt seines Platzes in der Ordner-Reihenfolge.
/// </summary>
/// <param name="Photo">Das eingelesene Foto.</param>
/// <param name="Index">
/// Der Platz in der Reihenfolge des Ordners. Weil mehrere Dateien gleichzeitig gelesen
/// werden, kommen die Bilder in der Reihenfolge, in der sie fertig werden; über den
/// Index lässt sich das Ergebnis wieder in die Ordner-Reihenfolge bringen.
/// </param>
/// <param name="Total">
/// Gesamtzahl der gefundenen Bilddateien. Steht schon beim ersten Bild fest und ist die
/// Bezugsgröße beider Fortschrittsbalken.
/// </param>
public readonly record struct ScannedPhoto(Photo Photo, int Index, int Total);

/// <summary>
/// Fortschritt beim Einlesen der Bilddateien eines Ordners.
/// </summary>
/// <param name="Processed">Anzahl der bereits eingelesenen Dateien.</param>
/// <param name="Total">
/// Gesamtzahl der gefundenen Bilddateien. Steht fest, sobald das Verzeichnis
/// aufgezählt ist – und damit lange bevor die letzte Datei gelesen ist.
/// </param>
public readonly record struct PhotoScanProgress(int Processed, int Total);
