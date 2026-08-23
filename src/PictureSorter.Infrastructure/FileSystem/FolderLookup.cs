using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Infrastructure.FileSystem;

/// <summary>
/// Sucht den Ordner, der gemeint war, wenn der angegebene Pfad ins Leere geht.
///
/// Der Anlass: Ein aus iCloud geladener Bestand lag in einem Ordner, dessen Name
/// zwischen zwei Wörtern ein geschütztes Leerzeichen trug. Auf dem Bildschirm sieht
/// das aus wie ein gewöhnliches. Wer den Pfad abtippte, bekam „Der Ordner wurde nicht
/// gefunden" zu lesen, während der Ordner im Explorer sichtbar danebenlag - eine
/// Meldung, die zwar stimmt, aber niemanden weiterbringt.
///
/// Gesucht wird Abschnitt für Abschnitt, weil das unsichtbare Zeichen überall im Pfad
/// stecken kann, nicht nur im letzten Ordner.
/// </summary>
public sealed class FolderLookup : IFolderLookup
{
    /// <inheritdoc />
    public string? FindLookalike(string requestedPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedPath);

        // Gibt es den Ordner, ist nichts zu erklären.
        if (Directory.Exists(requestedPath))
        {
            return null;
        }

        string full;
        try
        {
            full = Path.GetFullPath(requestedPath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // Ein Pfad, den das Betriebssystem gar nicht erst annimmt, hat keinen
            // Zwilling, den man vorschlagen könnte.
            return null;
        }

        string root = Path.GetPathRoot(full) ?? string.Empty;
        if (root.Length == 0 || !Directory.Exists(root))
        {
            return null;
        }

        string rest = full[root.Length..].Trim(Path.DirectorySeparatorChar);
        if (rest.Length == 0)
        {
            return null;
        }

        string current = root;
        foreach (string segment in rest.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            string? next = ResolveSegment(current, segment);
            if (next is null)
            {
                return null;
            }

            current = next;
        }

        // Nur melden, wenn sich wirklich etwas geändert hat. Ein Pfad, der sich nur in
        // der Groß- und Kleinschreibung unterscheidet, ist unter Windows derselbe und
        // wäre schon oben als vorhanden durchgegangen.
        return string.Equals(current, full, StringComparison.Ordinal) ? null : current;
    }

    private static string? ResolveSegment(string parent, string segment)
    {
        string direct = Path.Combine(parent, segment);
        if (Directory.Exists(direct))
        {
            return direct;
        }

        string wanted = InvisibleCharacters.Normalize(segment);
        try
        {
            foreach (string candidate in Directory.EnumerateDirectories(parent))
            {
                string name = Path.GetFileName(candidate);
                if (string.Equals(InvisibleCharacters.Normalize(name), wanted, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Ein Ordner, in den nicht hineingesehen werden darf, liefert keinen
            // Vorschlag. Das ist kein Fehler des Vorgangs, der ihn ausgelöst hat.
            return null;
        }

        return null;
    }
}
