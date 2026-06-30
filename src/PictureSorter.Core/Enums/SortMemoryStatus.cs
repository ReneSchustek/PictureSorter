namespace PictureSorter.Core.Enums;

/// <summary>
/// Status eines gemerkten Sortier-Eintrags im dauerhaften Gedächtnis.
/// </summary>
public enum SortMemoryStatus
{
    /// <summary>Vorgeschlagen, aber noch nicht angewendet.</summary>
    Proposed,

    /// <summary>In den Zielordner einsortiert (erledigt).</summary>
    Sorted,

    /// <summary>Bewusst ignoriert; nicht erneut vorschlagen.</summary>
    Ignored,
}
