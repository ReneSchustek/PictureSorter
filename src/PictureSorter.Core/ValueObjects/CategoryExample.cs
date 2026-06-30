namespace PictureSorter.Core.ValueObjects;

/// <summary>
/// Ein vom Nutzer bestätigtes Beispiel für eine Kategorie. Aus positiven und
/// negativen Beispielen entsteht das lernbare Profil der Kategorie.
/// </summary>
public sealed record CategoryExample
{
    /// <summary>
    /// Pfad des Beispielbildes zum Zeitpunkt der Bestätigung.
    /// </summary>
    public required string PhotoPath { get; init; }

    /// <summary>
    /// Embedding des Beispielbildes.
    /// </summary>
    public required ImageEmbedding Embedding { get; init; }

    /// <summary>
    /// <see langword="true"/>, wenn das Bild zur Kategorie gehört, sonst
    /// <see langword="false"/> (Gegenbeispiel).
    /// </summary>
    public required bool IsPositive { get; init; }
}
