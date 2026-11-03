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
    /// Das Einlesen wartet fast nur auf die Festplatte, mit abgeschaltetem
    /// <see cref="SkipOnlineOnlyFiles"/> auch auf Downloads aus der Cloud. Wartezeit lässt sich
    /// übereinanderlegen, ohne dass es etwas kostet.
    ///
    /// Acht ist bewusst kein hoher Wert: Eine mechanische Festplatte wird durch zu viele
    /// gleichzeitige Zugriffe langsamer statt schneller, weil der Kopf zwischen den
    /// Dateien hin- und herspringt.
    /// </summary>
    [Range(1, 64)]
    public int MaxParallelReads { get; set; } = 8;

    /// <summary>
    /// Wie viele fertig eingelesene Bilder höchstens auf Vorrat bereitliegen, bevor das
    /// Laden wartet. <c>0</c> bedeutet: ohne Grenze; das Laden wartet nie auf die
    /// Bewertung.
    ///
    /// Voreingestellt ist die Entkopplung (<c>0</c>): Das Laden hängt nicht an der Geschwindigkeit
    /// der KI, und genau das sollen die getrennten Balken zeigen. Der Vorrat kostet kaum Speicher:
    /// Gepuffert wird nur ein <c>Photo</c> mit seinen Metadaten, nie der Bildinhalt. Selbst
    /// zehntausend Einträge bleiben im zweistelligen Megabyte-Bereich. Eine Grenze spart auch keine
    /// Arbeit, denn das Laden öffnet ohnehin jede Datei, um an das Aufnahmedatum zu kommen.
    ///
    /// Der Preis: Wird ein Lauf früh abgebrochen, wurden bereits Dateien gelesen, die niemand mehr
    /// braucht.
    /// </summary>
    [Range(0, 100_000)]
    public int PrefetchBuffer { get; set; }

    /// <summary>
    /// Wie viele Sekunden ein Versuch, eine einzelne Datei einzulesen, höchstens dauern darf. Danach gibt es
    /// einen zweiten Versuch, dann wird die Datei übersprungen.
    ///
    /// Auf einem echten Bestand aus iCloud kam das Lesen je Lauf bei ein bis zwei Dateien nie zurück, bei
    /// jedem Lauf bei anderen. Der ganze Lauf endete damit nie, ohne jede Meldung. Großzügig bemessen, weil
    /// ein Cloud-Ordner die Datei dabei oft erst herunterlädt.
    /// </summary>
    [Range(1, 3600)]
    public int ReadTimeoutSeconds { get; set; } = 120;

    /// <summary>
    /// Lässt Dateien aus, die nur online in der Cloud liegen, statt sie zu öffnen.
    ///
    /// An einem echten iCloud-Bestand lieferte das Öffnen nichts und blockierte je Datei zweimal die volle
    /// Zeitgrenze. Solange solche Dateien gelesen wurden, blieb außerdem der Aufräum-Thread von .NET stehen,
    /// und das Programm wuchs bis zum Absturz. Wer sie einbeziehen will, lädt sie vorher auf den PC.
    /// </summary>
    public bool SkipOnlineOnlyFiles { get; set; } = true;
}
