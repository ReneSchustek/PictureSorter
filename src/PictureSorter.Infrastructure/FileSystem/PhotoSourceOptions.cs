using System.ComponentModel.DataAnnotations;

namespace PictureSorter.Infrastructure.FileSystem;

/// <summary>
/// Einstellungen des Einlesens der Bilddateien. Wird aus dem Abschnitt „PhotoSource"
/// der <c>appsettings.json</c> gebunden.
/// </summary>
public sealed class PhotoSourceOptions
{
    /// <summary>
    /// Name des Konfigurationsabschnitts.
    /// </summary>
    public const string SectionName = "PhotoSource";

    /// <summary>
    /// Wie viele Dateien gleichzeitig eingelesen werden.
    ///
    /// Das Einlesen wartet fast nur: auf die Festplatte, und bei einem Ordner aus der
    /// Cloud (iCloud-Fotos unter Windows, OneDrive) auf den Download der Datei. Wartezeit
    /// lässt sich übereinanderlegen, ohne dass es etwas kostet – deshalb wird der
    /// langsamste Abschnitt eines Laufs hier um ein Vielfaches kürzer.
    ///
    /// Acht ist bewusst kein hoher Wert: Eine mechanische Festplatte wird durch zu viele
    /// gleichzeitige Zugriffe langsamer statt schneller, weil der Kopf zwischen den
    /// Dateien hin- und herspringt.
    /// </summary>
    [Range(1, 64)]
    public int MaxParallelReads { get; set; } = 8;

    /// <summary>
    /// Wie viele fertig eingelesene Bilder höchstens auf Vorrat bereitliegen, bevor das
    /// Laden wartet.
    ///
    /// Das ist der Vorlauf, den das Laden vor der Bewertung haben darf. Ohne Grenze zöge
    /// die Anwendung bei einem Cloud-Ordner alle 1100 Dateien herunter, während die
    /// Bewertung noch beim zwanzigsten Bild steht — Bandbreite und Plattenplatz für
    /// etwas, das noch lange niemand braucht. Ein Abbruch hätte dann obendrein den
    /// gesamten Download umsonst ausgelöst.
    /// </summary>
    [Range(1, 1000)]
    public int PrefetchBuffer { get; set; } = 50;
}
