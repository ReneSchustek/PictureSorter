using PictureSorter.Core.Enums;

namespace PictureSorter.Core.ValueObjects;

/// <summary>
/// Ein abgeschlossener Sortierlauf: welche Dateien wann von wo nach wo verschoben
/// wurden. Erst dieses Protokoll macht den Lauf umkehrbar – ein Verschieben ist
/// sonst endgültig, und die Zielpfade sind nach dem Lauf nirgends mehr bekannt.
/// </summary>
public sealed record SortRun
{
    /// <summary>Kennung des Laufs.</summary>
    public required Guid Id { get; init; }

    /// <summary>Zeitpunkt des Laufs (UTC).</summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>Der Quellordner, aus dem sortiert wurde.</summary>
    public required string SourceFolder { get; init; }

    /// <summary>Die Kategorie, nach der sortiert wurde.</summary>
    public required string CategoryName { get; init; }

    /// <summary>
    /// Ob der Lauf verschoben oder kopiert hat. Voreinstellung ist
    /// <see cref="FileOperationMode.Move"/> – so tragen auch die Läufe, die vor
    /// dieser Wahlmöglichkeit protokolliert wurden, den zutreffenden Wert.
    /// </summary>
    public FileOperationMode Operation { get; init; } = FileOperationMode.Move;

    /// <summary>Die verschobenen Dateien.</summary>
    public required IReadOnlyList<SortRunItem> Items { get; init; }
}
