using System.Text.RegularExpressions;

namespace PictureSorter.App.Tests.Resources;

/// <summary>
/// Prüft die Code-Strings gegen die Ressourcen: Jeder Schlüssel, den der Code über
/// <c>ILocalizer</c> anfordert, muss in beiden Sprachen hinterlegt sein. Ein Tippfehler
/// im Schlüssel fiele sonst erst zur Laufzeit auf – und dann nur dem Nutzer, dem statt
/// der Meldung der rohe Schlüssel angezeigt würde.
/// </summary>
public sealed partial class CodeStringKeyTests
{
    [Fact]
    public void EveryKeyUsedInCode_ExistsInBothLanguages()
    {
        Dictionary<string, string> german = TestResources.Load("de-DE");
        Dictionary<string, string> english = TestResources.Load("en-US");

        string[] missing =
        [
            .. CollectKeysUsedInCode()
                .Where(key => !german.ContainsKey(key) || !english.ContainsKey(key))
                .Order(StringComparer.Ordinal),
        ];

        Assert.True(missing.Length == 0, $"Im Code verwendete, aber nicht übersetzte Schlüssel: {string.Join(", ", missing)}");
    }

    [Fact]
    public void CodeUsesLocalizedStrings_AtAll()
    {
        // Schutz vor einem stillen Fehlschlag der Suche: Fände das Muster nichts mehr
        // (etwa nach einer Umbenennung von ILocalizer), liefe der Test oben grün, ohne
        // noch irgendetwas zu prüfen.
        Assert.NotEmpty(CollectKeysUsedInCode());
    }

    private static HashSet<string> CollectKeysUsedInCode()
    {
        HashSet<string> keys = new(StringComparer.Ordinal);
        foreach (string file in Directory.EnumerateFiles(TestResources.FindSourceDirectory(), "*.cs", SearchOption.AllDirectories))
        {
            // Erzeugte Dateien (obj/bin) tragen keine handgeschriebenen Schlüssel.
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (Match match in LocalizerCall().Matches(File.ReadAllText(file)))
            {
                _ = keys.Add(match.Groups[1].Value);
            }
        }

        return keys;
    }

    // Erfasst localizer.Get("Schlüssel") und localizer.Format("Schlüssel", …).
    [GeneratedRegex("\\.(?:Get|Format)\\(\\s*\"([A-Za-z0-9_]+)\"")]
    private static partial Regex LocalizerCall();
}
