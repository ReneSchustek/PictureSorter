using PictureSorter.Core.Enums;

namespace PictureSorter.Core.ValueObjects;

/// <summary>
/// Das protokollierte Ergebnis zu einem einzelnen Foto.
///
/// Der Schlüssel ist die Signatur, nicht der Pfad: Wird eine Datei umbenannt oder
/// verändert, ändert sich ihre Signatur, und sie wird beim Fortsetzen zu Recht neu
/// bewertet. Der Pfad steht trotzdem dabei — ein Protokoll, das man nicht lesen kann,
/// ist keines.
/// </summary>
public sealed record AnalysisRunItem
{
    /// <summary>Signatur des Fotos (Pfad, Größe, Aufnahmezeit).</summary>
    public required string FileSignature { get; init; }

    /// <summary>Pfad des Fotos zum Zeitpunkt der Bewertung.</summary>
    public required string PhotoPath { get; init; }

    /// <summary>Was bei der Bewertung herauskam.</summary>
    public required AnalysisOutcome Outcome { get; init; }

    /// <summary>Konfidenz der Zuordnung (0,0 bis 1,0); 0 ohne Zuordnung.</summary>
    public required double Confidence { get; init; }

    /// <summary>Auf welchem Weg die Zuordnung zustande kam.</summary>
    public required ClassificationMethod Method { get; init; }

    /// <summary>Zeitpunkt der Bewertung (UTC).</summary>
    public required DateTimeOffset DecidedAt { get; init; }
}
