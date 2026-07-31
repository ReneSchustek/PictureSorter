using System.ComponentModel.DataAnnotations;

namespace PictureSorter.Application.Sorting;

/// <summary>
/// Schwellwerte und Grenzen der Sortierlogik. Wird aus dem Abschnitt „Sorting"
/// der <c>appsettings.json</c> gebunden.
/// </summary>
public sealed class SortingOptions
{
    /// <summary>
    /// Name des Konfigurationsabschnitts.
    /// </summary>
    public const string SectionName = "Sorting";

    /// <summary>
    /// Ab dieser Ähnlichkeit gilt ein Bild ohne Vision-Prüfung als zugehörig.
    /// </summary>
    [Range(0.0, 1.0)]
    public double UpperSimilarityThreshold { get; set; } = 0.78;

    /// <summary>
    /// Unter dieser Ähnlichkeit gilt ein Bild ohne Vision-Prüfung als nicht zugehörig.
    /// </summary>
    [Range(0.0, 1.0)]
    public double LowerSimilarityThreshold { get; set; } = 0.45;

    /// <summary>
    /// Mindestkonfidenz, ab der das Vision-Urteil einen Grenzfall als zugehörig wertet.
    /// </summary>
    [Range(0.0, 1.0)]
    public double VisionConfidenceThreshold { get; set; } = 0.5;

    /// <summary>
    /// Ab dieser Anzahl an Verschiebungen verlangt die Oberfläche eine zusätzliche
    /// Bestätigung (Safe-Write).
    /// </summary>
    [Range(1, 100000)]
    public int BulkConfirmationThreshold { get; set; } = 50;

    /// <summary>
    /// Höchstzahl der Beispiele je Seite (passend bzw. nicht passend). Entschieden wird
    /// über die höchste Ähnlichkeit zu einem einzelnen Beispiel: Ein halbpassendes
    /// Beispiel zieht deshalb eine ganze Nachbarschaft falscher Fotos mit herein. Wenige,
    /// eindeutige Beispiele wirken besser als viele mittelmäßige — und jedes zusätzliche
    /// kostet beim Anlernen einen vollständigen Aufruf des Bild-Modells.
    /// </summary>
    [Range(1, 100)]
    public int MaxExamplesPerSide { get; set; } = 15;
}
