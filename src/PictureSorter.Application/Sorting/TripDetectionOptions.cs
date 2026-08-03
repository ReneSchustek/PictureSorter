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
    /// Wie viele Tage ohne ein einziges Foto einen Zeitraum beenden.
    ///
    /// Zwei ist bewusst knapp: Auf einer Reise wird fast täglich fotografiert, und ein
    /// einzelner Regentag ohne Bilder soll den Urlaub nicht in zwei Vorschläge zerlegen.
    /// Bei einem größeren Wert wachsen benachbarte Wochenenden zu einem Block zusammen.
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
