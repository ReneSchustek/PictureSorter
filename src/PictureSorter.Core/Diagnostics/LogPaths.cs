namespace PictureSorter.Core.Diagnostics;

/// <summary>
/// Hilfsfunktionen, um Pfade vor der Protokollierung von personenbeziehbaren
/// Bestandteilen zu befreien. Ordner- und Dateipfade enthalten unter Windows das
/// Benutzerprofil (<c>C:\Users\&lt;Name&gt;\…</c>) und damit den Anmeldenamen. Für die
/// Fehlerdiagnose genügt die Struktur unterhalb des Profils; der Name gehört nicht
/// ins Protokoll (siehe Logging-/Datenschutzvorgaben).
/// </summary>
public static class LogPaths
{
    private static readonly string ProfileRoot =
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>
    /// Ersetzt das Benutzerprofil-Präfix eines Pfads durch <c>~</c>. Liegt der Pfad
    /// außerhalb des Profils, wird er unverändert zurückgegeben.
    /// </summary>
    /// <param name="path">Der zu redigierende Pfad (darf <see langword="null"/> sein).</param>
    /// <returns>Der Pfad ohne Benutzernamen.</returns>
    public static string Redact(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return string.Empty;
        }

        if (ProfileRoot.Length > 0
            && path.StartsWith(ProfileRoot, StringComparison.OrdinalIgnoreCase))
        {
            return string.Concat("~", path.AsSpan(ProfileRoot.Length));
        }

        return path;
    }
}
