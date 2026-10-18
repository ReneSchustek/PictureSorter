namespace PictureSorter.Data.Entities;

/// <summary>
/// Datenbank-Abbild eines Umbenennungs-Laufs. Der Lauf bleibt auch nach dem
/// Zurücknehmen stehen (<see cref="IsUndone"/>), damit ein zurückgenommener Lauf
/// nicht erneut angeboten wird.
/// </summary>
internal sealed class RenameRunEntity
{
    /// <summary>Technischer Primärschlüssel.</summary>
    public int Id { get; set; }

    /// <summary>Fachliche Kennung des Laufs.</summary>
    public Guid RunId { get; set; }

    /// <summary>
    /// Zeitpunkt des Laufs in UTC. Wie beim Sortierlauf bewusst <see cref="DateTime"/>:
    /// SQLite kann <see cref="DateTimeOffset"/> nicht in ORDER BY verwenden, und genau
    /// danach wird hier sortiert, um den jüngsten Lauf zu finden.
    /// </summary>
    public DateTime StartedAtUtc { get; set; }

    /// <summary>Ordner, in dem umbenannt wurde.</summary>
    public required string Folder { get; set; }

    /// <summary><see langword="true"/>, wenn der Lauf bereits zurückgenommen wurde.</summary>
    public bool IsUndone { get; set; }

    /// <summary>Die umbenannten Dateien des Laufs.</summary>
    public List<RenameRunItemEntity> Items { get; } = [];
}
