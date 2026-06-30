namespace PictureSorter.Core.ValueObjects;

/// <summary>
/// Fortschritt der Foto-Analyse: wie viele Fotos von insgesamt wie vielen bereits
/// bewertet wurden. Dient der Prozentanzeige in der Oberfläche.
/// </summary>
/// <param name="Processed">Anzahl der bereits bewerteten Fotos.</param>
/// <param name="Total">Gesamtzahl der zu bewertenden Fotos.</param>
public readonly record struct SortProgress(int Processed, int Total);
