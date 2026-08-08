using PictureSorter.Core.Enums;

namespace PictureSorter.Core.ValueObjects;

/// <summary>
/// Fortschritt der Foto-Analyse: wie viele Fotos von insgesamt wie vielen bereits
/// bearbeitet wurden. Dient der Prozentanzeige in der Oberfläche.
/// </summary>
/// <param name="Processed">Anzahl der bereits bearbeiteten Fotos.</param>
/// <param name="Total">Gesamtzahl der zu bearbeitenden Fotos.</param>
/// <param name="Phase">
/// Der Abschnitt, auf den sich die Zahlen beziehen. Voreingestellt ist das Bewerten:
/// So bleiben die vorhandenen Meldungen des Sortierlaufs unverändert gültig.
/// </param>
/// <param name="IsFinal">
/// <see langword="true"/> für die Abschlussmeldung eines Abschnitts.
///
/// Sie muss ausdrücklich als solche gemeldet werden und darf nicht daraus erschlossen
/// werden, dass der Zählstand die Gesamtzahl erreicht: Fällt beim Einlesen auch nur eine
/// Datei aus – Virenscanner, Cloud-Ordner, fehlender Codec –, kommen weniger Fotos zur
/// Bewertung als der Ordner Dateien hat. Der Zählstand erreicht die Gesamtzahl dann nie,
/// die Anzeige bliebe kurz vor dem Ende stehen, und von einem Stillstand wäre das nicht zu
/// unterscheiden.
/// </param>
public readonly record struct SortProgress(
    int Processed,
    int Total,
    ScanPhase Phase = ScanPhase.Analyzing,
    bool IsFinal = false);
