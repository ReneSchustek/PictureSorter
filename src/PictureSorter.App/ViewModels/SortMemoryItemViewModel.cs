using System.Globalization;
using PictureSorter.Core.Enums;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.App.ViewModels;

/// <summary>
/// Ein Eintrag des Sortier-Gedächtnisses in der Verwaltungsansicht. Übersetzt die
/// technischen Werte des Datensatzes in Anzeigetexte.
/// </summary>
internal sealed class SortMemoryItemViewModel
{
    /// <summary>
    /// Erzeugt das Anzeige-Modell aus einem Gedächtnis-Eintrag.
    /// </summary>
    /// <param name="record">Der zugrunde liegende Eintrag.</param>
    public SortMemoryItemViewModel(SortMemoryRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        Record = record;
    }

    /// <summary>
    /// Der zugrunde liegende Eintrag (für Lösch-Aufrufe).
    /// </summary>
    public SortMemoryRecord Record { get; }

    /// <summary>Vollständiger Pfad des Fotos (Quelle der Vorschau).</summary>
    public string FilePath => Record.PhotoPath;

    /// <summary>Reiner Dateiname.</summary>
    public string FileName => Path.GetFileName(Record.PhotoPath);

    /// <summary>Die Kategorie, für die entschieden wurde.</summary>
    public string CategoryName => Record.CategoryName;

    /// <summary>Der Quellordner der Entscheidung.</summary>
    public string FolderPath => Record.FolderPath;

    /// <summary>Der Status in verständlichem Deutsch.</summary>
    public string StatusText => Record.Status switch
    {
        SortMemoryStatus.Sorted => "Einsortiert",
        SortMemoryStatus.Ignored => "Abgewählt",
        SortMemoryStatus.Rejected => "Passt nicht",
        _ => "Vorgeschlagen",
    };

    /// <summary>Zeitpunkt der letzten Änderung in lokaler Schreibweise.</summary>
    public string UpdatedText => Record.UpdatedAt
        .ToLocalTime()
        .ToString("dd.MM.yyyy HH:mm", CultureInfo.GetCultureInfo("de-DE"));

    /// <summary>Konfidenz als Prozentwert.</summary>
    public string ConfidenceText => Record.Confidence.ToString("P0", CultureInfo.GetCultureInfo("de-DE"));
}
