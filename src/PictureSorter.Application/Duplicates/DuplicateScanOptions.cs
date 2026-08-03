using System.ComponentModel.DataAnnotations;

namespace PictureSorter.Application.Duplicates;

/// <summary>
/// Einstellungen der Duplikat-Suche. Wird aus dem Abschnitt „Duplicates" der
/// <c>appsettings.json</c> gebunden.
/// </summary>
public sealed class DuplicateScanOptions
{
    /// <summary>
    /// Name des Konfigurationsabschnitts.
    /// </summary>
    public const string SectionName = "Duplicates";

    /// <summary>
    /// <see langword="true"/>, um zusätzlich zu bit-identischen auch visuell
    /// ähnliche Bilder (skaliert/neu komprimiert) zu erkennen.
    /// </summary>
    public bool DetectSimilar { get; set; } = true;

    /// <summary>
    /// Maximale Hamming-Distanz (0–64) zweier Wahrnehmungs-Hashes, ab der zwei
    /// Bilder noch als ähnlich gelten. Kleiner = strenger.
    /// </summary>
    [Range(0, 64)]
    public int MaxHammingDistance { get; set; } = 8;

    /// <summary>
    /// Wie viele Bilder gleichzeitig ihren Fingerabdruck bekommen. Dabei wird jedes Bild
    /// geladen, verkleinert und durchgerechnet — das wartet teils auf die Platte, teils
    /// auf den Prozessor, und beides lässt sich übereinanderlegen.
    /// </summary>
    [Range(1, 32)]
    public int MaxParallelFingerprints { get; set; } = 4;
}
