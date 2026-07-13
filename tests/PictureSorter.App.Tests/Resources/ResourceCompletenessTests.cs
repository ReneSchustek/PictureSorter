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
        Dictionary<string, string> de = TestResources.Load("de-DE");
        Dictionary<string, string> en = TestResources.Load("en-US");

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
        Dictionary<string, string> resources = TestResources.Load(language);

        string[] empty = [.. resources.Where(pair => string.IsNullOrWhiteSpace(pair.Value)).Select(pair => pair.Key).Order()];

        Assert.True(empty.Length == 0, $"Leere Ressourcenwerte ({language}): {string.Join(", ", empty)}");
    }

    /// <summary>
    /// Die Platzhalter eines Textes müssen in beiden Sprachen identisch sein. Fehlt
    /// im Englischen ein <c>{1}</c>, verschwindet dort ein Wert wortlos; steht dort ein
    /// <c>{2}</c> zu viel, wirft die Formatierung zur Laufzeit.
    /// </summary>
    [Fact]
    public void Placeholders_MatchAcrossLanguages()
    {
        Dictionary<string, string> de = TestResources.Load("de-DE");
        Dictionary<string, string> en = TestResources.Load("en-US");

        string[] mismatched =
        [
            .. de.Where(pair => en.TryGetValue(pair.Key, out string? english)
                    && PlaceholderCount(pair.Value) != PlaceholderCount(english))
                .Select(pair => pair.Key)
                .Order(StringComparer.Ordinal),
        ];

        Assert.True(
            mismatched.Length == 0,
            $"Unterschiedliche Platzhalter in de/en: {string.Join(", ", mismatched)}");
    }

    // Zählt die verschiedenen Platzhalter {0}, {1}, … eines Textes.
    private static int PlaceholderCount(string value) =>
        Enumerable.Range(0, 10).Count(index => value.Contains($"{{{index}}}", StringComparison.Ordinal));
}
