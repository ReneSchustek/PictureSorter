namespace PictureSorter.Core.ValueObjects;

/// <summary>
/// Erkennungsmerkmale einer Bilddatei für die Duplikat-Suche: ein
/// kryptografischer Inhalts-Hash (für bit-identische Duplikate) und – sofern das
/// Bild dekodiert werden konnte – ein Wahrnehmungs-Hash (für ähnliche Duplikate).
/// </summary>
public sealed record ImageFingerprint
{
    /// <summary>
    /// Vollständiger Pfad der Bilddatei.
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// Hexadezimaler SHA-256-Hash des Dateiinhalts. Identische Werte bedeuten
    /// bit-identische Dateien.
    /// </summary>
    public required string ContentHash { get; init; }

    /// <summary>
    /// Wahrnehmungs-Hash des Bildinhalts; <see langword="null"/>, wenn das Bild
    /// nicht dekodiert werden konnte (dann ist nur der exakte Vergleich möglich).
    /// </summary>
    public PerceptualHash? Perceptual { get; init; }
}
