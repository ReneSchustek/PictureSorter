namespace PictureSorter.App.ViewModels;

/// <summary>
/// Expliziter Zustand der Gedächtnis-Verwaltung. Single Source of Truth für das
/// Verhalten der Oberfläche.
/// </summary>
internal enum MemoryState
{
    /// <summary>
    /// Bereit; noch nichts geladen.
    /// </summary>
    Idle,

    /// <summary>
    /// Die Einträge werden geladen.
    /// </summary>
    Loading,

    /// <summary>
    /// Einträge liegen zur Durchsicht vor.
    /// </summary>
    Review,

    /// <summary>
    /// Einträge werden entfernt.
    /// </summary>
    Deleting,

    /// <summary>
    /// Vorgang abgeschlossen; es gibt keine Einträge (mehr).
    /// </summary>
    Empty,

    /// <summary>
    /// Ein Fehler ist aufgetreten.
    /// </summary>
    Error,
}
