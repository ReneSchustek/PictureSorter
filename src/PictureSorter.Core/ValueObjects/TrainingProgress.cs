namespace PictureSorter.Core.ValueObjects;

/// <summary>
/// Fortschritt beim Anlernen einer Kategorie: wie viele Beispiele von insgesamt wie
/// vielen bereits verarbeitet wurden. Jedes Beispiel kostet einen vollständigen Aufruf
/// des Bild-Modells, weshalb der Vorgang auch bei wenigen Bildern spürbar dauert.
/// </summary>
/// <param name="Processed">Anzahl der bereits verarbeiteten Beispiele.</param>
/// <param name="Total">Gesamtzahl der Beispiele.</param>
public readonly record struct TrainingProgress(int Processed, int Total);
