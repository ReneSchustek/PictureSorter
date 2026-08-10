using System;

namespace PictureSorter.App.Services;

/// <summary>
/// Die Startgröße des Lupen-Fensters.
///
/// Eigene Klasse ohne Fensterbezug, weil hier die Regeln stehen: Sie lassen sich so
/// prüfen, ohne dass ein Fenster entstehen muss.
/// </summary>
internal static class ZoomWindowSize
{
    /// <summary>Kleinste Breite, damit das Fenster greifbar bleibt.</summary>
    public const int MinimumWidth = 480;

    /// <summary>Kleinste Höhe, damit das Fenster greifbar bleibt.</summary>
    public const int MinimumHeight = 360;

    // Nicht die ganze Arbeitsfläche: Ein Rand ringsum zeigt, dass darunter das
    // Hauptfenster weitersteht, und lässt Platz zum Anfassen.
    private const double WorkAreaShare = 0.85;

    /// <summary>
    /// Berechnet die Größe des Fensterinhalts aus dem Bild und der freien Fläche.
    ///
    /// Drei Regeln: Das Seitenverhältnis bleibt, die Arbeitsfläche wird nicht
    /// ausgefüllt, und ein kleines Bild wird nicht aufgeblasen — hochskaliert sieht es
    /// nur schlechter aus, gewonnen ist nichts.
    /// </summary>
    /// <param name="pixelWidth">Breite des Bildes in Bildpunkten.</param>
    /// <param name="pixelHeight">Höhe des Bildes in Bildpunkten.</param>
    /// <param name="workWidth">Breite der freien Bildschirmfläche.</param>
    /// <param name="workHeight">Höhe der freien Bildschirmfläche.</param>
    /// <returns>Breite und Höhe des Fensterinhalts.</returns>
    public static (int Width, int Height) Compute(
        int pixelWidth,
        int pixelHeight,
        int workWidth,
        int workHeight)
    {
        if (pixelWidth <= 0 || pixelHeight <= 0 || workWidth <= 0 || workHeight <= 0)
        {
            // Ohne brauchbare Maße bleibt nur ein vernünftiger Anfang.
            return (MinimumWidth, MinimumHeight);
        }

        double factor = Math.Min(
            workWidth * WorkAreaShare / pixelWidth,
            workHeight * WorkAreaShare / pixelHeight);
        factor = Math.Min(factor, 1.0);

        int width = (int)Math.Round(pixelWidth * factor);
        int height = (int)Math.Round(pixelHeight * factor);

        // Die Mindestgröße kann das Verhältnis brechen; das Bild bleibt darin trotzdem
        // unverzerrt, es bekommt nur einen Rand. Das ist der ehrlichere Kompromiss als
        // ein Fenster, das sich nicht mehr anfassen lässt.
        return (Math.Max(width, MinimumWidth), Math.Max(height, MinimumHeight));
    }
}
