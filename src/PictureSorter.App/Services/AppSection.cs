namespace PictureSorter.App.Services;

/// <summary>
/// Die Bereiche der Anwendung. Bewusst ein Aufzählungstyp statt der bisherigen
/// Zeichenketten-Kennungen: Ein Tippfehler im Ziel fällt dann beim Übersetzen auf
/// und nicht erst, wenn die Nutzerin auf eine Kachel klickt.
/// </summary>
internal enum AppSection
{
    /// <summary>Die Startseite.</summary>
    Dashboard,

    /// <summary>Die Sortier-Ansicht.</summary>
    Sort,

    /// <summary>Die Duplikat-Suche.</summary>
    Duplicates,

    /// <summary>Die Ablage nach Aufnahmedatum.</summary>
    Calendar,

    /// <summary>Die Gedächtnis-Verwaltung.</summary>
    Memory,

    /// <summary>Die Einstellungen.</summary>
    Settings,
}
