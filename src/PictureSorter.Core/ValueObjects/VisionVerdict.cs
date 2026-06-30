namespace PictureSorter.Core.ValueObjects;

/// <summary>
/// Antwort des Vision-Modells auf die Frage, ob ein Bild zu einer Kategorie passt.
/// </summary>
public sealed record VisionVerdict
{
    /// <summary>
    /// <see langword="true"/>, wenn das Modell das Bild der Kategorie zuordnet.
    /// </summary>
    public required bool Matches { get; init; }

    /// <summary>
    /// Konfidenz des Modells im Bereich 0.0 bis 1.0.
    /// </summary>
    public required double Confidence { get; init; }

    /// <summary>
    /// Kurze Begründung des Modells, falls vorhanden (für Nachvollziehbarkeit).
    /// </summary>
    public string? Reason { get; init; }
}
