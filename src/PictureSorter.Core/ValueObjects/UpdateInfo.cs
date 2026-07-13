namespace PictureSorter.Core.ValueObjects;

/// <summary>
/// Ergebnis der Update-Prüfung gegen die Veröffentlichungen (Releases) auf GitHub.
/// Vergleicht die laufende Version mit der neuesten veröffentlichten Version.
/// </summary>
public sealed record UpdateInfo
{
    /// <summary>
    /// Die aktuell laufende Version (z. B. „1.2.0").
    /// </summary>
    public required string CurrentVersion { get; init; }

    /// <summary>
    /// Die neueste auf GitHub veröffentlichte Version (z. B. „1.3.0").
    /// </summary>
    public required string LatestVersion { get; init; }

    /// <summary>
    /// <see langword="true"/>, wenn die veröffentlichte Version neuer ist als die
    /// laufende.
    /// </summary>
    public required bool IsUpdateAvailable { get; init; }

    /// <summary>
    /// Das Update-Paket (ZIP) für die laufende Architektur, falls das Release eines
    /// enthält.
    /// </summary>
    public Uri? PackageDownloadUrl { get; init; }

    /// <summary>
    /// Die losgelöste Signatur des Pakets. Ohne sie wird nichts eingespielt: Sie ist
    /// der einzige Beleg dafür, dass das Paket aus der richtigen Hand stammt.
    /// </summary>
    public Uri? SignatureDownloadUrl { get; init; }

    /// <summary>
    /// Verweis auf die Release-Seite (für „Details anzeigen"), falls vorhanden.
    /// </summary>
    public Uri? ReleaseUrl { get; init; }
}
