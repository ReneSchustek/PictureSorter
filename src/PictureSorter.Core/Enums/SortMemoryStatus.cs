namespace PictureSorter.Core.Enums;

/// <summary>
/// Status eines gemerkten Sortier-Eintrags im dauerhaften Gedächtnis. Der Status
/// entscheidet, ob ein Foto in einem späteren Lauf erneut von der KI bewertet
/// werden muss.
/// </summary>
public enum SortMemoryStatus
{
    /// <summary>
    /// Vorgeschlagen, aber noch nicht angewendet. Wird erneut vorgeschlagen.
    /// </summary>
    Proposed,

    /// <summary>
    /// In den Zielordner einsortiert (erledigt). Wird übersprungen.
    /// </summary>
    Sorted,

    /// <summary>
    /// Vom Nutzer bewusst abgewählt. Wird nicht erneut vorgeschlagen.
    /// </summary>
    Ignored,

    /// <summary>
    /// Die KI hat das Foto der Kategorie nicht zugeordnet. Das Urteil wird gemerkt,
    /// damit der teure Vision-Aufruf sich beim nächsten Lauf nicht wiederholt.
    /// </summary>
    Rejected,
}
