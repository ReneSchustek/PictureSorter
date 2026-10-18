namespace PictureSorter.Core.ValueObjects;

/// <summary>
/// Ein abgeschlossener Umbenennungs-Lauf: welche Datei wann von welchem Namen auf
/// welchen kam. Erst dieses Protokoll macht den Lauf umkehrbar, denn der alte Name ist
/// nach dem Umbenennen nirgends mehr zu finden, und gerade er war ja das, was
/// verloren zu gehen drohte.
///
/// Bewusst getrennt vom Protokoll der Sortierläufe: Jenes trägt Kategorie und
/// Signatur mit sich, weil sein Rückgängigmachen auch das Sortier-Gedächtnis
/// aufräumen muss. Hier gibt es beides nicht, und ein gemeinsamer Vertrag hätte für
/// beide Seiten die falschen Zusagen gemacht.
/// </summary>
public sealed record RenameRun
{
    /// <summary>Kennung des Laufs.</summary>
    public required Guid Id { get; init; }

    /// <summary>Zeitpunkt des Laufs (UTC).</summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>Der Ordner, in dem umbenannt wurde.</summary>
    public required string Folder { get; init; }

    /// <summary>Die umbenannten Dateien.</summary>
    public required IReadOnlyList<RenameRunItem> Items { get; init; }
}

/// <summary>
/// Eine einzelne umbenannte Datei. Sie hält beide Pfade fest: den alten, um ihn
/// zurückgeben zu können, und den neuen, um die Datei überhaupt wiederzufinden.
/// </summary>
public sealed record RenameRunItem
{
    /// <summary>Vollständiger Pfad vor dem Umbenennen.</summary>
    public required string OriginalPath { get; init; }

    /// <summary>Vollständiger Pfad nach dem Umbenennen.</summary>
    public required string NewPath { get; init; }
}
