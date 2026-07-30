using PictureSorter.Core.ValueObjects;

namespace PictureSorter.App.Services;

/// <summary>
/// Prüfung und Einspielen einer neuen Fassung, so wie die Oberfläche beides braucht.
/// Als Abstraktion, damit die ViewModels ohne Netzzugriff und ohne Dateisystem prüfbar
/// bleiben – der echte Ablauf lädt ein Paket herunter und startet einen Prozess.
/// </summary>
internal interface IUpdateCoordinator
{
    /// <summary>
    /// Prüft auf eine neuere Version.
    /// </summary>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    /// <returns>Die Update-Information oder <see langword="null"/>, wenn nicht prüfbar.</returns>
    Task<UpdateInfo?> CheckAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Lädt die neue Fassung, prüft ihre Signatur und startet das Einspielen.
    /// </summary>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    /// <returns><see langword="true"/>, wenn das Einspielen angelaufen ist.</returns>
    Task<bool> DownloadAndLaunchUpdaterAsync(CancellationToken cancellationToken);
}
