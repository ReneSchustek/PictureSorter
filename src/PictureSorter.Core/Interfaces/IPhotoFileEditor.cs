using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Core.Interfaces;

/// <summary>
/// Benennt einzelne Bilddateien um und verschiebt sie — die Bearbeitung aus der
/// Detailansicht heraus.
/// </summary>
/// <remarks>
/// Getrennt vom <see cref="IProposalApplier"/>, obwohl beide Dateien bewegen: Dort geht
/// es um einen Lauf über hunderte Bilder mit Protokoll und Rückgängig, hier um eine
/// einzelne Datei auf Zuruf. Ein gemeinsamer Vertrag hätte für beide Seiten die falschen
/// Zusagen gemacht.
///
/// Kein Aufruf wirft bei einem vorhersehbaren Hindernis. Eine gesperrte Datei, ein
/// vergebener Name, ein fehlendes Recht — all das ist im Alltag normal und kommt als
/// Ergebnis zurück, das die Oberfläche erklären kann. Eine Ausnahme bliebe entweder
/// unbehandelt oder würde zu einer Meldung, die niemandem sagt, was zu tun ist.
/// </remarks>
public interface IPhotoFileEditor
{
    /// <summary>
    /// Gibt einer Bilddatei einen neuen Namen; sie bleibt in ihrem Ordner.
    /// </summary>
    /// <param name="filePath">Absoluter Pfad der Datei.</param>
    /// <param name="newName">
    /// Der neue Name, mit oder ohne Endung. Fehlt die Endung, bleibt die bisherige
    /// erhalten — sonst wäre das Bild mit einem Klick nicht mehr zu öffnen.
    /// </param>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    /// <returns>Das Ergebnis samt neuem Pfad oder Grund des Scheiterns.</returns>
    Task<FileEditResult> RenameAsync(string filePath, string newName, CancellationToken cancellationToken);

    /// <summary>
    /// Verschiebt eine Bilddatei in einen anderen Ordner.
    /// </summary>
    /// <param name="filePath">Absoluter Pfad der Datei.</param>
    /// <param name="targetFolder">Der Zielordner. Er wird angelegt, wenn er fehlt.</param>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    /// <returns>Das Ergebnis samt neuem Pfad oder Grund des Scheiterns.</returns>
    Task<FileEditResult> MoveAsync(string filePath, string targetFolder, CancellationToken cancellationToken);
}
