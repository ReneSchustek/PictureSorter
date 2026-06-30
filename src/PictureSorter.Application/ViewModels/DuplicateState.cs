namespace PictureSorter.Application.ViewModels;

/// <summary>
/// Expliziter Zustand der Duplikate-Ansicht. Single Source of Truth für das
/// Verhalten der Oberfläche.
/// </summary>
public enum DuplicateState
{
    /// <summary>
    /// Bereit; noch keine Suche gestartet.
    /// </summary>
    Idle,

    /// <summary>
    /// Der Ordner wird nach Duplikaten durchsucht.
    /// </summary>
    Scanning,

    /// <summary>
    /// Gefundene Duplikate liegen zur Durchsicht und Auswahl vor.
    /// </summary>
    Review,

    /// <summary>
    /// Ausgewählte Duplikate werden in den Papierkorb verschoben.
    /// </summary>
    Deleting,

    /// <summary>
    /// Vorgang abgeschlossen (keine Duplikate mehr oder Löschung erledigt).
    /// </summary>
    Completed,

    /// <summary>
    /// Ein Fehler ist aufgetreten.
    /// </summary>
    Error,
}
