namespace PictureSorter.Application.Services;

/// <summary>
/// Lässt den Nutzer einen Ordner auswählen. Die konkrete Dialog-Umsetzung liegt
/// in der UI-Schicht; ViewModels bleiben dadurch testbar und frei von WinUI.
/// </summary>
public interface IFolderPicker
{
    /// <summary>
    /// Öffnet einen Ordnerauswahl-Dialog.
    /// </summary>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    /// <returns>
    /// Der gewählte Ordnerpfad, oder <see langword="null"/>, wenn abgebrochen wurde.
    /// </returns>
    Task<string?> PickFolderAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Öffnet einen Auswahldialog für mehrere Bilddateien. Damit kann der Nutzer die
    /// Beispiele selbst bestimmen, statt auf die zufällig ersten Bilder eines Ordners
    /// angewiesen zu sein – bei einem gemischten Ordner ist darunter oft kaum eines,
    /// das zum gesuchten Thema passt.
    /// </summary>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    /// <returns>Die gewählten Pfade; leer, wenn abgebrochen wurde.</returns>
    Task<IReadOnlyList<string>> PickImagesAsync(CancellationToken cancellationToken);
}
