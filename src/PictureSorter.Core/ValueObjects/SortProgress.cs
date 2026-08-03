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
public readonly record struct SortProgress(int Processed, int Total, ScanPhase Phase = ScanPhase.Analyzing);
