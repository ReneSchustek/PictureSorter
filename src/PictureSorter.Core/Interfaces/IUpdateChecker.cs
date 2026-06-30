using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Core.Interfaces;

/// <summary>
/// Prüft, ob eine neuere Programmversion veröffentlicht wurde. Kapselt den Zugriff
/// auf die Veröffentlichungsquelle (z. B. die GitHub-Release-API).
/// </summary>
public interface IUpdateChecker
{
    /// <summary>
    /// Prüft, ob eine neuere Version als die laufende verfügbar ist.
    /// </summary>
    /// <param name="currentVersion">Die laufende Version (z. B. „1.2.0").</param>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    /// <returns>
    /// Die Update-Information, oder <see langword="null"/>, wenn die Prüfung nicht
    /// möglich war (nicht konfiguriert, offline, Quelle nicht erreichbar).
    /// </returns>
    Task<UpdateInfo?> CheckAsync(string currentVersion, CancellationToken cancellationToken);
}
