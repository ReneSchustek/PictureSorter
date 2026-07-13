namespace PictureSorter.Data.Entities;

/// <summary>
/// Datenbank-Abbild eines Sortierlaufs. Der Lauf bleibt auch nach dem
/// Rückgängigmachen stehen (<see cref="IsUndone"/>), damit ein zurückgenommener Lauf
/// nicht erneut angeboten wird.
/// </summary>
internal sealed class SortRunEntity
{
    /// <summary>Technischer Primärschlüssel.</summary>
    public int Id { get; set; }

    /// <summary>Fachliche Kennung des Laufs.</summary>
    public Guid RunId { get; set; }

    /// <summary>
    /// Zeitpunkt des Laufs in UTC. Wie beim Gedächtnis bewusst <see cref="DateTime"/>:
    /// SQLite kann <see cref="DateTimeOffset"/> nicht in ORDER BY verwenden – und
    /// genau danach wird hier sortiert, um den jüngsten Lauf zu finden.
    /// </summary>
    public DateTime StartedAtUtc { get; set; }

    /// <summary>Quellordner, aus dem sortiert wurde.</summary>
    public required string SourceFolder { get; set; }

    /// <summary>Kategorie, nach der sortiert wurde.</summary>
    public required string CategoryName { get; set; }

    /// <summary><see langword="true"/>, wenn der Lauf bereits zurückgenommen wurde.</summary>
    public bool IsUndone { get; set; }

    /// <summary>Die verschobenen Dateien des Laufs.</summary>
    public List<SortRunItemEntity> Items { get; } = [];
}
