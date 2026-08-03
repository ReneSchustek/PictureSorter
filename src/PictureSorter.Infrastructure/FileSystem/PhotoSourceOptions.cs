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
    /// Laden wartet. <c>0</c> bedeutet: ohne Grenze – das Laden wartet nie auf die
    /// Bewertung.
    ///
    /// Voreingestellt ist die Entkopplung (<c>0</c>), und zwar aus zwei Gründen. Erstens
    /// kostet der Vorrat kaum Speicher: Gepuffert wird nur ein <c>Photo</c> mit seinen
    /// Metadaten, nie der Bildinhalt — selbst zehntausend Einträge bleiben im
    /// zweistelligen Megabyte-Bereich. Zweitens sparte die alte Grenze von fünfzig
    /// Bildern keine einzige Übertragung: Das Laden muss ohnehin JEDE Datei öffnen, um an
    /// das Aufnahmedatum zu kommen, also wird bei einem vollständigen Lauf am Ende alles
    /// heruntergeladen. Die Grenze verschob den Download nur nach hinten und machte dabei
    /// das Laden von der Geschwindigkeit der KI abhängig — genau das, was die getrennten
    /// Balken sichtbar machen sollten.
    ///
    /// Der Preis: Wird ein Lauf früh abgebrochen, wurden bereits Dateien geladen, die
    /// niemand mehr braucht. Wer über eine langsame oder teure Leitung arbeitet, setzt
    /// hier wieder eine Grenze.
    /// </summary>
    [Range(0, 100_000)]
    public int PrefetchBuffer { get; set; }
}
