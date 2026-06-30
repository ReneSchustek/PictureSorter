namespace PictureSorter.Core.Enums;

/// <summary>
/// Ergebnis der Bewertung eines Bildes für eine Kategorie.
/// </summary>
public enum SortDecision
{
    /// <summary>
    /// Bild gehört in die Kategorie.
    /// </summary>
    Assigned,

    /// <summary>
    /// Bild gehört nicht in die Kategorie.
    /// </summary>
    Rejected,

    /// <summary>
    /// Unsicher — sollte vom Nutzer oder vom Vision-Modell geprüft werden.
    /// </summary>
    Uncertain,
}
