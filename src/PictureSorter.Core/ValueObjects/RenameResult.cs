namespace PictureSorter.Core.ValueObjects;

/// <summary>
/// Ergebnis eines Umbenennungs-Laufs. Übersprungene Dateien werden ausgewiesen statt
/// verschwiegen: Eine geöffnete Datei oder ein inzwischen vergebener Name sind im
/// Alltag normal, und die Nutzerin muss erfahren, dass nicht alles durchging.
/// </summary>
public sealed record RenameResult
{
    /// <summary>Zahl der umbenannten Dateien.</summary>
    public required int Renamed { get; init; }

    /// <summary>Zahl der Dateien, die ihren Namen behalten haben.</summary>
    public required int Skipped { get; init; }
}
