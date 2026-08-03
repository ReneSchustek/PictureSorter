namespace PictureSorter.Core.Enums;

/// <summary>
/// Gibt an, auf welchem Weg eine Zuordnung zustande kam. Wichtig für
/// Nachvollziehbarkeit und die Anzeige der Konfidenz in der Oberfläche.
/// </summary>
public enum ClassificationMethod
{
    /// <summary>
    /// Noch nicht bewertet.
    /// </summary>
    NotEvaluated,

    /// <summary>
    /// Zuordnung über Embedding-Ähnlichkeit (schnelle Vorsortierung).
    /// </summary>
    Embedding,

    /// <summary>
    /// Zuordnung über das Vision-Modell (Prüfung von Grenzfällen).
    /// </summary>
    VisionModel,

    /// <summary>
    /// Zuordnung allein über das Aufnahmedatum, ohne jede KI-Bewertung. Der Weg für
    /// „alles aus diesem Urlaub": Dort entscheidet der Zeitraum, nicht das Motiv.
    /// </summary>
    CaptureDate,

    /// <summary>
    /// Manuell vom Nutzer bestätigt oder korrigiert.
    /// </summary>
    Manual,
}
