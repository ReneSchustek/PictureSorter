using System.Text.RegularExpressions;

namespace PictureSorter.App.Tests.Resources;

/// <summary>
/// Prüft die Oberflächen-Texte gegen die Ressourcen: Jedes <c>x:Uid</c> in einer
/// XAML-Datei braucht einen Eintrag in beiden Sprachen. Fehlt er, meldet WinUI nichts –
/// es bleibt still der im XAML hinterlegte deutsche Text stehen, auch in der englischen
/// Fassung. Genau so blieb die Beschreibung des letzten Assistenten-Schritts unübersetzt,
/// ohne dass Build, Tests oder die bestehenden Ressourcen-Prüfungen etwas gemerkt hätten:
/// <see cref="CodeStringKeyTests"/> sieht nur die Schlüssel aus dem C#-Code,
/// <see cref="ResourceCompletenessTests"/> nur die beiden Sprachdateien gegeneinander.
/// </summary>
public sealed partial class XamlUidTests
{
    [Fact]
    public void EveryUidInXaml_ExistsInBothLanguages()
    {
        Dictionary<string, string> german = TestResources.Load("de-DE");
        Dictionary<string, string> english = TestResources.Load("en-US");
        HashSet<string> germanPrefixes = PrefixesOf(german);
        HashSet<string> englishPrefixes = PrefixesOf(english);

        string[] missing =
        [
            .. CollectUids()
                .Where(uid => !germanPrefixes.Contains(uid) || !englishPrefixes.Contains(uid))
                .Order(StringComparer.Ordinal),
        ];

        Assert.True(
            missing.Length == 0,
            $"x:Uid ohne Eintrag in den Sprachdateien: {string.Join(", ", missing)}");
    }

    [Fact]
    public void XamlUsesLocalizedTexts_AtAll()
    {
        // Schutz vor einem stillen Fehlschlag der Suche: Fände das Muster nichts mehr,
        // liefe der Test oben grün, ohne noch irgendetwas zu prüfen.
        Assert.NotEmpty(CollectUids());
    }

    // Der Teil eines Ressourcen-Schlüssels vor dem ersten Punkt entspricht dem x:Uid;
    // dahinter steht die Eigenschaft, etwa „.Text" oder „.Content".
    private static HashSet<string> PrefixesOf(Dictionary<string, string> resources) =>
        [.. resources.Keys.Select(key => key.Split('.', 2)[0])];

    private static HashSet<string> CollectUids()
    {
        HashSet<string> uids = new(StringComparer.Ordinal);
        foreach (string file in Directory.EnumerateFiles(TestResources.FindSourceDirectory(), "*.xaml", SearchOption.AllDirectories))
        {
            // Erzeugte Dateien (obj/bin) sind Kopien der handgeschriebenen.
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (Match match in UidAttribute().Matches(File.ReadAllText(file)))
            {
                _ = uids.Add(match.Groups[1].Value);
            }
        }

        return uids;
    }

    [GeneratedRegex("x:Uid=\"([^\"]+)\"")]
    private static partial Regex UidAttribute();
}
