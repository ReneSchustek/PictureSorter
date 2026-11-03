namespace PictureSorter.Core.Interfaces;

/// <summary>
/// Sucht zu einem Pfad, den es nicht gibt, den Ordner, der gemeint war.
/// </summary>
public interface IFolderLookup
{
    /// <summary>
    /// Liefert den tatsächlich vorhandenen Ordner, der sich vom gesuchten nur in
    /// unsichtbaren Zeichen unterscheidet, etwa einem geschützten Leerzeichen
    /// anstelle eines gewöhnlichen.
    /// </summary>
    /// <param name="requestedPath">Der Pfad, den die Nutzerin angegeben hat.</param>
    /// <returns>
    /// Der vorhandene Pfad, oder <see langword="null"/>, wenn es keinen solchen gibt
    /// (auch dann, wenn der gesuchte Ordner selbst existiert: Dann ist nichts zu
    /// erklären).
    /// </returns>
    string? FindLookalike(string requestedPath);

    /// <summary>
    /// Nennt die sichtbaren Unterordner eines Ordners.
    /// </summary>
    /// <param name="folderPath">Der Ordner.</param>
    /// <returns>Ihre Namen, nach Namen geordnet; leer, wenn der Ordner nicht lesbar ist.</returns>
    IReadOnlyList<string> ListSubfolders(string folderPath);

    /// <summary>
    /// Zählt die sichtbaren Dateien direkt in einem Ordner, die das Programm nicht als Foto liest.
    /// </summary>
    /// <param name="folderPath">Der Ordner.</param>
    /// <returns>Ihre Zahl; 0, wenn der Ordner nicht lesbar ist.</returns>
    int CountUnsupportedFiles(string folderPath);

    /// <summary>
    /// Zählt die Fotos in einem Ordner, die nur online in der Cloud liegen und deshalb nicht gelesen werden.
    /// </summary>
    /// <param name="folderPath">Der Ordner.</param>
    /// <param name="includeSubfolders">Ob Unterordner mitzählen.</param>
    /// <returns>Ihre Zahl; 0, wenn der Ordner nicht lesbar ist.</returns>
    int CountOnlineOnlyPhotos(string folderPath, bool includeSubfolders);
}
