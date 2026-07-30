namespace PictureSorter.App.ViewModels;

/// <summary>
/// Auswahl, welche Protokolleinträge die Einstellungsseite zeigt.
/// </summary>
internal enum LogFilter
{
    /// <summary>Alle Einträge.</summary>
    All = 0,

    /// <summary>Nur Warnungen, Fehler und schwerwiegende Fehler.</summary>
    ProblemsOnly = 1,
}
