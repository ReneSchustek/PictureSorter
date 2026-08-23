namespace PictureSorter.Data.Entities;

/// <summary>
/// Datenbank-Abbild einer einzelnen Umbenennung innerhalb eines Laufs. Der alte Pfad
/// steht hier als einziger Ort: Nach dem Umbenennen ist der Name sonst nirgends mehr
/// zu finden.
/// </summary>
internal sealed class RenameRunItemEntity
{
    /// <summary>Technischer Primärschlüssel.</summary>
    public int Id { get; set; }

    /// <summary>Fremdschlüssel auf den Lauf.</summary>
    public int RenameRunId { get; set; }

    /// <summary>Der Lauf, zu dem die Umbenennung gehört.</summary>
    public RenameRunEntity? RenameRun { get; set; }

    /// <summary>Pfad vor dem Umbenennen.</summary>
    public required string OriginalPath { get; set; }

    /// <summary>Pfad nach dem Umbenennen.</summary>
    public required string NewPath { get; set; }
}
