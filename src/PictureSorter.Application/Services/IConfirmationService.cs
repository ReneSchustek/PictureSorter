namespace PictureSorter.Application.Services;

/// <summary>
/// Holt vor sicherheitskritischen Aktionen (z. B. dem Löschen vieler Dateien)
/// eine ausdrückliche Bestätigung des Nutzers ein (Safe-Write). Die konkrete
/// Dialog-Umsetzung liegt in der UI-Schicht.
/// </summary>
public interface IConfirmationService
{
    /// <summary>
    /// Zeigt eine Rückfrage mit Bestätigen-/Abbrechen-Auswahl.
    /// </summary>
    /// <param name="title">Titel der Rückfrage.</param>
    /// <param name="message">Erläuternder Text.</param>
    /// <param name="confirmText">Beschriftung der Bestätigen-Schaltfläche.</param>
    /// <param name="cancelText">Beschriftung der Abbrechen-Schaltfläche.</param>
    /// <returns><see langword="true"/>, wenn der Nutzer bestätigt hat.</returns>
    Task<bool> ConfirmAsync(string title, string message, string confirmText, string cancelText);
}
