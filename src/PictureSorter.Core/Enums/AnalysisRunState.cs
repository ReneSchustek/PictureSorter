namespace PictureSorter.Core.Enums;

/// <summary>
/// Zustand eines protokollierten Analyselaufs.
///
/// Der Zustand ist die Grundlage des Fortsetzens: Nur ein Lauf, der nicht ordentlich zu
/// Ende gekommen ist, hat offene Fotos. Ohne ihn wäre ein stehengebliebener Lauf von
/// einem abgeschlossenen nicht zu unterscheiden.
/// </summary>
/// <remarks>
/// Die Werte sind ausdrücklich vergeben, weil dieser Zustand als Zahl in der Datenbank
/// steht: Käme später ein Zustand dazwischen, deutete er alle gespeicherten Läufe um —
/// aus „angehalten" würde „fehlgeschlagen", und niemand bekäme es mit.
/// </remarks>
public enum AnalysisRunState
{
    /// <summary>
    /// Der Lauf ist gestartet und nicht beendet worden. Bleibt dieser Zustand nach dem
    /// Beenden der Anwendung stehen, ist der Lauf abgestürzt oder wurde abgewürgt.
    /// </summary>
    Running = 0,

    /// <summary>Der Lauf ist vollständig durchgelaufen.</summary>
    Completed = 1,

    /// <summary>
    /// Die Nutzerin hat den Lauf angehalten.
    ///
    /// Bewusst „angehalten" und nicht „abgebrochen": Seit jedes Ergebnis im Protokoll
    /// steht, geht beim Anhalten nichts verloren, und der Lauf setzt später genau dort
    /// wieder an. Wer einen Ordner mit tausenden Bildern sortiert, muss den Rechner nicht
    /// tagelang durchlaufen lassen.
    /// </summary>
    Paused = 2,

    /// <summary>Der Lauf ist mit einem Fehler geendet; der Grund steht im Lauf-Kopf.</summary>
    Failed = 3,
}
