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

    /// <summary>
    /// Größe der Zieldatei unmittelbar nach dem Sortieren. Zusammen mit
    /// <see cref="TargetLastWriteUtc"/> der Beleg dafür, dass eine Kopie noch
    /// unverändert ist und beim Rückgängigmachen entfernt werden darf.
    /// <see langword="null"/> bei Läufen aus der Zeit vor dieser Prüfung – dann wird
    /// im Zweifel nichts gelöscht.
    /// </summary>
    public long? TargetLength { get; init; }

    /// <summary>
    /// Änderungszeitpunkt der Zieldatei unmittelbar nach dem Sortieren (UTC).
    /// Siehe <see cref="TargetLength"/>.
    /// </summary>
    public DateTime? TargetLastWriteUtc { get; init; }
}
