namespace PictureSorter.App.ViewModels;

/// <summary>
/// Expliziter Zustand der Sortier-Ansicht. Single Source of Truth für das
/// Verhalten der Oberfläche (keine Modellierung über mehrere Bool-Flags).
/// </summary>
internal enum SortState
{
    /// <summary>
    /// Bereit; noch keine Analyse gestartet.
    /// </summary>
    Idle,

    /// <summary>
    /// Beispielfotos werden eingelesen und das Kategorie-Profil angelernt.
    /// </summary>
    Learning,

    /// <summary>
    /// Fotos werden analysiert und Vorschläge erzeugt.
    /// </summary>
    Analyzing,

    /// <summary>
    /// Vorschläge liegen als Vorschau vor und können bestätigt werden.
    /// </summary>
    Preview,

    /// <summary>
    /// Bestätigte Vorschläge werden angewendet (Dateien verschoben).
    /// </summary>
    Sorting,

    /// <summary>
    /// Ein Sortierlauf wird zurückgenommen (Dateien werden zurückgeholt).
    /// </summary>
    Undoing,

    /// <summary>
    /// Vorgang erfolgreich abgeschlossen.
    /// </summary>
    Completed,

    /// <summary>
    /// Ein Fehler ist aufgetreten.
    /// </summary>
    Error,
}
