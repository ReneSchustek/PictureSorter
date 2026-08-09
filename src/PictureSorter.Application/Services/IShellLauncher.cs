namespace PictureSorter.Application.Services;

/// <summary>
/// Öffnet Orte und Adressen in den Programmen des Betriebssystems.
/// </summary>
/// <remarks>
/// Eigener Vertrag, damit ein Anzeige-Modell keine Prozesse startet: Ein Test, der die
/// Detailansicht prüft, soll nicht den Datei-Explorer aufgehen lassen.
/// </remarks>
public interface IShellLauncher
{
    /// <summary>
    /// Zeigt eine Datei im Datei-Explorer — den Ordner geöffnet, die Datei ausgewählt.
    /// </summary>
    /// <param name="filePath">Absoluter Pfad der Datei.</param>
    /// <returns><see langword="true"/>, wenn der Explorer gestartet werden konnte.</returns>
    bool ShowInFolder(string filePath);
}
