using System.ComponentModel.DataAnnotations;

namespace PictureSorter.Application.Sorting;

/// <summary>
/// Schwellwerte der Urlaubs-Erkennung. Wird aus dem Abschnitt „TripDetection" der
/// <c>appsettings.json</c> gebunden.
/// </summary>
public sealed class TripDetectionOptions
{
    /// <summary>
    /// Name des Konfigurationsabschnitts.
    /// </summary>
    public const string SectionName = "TripDetection";

    /// <summary>
    /// Wie viele Tage ohne ein einziges Foto ein Zeitraum überbrücken darf, bevor er endet.
    ///
    /// Zwei heißt: Ein einzelner Tag ohne Bilder — Regentag, langer Anfahrtstag —
    /// überbrückt der Vorschlag, statt die Reise in zwei Teile zu zerlegen. Genau das ist
    /// im Alltag der häufigere Fall.
    ///
    /// Der Preis: Ein Alltagsfoto, das genau zwei Tage vor der Abreise entstanden ist,
    /// wird mit hineingezogen und lässt den Vorschlag zwei Tage zu früh beginnen. Das ist
    /// die harmlosere Seite — der Vorschlag ist ein Angebot, und die beiden Datumsfelder
    /// lassen sich von Hand nachziehen. Ein zerrissener Urlaub dagegen sieht nach einem
    /// Fehler aus.
    /// </summary>
    [Range(1, 60)]
    public int MaxGapDays { get; set; } = 2;

    /// <summary>
    /// Wie viele Fotos ein Zeitraum mindestens enthalten muss, um vorgeschlagen zu werden.
    ///
    /// Ohne Untergrenze stünden zwischen den Urlauben dutzende Vorschläge aus zwei
    /// Sonntagsfotos, und die Liste wäre nicht zu gebrauchen.
    /// </summary>
    [Range(2, 1000)]
    public int MinPhotos { get; set; } = 8;
}
