namespace PictureSorter.Core.Enums;

/// <summary>
/// Art einer Kategorie. Bestimmt, wie der Zielordnername gebildet wird.
/// </summary>
public enum CategoryKind
{
    /// <summary>
    /// Themen-Kategorie mit festem Ordnernamen (z. B. „Familie").
    /// </summary>
    Topic,

    /// <summary>
    /// Ereignis-Kategorie, deren Ordnername zusätzlich das Aufnahmedatum
    /// enthält (z. B. „Urlaub 14.07.25").
    /// </summary>
    Event,
}
