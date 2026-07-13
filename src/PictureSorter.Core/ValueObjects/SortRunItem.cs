namespace PictureSorter.Core.ValueObjects;

/// <summary>
/// Eine einzelne verschobene Datei eines Sortierlaufs. Sie hält beide Pfade fest:
/// wo das Foto vorher lag und wo es tatsächlich gelandet ist. Der Zielpfad ist nicht
/// vorhersagbar – bei einem Namenskonflikt hängt das Verschieben eine Nummer an –,
/// und ohne ihn ließe sich der Schritt nicht zurücknehmen.
/// </summary>
public sealed record SortRunItem
{
    /// <summary>Vollständiger Pfad, an dem das Foto vor dem Sortieren lag.</summary>
    public required string SourcePath { get; init; }

    /// <summary>Vollständiger Pfad, an dem das Foto nach dem Sortieren liegt.</summary>
    public required string TargetPath { get; init; }

    /// <summary>
    /// Signatur des Fotos zum Zeitpunkt des Sortierens. Sie ist der Schlüssel des
    /// Eintrags im Sortier-Gedächtnis, der beim Rückgängigmachen wieder verschwinden
    /// muss – sonst gälte das Foto weiterhin als einsortiert, obwohl es zurück im
    /// Quellordner liegt, und würde nie wieder vorgeschlagen.
    /// </summary>
    public required string FileSignature { get; init; }
}
