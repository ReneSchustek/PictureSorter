namespace PictureSorter.Core.ValueObjects;

/// <summary>
/// Ein Vorschlag, wie eine Datei mit unlesbarem Namen künftig heißen soll. Er wird
/// erst angezeigt und dann angewendet, nie umgekehrt: Ein Name ist das Einzige, was
/// die Nutzerin von einem Foto im Explorer sieht, und ihn ungefragt zu ändern nähme
/// ihr die Orientierung.
/// </summary>
public sealed record RenameProposal
{
    /// <summary>Vollständiger Pfad der Datei, so wie sie heute heißt.</summary>
    public required string FilePath { get; init; }

    /// <summary>Der bisherige Dateiname, samt seiner unlesbaren Zeichen.</summary>
    public required string CurrentName { get; init; }

    /// <summary>Der vorgeschlagene Dateiname samt Endung.</summary>
    public required string SuggestedName { get; init; }

    /// <summary>Woher der vorgeschlagene Name stammt.</summary>
    public required RenameSource Source { get; init; }
}

/// <summary>
/// Woher der vorgeschlagene Name kommt. Die Unterscheidung gehört in die Oberfläche:
/// Ein Name aus dem Aufnahmedatum sagt etwas über das Bild, eine laufende Nummer sagt
/// nur, dass nichts Besseres zu holen war.
/// </summary>
public enum RenameSource
{
    /// <summary>Aus dem Aufnahmedatum im Dateikopf.</summary>
    CaptureDate,

    /// <summary>Aus dem Ordnernamen und einer laufenden Nummer.</summary>
    FolderAndNumber,
}
