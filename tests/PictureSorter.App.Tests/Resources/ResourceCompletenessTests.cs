using System.Xml.Linq;

namespace PictureSorter.App.Tests.Resources;

/// <summary>
/// Stellt sicher, dass die Sprachressourcen vollständig und deckungsgleich sind:
/// Jeder Schlüssel muss in de-DE UND en-US existieren und darf keinen leeren Wert
/// haben. So fällt eine vergessene Übersetzung sofort auf, statt zur Laufzeit als
/// falsche Sprache aufzutauchen.
/// </summary>
public sealed class ResourceCompletenessTests
{
    [Fact]
    public void GermanAndEnglishResources_HaveIdenticalKeySets()
    {
        Dictionary<string, string> de = LoadResources("de-DE");
        Dictionary<string, string> en = LoadResources("en-US");

        string[] onlyInGerman = [.. de.Keys.Except(en.Keys).Order()];
        string[] onlyInEnglish = [.. en.Keys.Except(de.Keys).Order()];

        Assert.True(
            onlyInGerman.Length == 0,
            $"Schlüssel ohne englische Übersetzung: {string.Join(", ", onlyInGerman)}");
        Assert.True(
            onlyInEnglish.Length == 0,
            $"Schlüssel ohne deutsche Übersetzung: {string.Join(", ", onlyInEnglish)}");
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("en-US")]
    public void Resources_HaveNoEmptyValues(string language)
    {
        Dictionary<string, string> resources = LoadResources(language);

        string[] empty = [.. resources.Where(pair => string.IsNullOrWhiteSpace(pair.Value)).Select(pair => pair.Key).Order()];

        Assert.True(empty.Length == 0, $"Leere Ressourcenwerte ({language}): {string.Join(", ", empty)}");
    }

    private static Dictionary<string, string> LoadResources(string language)
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

    // Vom Testausgabeverzeichnis nach oben bis zur Solution laufen und von dort die
    // Ressourcen im App-Projekt ansteuern.
    private static string FindResourceDirectory()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "PictureSorter.slnx")))
        {
            current = current.Parent;
        }

        Assert.NotNull(current);
        return Path.Combine(current!.FullName, "src", "PictureSorter.App", "Strings");
    }
}
