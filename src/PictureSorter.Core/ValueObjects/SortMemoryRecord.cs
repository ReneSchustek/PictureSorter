using PictureSorter.Core.Enums;

namespace PictureSorter.Core.ValueObjects;

/// <summary>
/// Ein Eintrag des dauerhaften Sortier-Gedächtnisses: Was wurde für ein bestimmtes
/// Foto in einem bestimmten Ordner bereits entschieden? Damit überspringt ein
/// zweiter Lauf erledigte Fotos, statt sie erneut teuer von der KI bewerten zu lassen.
/// </summary>
public sealed record SortMemoryRecord
{
    /// <summary>
    /// Quellordner, für den die Entscheidung gilt.
    /// </summary>
    public required string FolderPath { get; init; }

    /// <summary>
    /// Stabile Signatur der Datei (Inhalt bzw. Pfad, Größe und Änderungszeit).
    /// Ändert sich die Datei, entsteht eine neue Signatur und das Foto wird erneut
    /// bewertet.
    /// </summary>
    public required string FileSignature { get; init; }

    /// <summary>
    /// Pfad des Fotos zum Zeitpunkt der Entscheidung (für die Anzeige).
    /// </summary>
    public required string PhotoPath { get; init; }

    /// <summary>
    /// Name der Kategorie, der das Foto zugeordnet wurde.
    /// </summary>
    public required string CategoryName { get; init; }

    /// <summary>
    /// Stand der Entscheidung (vorgeschlagen, einsortiert oder bewusst ignoriert).
    /// </summary>
    public required SortMemoryStatus Status { get; init; }

    /// <summary>
    /// Konfidenz der Zuordnung im Bereich 0.0 bis 1.0.
    /// </summary>
    public required double Confidence { get; init; }

    /// <summary>
    /// Zeitpunkt der letzten Änderung des Eintrags (UTC).
    /// </summary>
    public required DateTimeOffset UpdatedAt { get; init; }
}
