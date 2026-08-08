namespace PictureSorter.Data.Entities;

/// <summary>
/// Datenbank-Abbild eines Analyselaufs.
/// </summary>
internal sealed class AnalysisRunEntity
{
    /// <summary>Technischer Primärschlüssel.</summary>
    public int Id { get; set; }

    /// <summary>Fachliche Kennung des Laufs.</summary>
    public Guid RunId { get; set; }

    /// <summary>Der durchsuchte Ordner.</summary>
    public required string SourceFolder { get; set; }

    /// <summary>Kategorie beziehungsweise Zielordnername.</summary>
    public required string CategoryName { get; set; }

    /// <summary><see langword="true"/>, wenn allein nach Aufnahmedatum sortiert wurde.</summary>
    public bool ByDateOnly { get; set; }

    /// <summary><see langword="true"/>, wenn Unterordner einbezogen wurden.</summary>
    public bool IncludeSubfolders { get; set; }

    /// <summary>
    /// Erster Tag des Zeitraums als Zahl im Format JJJJMMTT, oder <see langword="null"/>.
    /// Bewusst kein Datumstyp: SQLite legt Datumswerte als Text ab, und ein Vergleich
    /// darauf ist von der Schreibweise abhängig. Eine Zahl ist eindeutig.
    /// </summary>
    public int? RangeFrom { get; set; }

    /// <summary>Letzter Tag des Zeitraums im Format JJJJMMTT, oder <see langword="null"/>.</summary>
    public int? RangeTo { get; set; }

    /// <summary>Zustand des Laufs (siehe <see cref="Core.Enums.AnalysisRunState"/>).</summary>
    public int State { get; set; }

    /// <summary>
    /// Beginn des Laufs in UTC. Wie beim Sortierlauf bewusst <see cref="DateTime"/>:
    /// SQLite kann <see cref="DateTimeOffset"/> nicht in ORDER BY verwenden — und genau
    /// danach wird gesucht, um den jüngsten Lauf zu finden.
    /// </summary>
    public DateTime StartedAtUtc { get; set; }

    /// <summary>Zeitpunkt der letzten Bewegung in UTC (der Herzschlag des Laufs).</summary>
    public DateTime LastProgressAtUtc { get; set; }

    /// <summary>Ende des Laufs in UTC, oder <see langword="null"/>, solange er läuft.</summary>
    public DateTime? FinishedAtUtc { get; set; }

    /// <summary>Zahl der gefundenen Bilddateien.</summary>
    public int TotalPhotos { get; set; }

    /// <summary>Grund des Scheiterns, oder <see langword="null"/>.</summary>
    public string? FailureReason { get; set; }

    /// <summary>Die protokollierten Ergebnisse des Laufs.</summary>
    public List<AnalysisRunItemEntity> Items { get; } = [];
}
