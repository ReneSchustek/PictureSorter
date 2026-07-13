using System.Xml.Linq;

namespace PictureSorter.App.Tests.Resources;

/// <summary>
/// Zugriff auf die echten Sprachressourcen des App-Projekts. Die Tests lesen sie
/// direkt aus der Quelldatei: So prüfen sie gegen dieselben Texte, die auch die
/// Anwendung anzeigt, und ein fehlender Schlüssel fällt im Test auf statt erst
/// beim Nutzer.
/// </summary>
internal static class TestResources
{
    /// <summary>Die Sprache, in der die ViewModel-Tests laufen.</summary>
    public const string DefaultLanguage = "de-DE";

    /// <summary>
    /// Lädt alle Schlüssel-Wert-Paare einer Sprache.
    /// </summary>
    /// <param name="language">Der Sprachordner, z. B. <c>de-DE</c>.</param>
    /// <returns>Die Ressourcen der Sprache.</returns>
    public static Dictionary<string, string> Load(string language)
    {
        string path = Path.Combine(FindResourceDirectory(), language, "Resources.resw");
        XDocument document = XDocument.Load(path);

        return document.Root!
            .Elements("data")
            .ToDictionary(
                element => element.Attribute("name")!.Value,
                element => element.Element("value")?.Value ?? string.Empty,
                StringComparer.Ordinal);
    }

    /// <summary>
    /// Der Ordner mit den C#-Quelldateien der App-Schicht.
    /// </summary>
    /// <returns>Der absolute Pfad.</returns>
    public static string FindSourceDirectory() =>
        Path.Combine(FindSolutionDirectory(), "src", "PictureSorter.App");

    /// <summary>
    /// Der Ordner mit den Sprachressourcen.
    /// </summary>
    /// <returns>Der absolute Pfad.</returns>
    public static string FindResourceDirectory() =>
        Path.Combine(FindSourceDirectory(), "Strings");

    // Vom Testausgabeverzeichnis nach oben laufen, bis die Solution gefunden ist.
    private static string FindSolutionDirectory()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "PictureSorter.slnx")))
        {
            current = current.Parent;
        }

        return current is null
            ? throw new InvalidOperationException("Die Solution PictureSorter.slnx wurde nicht gefunden.")
            : current.FullName;
    }
}
