using PictureSorter.Core.Entities;
using PictureSorter.Core.Enums;

namespace PictureSorter.Core.ValueObjects;

/// <summary>
/// Vorschlag, ein bestimmtes Foto in den Zielordner einer Kategorie zu verschieben.
/// Vorschläge werden vor dem tatsächlichen Verschieben als Vorschau angezeigt.
/// </summary>
public sealed record SortProposal
{
    /// <summary>
    /// Das betroffene Foto.
    /// </summary>
    public required Photo Photo { get; init; }

    /// <summary>
    /// Name der vorgeschlagenen Kategorie.
    /// </summary>
    public required string CategoryName { get; init; }

    /// <summary>
    /// Vollständiger Zielordnerpfad (inkl. evtl. Datum bei Ereignis-Kategorien).
    /// </summary>
    public required string TargetFolderPath { get; init; }

    /// <summary>
    /// Konfidenz der Zuordnung im Bereich 0.0 bis 1.0.
    /// </summary>
    public required double Confidence { get; init; }

    /// <summary>
    /// Auf welchem Weg die Zuordnung zustande kam.
    /// </summary>
    public required ClassificationMethod Method { get; init; }
}
