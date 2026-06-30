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
}
