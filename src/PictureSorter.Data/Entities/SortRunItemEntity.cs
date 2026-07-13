namespace PictureSorter.Data.Entities;

/// <summary>
/// Datenbank-Abbild einer einzelnen Verschiebung innerhalb eines Sortierlaufs.
/// </summary>
internal sealed class SortRunItemEntity
{
    /// <summary>Technischer Primärschlüssel.</summary>
    public int Id { get; set; }

    /// <summary>Fremdschlüssel auf den Lauf.</summary>
    public int SortRunId { get; set; }

    /// <summary>Der Lauf, zu dem die Verschiebung gehört.</summary>
    public SortRunEntity? SortRun { get; set; }

    /// <summary>Pfad, an dem das Foto vor dem Sortieren lag.</summary>
    public required string SourcePath { get; set; }

    /// <summary>Pfad, an dem das Foto nach dem Sortieren liegt.</summary>
    public required string TargetPath { get; set; }

    /// <summary>Signatur des Fotos – der Schlüssel seines Eintrags im Gedächtnis.</summary>
    public required string FileSignature { get; set; }
}
