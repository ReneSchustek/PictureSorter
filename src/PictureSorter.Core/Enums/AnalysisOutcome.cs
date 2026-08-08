namespace PictureSorter.Core.Enums;

/// <summary>
/// Was bei der Bewertung eines einzelnen Fotos herauskam.
///
/// Die Unterscheidung ist keine Buchhaltung, sondern entscheidet über das Fortsetzen: Ein
/// Foto, über das ein Urteil vorliegt, darf beim zweiten Anlauf nicht erneut der KI
/// vorgelegt werden. Ein Foto, das nur wegen eines Ausfalls unbeurteilt blieb, dagegen
/// schon — sonst würde ein einmaliger Aussetzer dauerhaft festgeschrieben.
/// </summary>
/// <remarks>
/// Die Werte sind ausdrücklich vergeben, weil dieses Ergebnis als Zahl in der Datenbank
/// steht: Käme später eines dazwischen, deutete es alle protokollierten Urteile um.
/// </remarks>
public enum AnalysisOutcome
{
    /// <summary>Die KI ordnet das Foto der Kategorie zu; es liegt ein Vorschlag vor.</summary>
    Proposed = 0,

    /// <summary>Die KI ordnet das Foto der Kategorie nicht zu.</summary>
    Rejected = 1,

    /// <summary>Das Foto liegt außerhalb des gewählten Zeitraums und wurde nicht bewertet.</summary>
    OutsideRange = 2,

    /// <summary>Über das Foto lag bereits eine Entscheidung im Sortier-Gedächtnis vor.</summary>
    SkippedByMemory = 3,

    /// <summary>
    /// Es kam kein Urteil zustande — die KI war nicht erreichbar oder die Datei nicht
    /// lesbar. Beim Fortsetzen wird das Foto deshalb erneut versucht.
    /// </summary>
    NotEvaluated = 4,
}
