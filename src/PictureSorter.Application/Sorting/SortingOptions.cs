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

    /// <summary>
    /// Wie viele Fotos gleichzeitig bewertet werden.
    ///
    /// Jede Bewertung ist ein vollständiger Aufruf des Bild-Modells und dauert Sekunden;
    /// nacheinander summiert sich das bei tausend Fotos auf Stunden. Die Anwendung
    /// wartet dabei fast nur auf Ollama, kann also mehrere Anfragen offen halten.
    ///
    /// Vier ist bewusst nicht mehr: Ollama arbeitet nur eine begrenzte Zahl von Anfragen
    /// wirklich gleichzeitig ab (Voreinstellung <c>OLLAMA_NUM_PARALLEL</c>), der Rest
    /// wartet dort in einer Schlange. Zu viele gleichzeitige Anfragen werden deshalb
    /// nicht schneller, sondern laufen einzeln ins Zeitlimit
    /// (<c>Ollama:RequestTimeoutSeconds</c>) — und ein Zeitlimit lässt das Bild
    /// unbewertet. Wer Ollama höher eingestellt hat, darf hier mitziehen.
    /// </summary>
    [Range(1, 32)]
    public int MaxParallelEvaluations { get; set; } = 4;
}
