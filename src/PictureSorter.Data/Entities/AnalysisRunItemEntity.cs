namespace PictureSorter.Data.Entities;

/// <summary>
/// Datenbank-Abbild des Ergebnisses zu einem einzelnen Foto.
/// </summary>
internal sealed class AnalysisRunItemEntity
{
    /// <summary>Technischer Primärschlüssel.</summary>
    public int Id { get; set; }

    /// <summary>Fremdschlüssel auf den Lauf.</summary>
    public int AnalysisRunId { get; set; }

    /// <summary>Der Lauf, zu dem das Ergebnis gehört.</summary>
    public AnalysisRunEntity? AnalysisRun { get; set; }

    /// <summary>Signatur des Fotos — der Schlüssel des Wiedererkennens.</summary>
    public required string FileSignature { get; set; }

    /// <summary>Pfad des Fotos zum Zeitpunkt der Bewertung.</summary>
    public required string PhotoPath { get; set; }

    /// <summary>Das Ergebnis (siehe <see cref="Core.Enums.AnalysisOutcome"/>).</summary>
    public int Outcome { get; set; }

    /// <summary>Konfidenz der Zuordnung.</summary>
    public double Confidence { get; set; }

    /// <summary>Verfahren der Zuordnung (siehe <see cref="Core.Enums.ClassificationMethod"/>).</summary>
    public int Method { get; set; }

    /// <summary>Zeitpunkt der Bewertung in UTC.</summary>
    public DateTime DecidedAtUtc { get; set; }
}
