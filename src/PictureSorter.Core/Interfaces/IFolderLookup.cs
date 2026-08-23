namespace PictureSorter.Core.Interfaces;

/// <summary>
/// Sucht zu einem Pfad, den es nicht gibt, den Ordner, der gemeint war.
/// </summary>
public interface IFolderLookup
{
    /// <summary>
    /// Liefert den tatsächlich vorhandenen Ordner, der sich vom gesuchten nur in
    /// unsichtbaren Zeichen unterscheidet - etwa einem geschützten Leerzeichen
    /// anstelle eines gewöhnlichen.
    /// </summary>
    /// <param name="requestedPath">Der Pfad, den die Nutzerin angegeben hat.</param>
    /// <returns>
    /// Der vorhandene Pfad, oder <see langword="null"/>, wenn es keinen solchen gibt
    /// (auch dann, wenn der gesuchte Ordner selbst existiert - dann ist nichts zu
    /// erklären).
    /// </returns>
    string? FindLookalike(string requestedPath);
}
