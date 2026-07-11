namespace PictureSorter.Data.Entities;

/// <summary>
/// Datenbank-Abbild eines Gedächtnis-Eintrags. Bewusst getrennt vom Core-Record
/// <see cref="Core.ValueObjects.SortMemoryRecord"/>, damit Persistenz-Details
/// (Schlüssel, Speichertypen) nicht in die Domäne durchschlagen.
/// </summary>
internal sealed class SortMemoryEntity
{
    /// <summary>Technischer Primärschlüssel.</summary>
    public int Id { get; set; }

    /// <summary>Quellordner, für den die Entscheidung gilt.</summary>
    public required string FolderPath { get; set; }

    /// <summary>Stabile Signatur der Datei (Pfad, Größe, Änderungszeit).</summary>
    public required string FileSignature { get; set; }

    /// <summary>Pfad des Fotos zum Zeitpunkt der Entscheidung.</summary>
    public required string PhotoPath { get; set; }

    /// <summary>Name der zugeordneten Kategorie.</summary>
    public required string CategoryName { get; set; }

    /// <summary>Stand der Entscheidung, gespeichert als Ganzzahl.</summary>
    public int Status { get; set; }

    /// <summary>Konfidenz der Zuordnung (0.0 bis 1.0).</summary>
    public double Confidence { get; set; }

    /// <summary>
    /// Zeitpunkt der letzten Änderung in UTC. Bewusst <see cref="DateTime"/> und
    /// nicht <see cref="DateTimeOffset"/>: SQLite kann DateTimeOffset nicht in
    /// ORDER BY verwenden. Der Offset ist hier ohnehin immer UTC.
    /// </summary>
    public DateTime UpdatedAtUtc { get; set; }
}
