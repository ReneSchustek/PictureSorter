using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace PictureSorter.App.Tests.Design;

/// <summary>
/// Hält die Gestaltungslinie (<c>F:\Entwicklung\_ai\rules\global\design-system.md</c>).
/// </summary>
/// <remarks>
/// Eine Gestaltungslinie, die nur in einem Dokument steht, hält kein Jahr: Nach dem
/// dritten „nur hier einmal schnell" ist sie Papier. Diese Prüfungen machen den Build
/// rot, statt auf gutes Zureden zu hoffen.
///
/// Bewusst als Test und nicht als eigener Schritt: So läuft die Prüfung bei jedem
/// Testlauf mit, braucht keine zusätzliche Verdrahtung und wirkt auch dort, wo keine
/// Fließbandprüfung läuft.
/// </remarks>
public sealed class GestaltungslinieGuardTests
{
    // Farbwerte gehören in die Palette, nicht in eine Ansicht. „Transparent" ist keine
    // Farbe der Marke, sondern eine Aussage über die Fläche — deshalb erlaubt.
    private static readonly Regex ColorAttribute = new(
        "(Background|Foreground|BorderBrush|Fill|Stroke|CaretBrush|Color)\\s*=\\s*"
        + "\"(#[0-9A-Fa-f]{3,8}|White|Black|Gray|LightGray|DarkGray|Silver|Red|Green|Blue|Yellow|Orange|Navy|Teal)\"",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Ecken bleiben klein: 4 bis 6. Die Marke ist Industrie, nicht Spielzeug — und ein
    // Radius, der einmal aus der Reihe fällt, zieht die nächsten hinter sich her.
    // Erfasst beide Schreibweisen: das Attribut an einem Element (CornerRadius="8") und
    // den Setter in einem gemeinsamen Style (Property="CornerRadius" Value="8").
    private static readonly Regex CornerRadiusAttribute = new(
        "CornerRadius(?:\"\\s+Value)?\\s*=\\s*\"([0-9]+)\"",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [Fact]
    public void ViewsContainNoColorValues()
    {
        List<string> findings = [];

        foreach (string file in ViewFiles())
        {
            string[] lines = File.ReadAllLines(file);
            for (int index = 0; index < lines.Length; index++)
            {
                Match match = ColorAttribute.Match(lines[index]);
                if (match.Success)
                {
                    findings.Add($"{Path.GetFileName(file)}:{index + 1}  {match.Value.Trim()}");
                }
            }
        }

        Assert.True(
            findings.Count == 0,
            "Farbwerte gehören in Themes/AppColors.xaml, nicht in eine Ansicht. Sonst bleibt "
            + "die Stelle beim Wechsel des Erscheinungsbilds stehen:"
            + Environment.NewLine + string.Join(Environment.NewLine, findings));
    }

    /// <remarks>
    /// Fehlt ein Schlüssel in einer Belegung, bricht die Bindung genau in dieser Belegung —
    /// und das fällt erst beim Nutzer auf, nicht beim Entwickeln. Das ist die wichtigere
    /// der beiden Prüfungen.
    /// </remarks>
    [Fact]
    public void LightAndDarkDefineTheSameKeys()
    {
        HashSet<string> light = PaletteKeys("Light");
        HashSet<string> dark = PaletteKeys("Dark");

        List<string> onlyLight = [.. light.Except(dark).Order()];
        List<string> onlyDark = [.. dark.Except(light).Order()];

        Assert.True(
            onlyLight.Count == 0 && onlyDark.Count == 0,
            "Die helle und die dunkle Belegung müssen denselben Schlüsselsatz führen."
            + Environment.NewLine + "Nur hell: " + string.Join(", ", onlyLight)
            + Environment.NewLine + "Nur dunkel: " + string.Join(", ", onlyDark));
    }

    [Fact]
    public void CornerRadiiStaySmall()
    {
        const int maximum = 6;
        List<string> findings = [];

        foreach (string file in ViewFiles())
        {
            string[] lines = File.ReadAllLines(file);
            for (int index = 0; index < lines.Length; index++)
            {
                Match match = CornerRadiusAttribute.Match(lines[index]);
                if (match.Success
                    && int.TryParse(match.Groups[1].Value, out int radius)
                    && radius > maximum)
                {
                    findings.Add($"{Path.GetFileName(file)}:{index + 1}  {match.Value.Trim()}");
                }
            }
        }

        Assert.True(
            findings.Count == 0,
            $"Ecken bleiben bei höchstens {maximum} Punkten; die Werte stehen in Themes/Tokens.xaml:"
            + Environment.NewLine + string.Join(Environment.NewLine, findings));
    }

    [Fact]
    public void TokensDefineSpacingAndTypography()
    {
        // Ohne diese Schlüssel gäbe es nichts zu binden, und jede Ansicht wählte ihre
        // Abstände wieder selbst.
        HashSet<string> tokens = ResourceKeys(Path.Combine(ThemesDirectory(), "Tokens.xaml"));

        Assert.Contains("SpacingM", tokens);
        Assert.Contains("PagePadding", tokens);
        Assert.Contains("AppCardCornerRadius", tokens);
        Assert.Contains("AppFontFamily", tokens);
        Assert.Contains("FontSizeBody", tokens);
    }

    [Fact]
    public void PaletteAndViewsAreNotEmpty()
    {
        // Fängt den Fall ab, dass die Prüfungen oben mangels gefundener Dateien grün wären
        // — eine Prüfung, die nichts findet, meldet sonst dasselbe wie eine bestandene.
        Assert.NotEmpty(PaletteKeys("Light"));
        Assert.NotEmpty(ViewFiles());
    }

    [Fact]
    public void EveryTokenIsUsedInAPropertyOfItsOwnType()
    {
        // Zwei Abstürze an einem Tag hatten dieselbe Ursache: ein Token vom falschen Typ.
        // „CharacterSpacing" nahm einen x:Double statt einer Ganzzahl, „Padding" einen
        // x:Double statt einer Kante. Beides übersetzt sauber durch, beides lässt die
        // Seite zur Laufzeit weiß bleiben oder mit einer XamlParseException abbrechen —
        // und kein Test mit Fakes kann das sehen, weil keiner einen XAML-Host hat.
        Dictionary<string, string> tokenTypes = TokenTypes();
        List<string> findings = [];

        foreach (string file in DesignFiles())
        {
            string[] lines = File.ReadAllLines(file);
            for (int index = 0; index < lines.Length; index++)
            {
                foreach (Match match in TokenUsage.Matches(lines[index]))
                {
                    string property = match.Groups[1].Value;
                    string token = match.Groups[2].Value;

                    if (!tokenTypes.TryGetValue(token, out string? actual)
                        || !ExpectedTokenTypes.TryGetValue(property, out string? expected)
                        || string.Equals(actual, expected, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    findings.Add(
                        $"{Path.GetFileName(file)}:{index + 1}  {property} bekommt {token} ({actual}), "
                        + $"gebraucht wird {expected}");
                }
            }
        }

        Assert.True(
            findings.Count == 0,
            "Token vom falschen Typ - das bricht erst zur Laufzeit:" + Environment.NewLine
            + string.Join(Environment.NewLine, findings));
    }

    // Welchen Typ eine Eigenschaft verlangt. Nur die, bei denen ein falscher Typ nicht
    // beim Übersetzen auffällt.
    private static readonly Dictionary<string, string> ExpectedTokenTypes = new(StringComparer.Ordinal)
    {
        ["Padding"] = "Thickness",
        ["Margin"] = "Thickness",
        ["BorderThickness"] = "Thickness",
        ["CornerRadius"] = "CornerRadius",
        ["CharacterSpacing"] = "Int32",
        ["Spacing"] = "Double",
        ["FontSize"] = "Double",
        ["FontFamily"] = "FontFamily",
    };

    private static readonly Regex TokenUsage = new(
        @"([A-Za-z]+)\s*=\s*""\{StaticResource ([A-Za-z0-9]+)\}""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Der Typ jedes Tokens, so wie er in der Datei steht: <x:Double>, <Thickness>, …
    private static Dictionary<string, string> TokenTypes()
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        Dictionary<string, string> types = new(StringComparer.Ordinal);

        foreach (XElement element in XDocument.Load(Path.Combine(ThemesDirectory(), "Tokens.xaml")).Descendants())
        {
            string? key = element.Attribute(x + "Key")?.Value;
            if (key is not null)
            {
                types[key] = element.Name.LocalName;
            }
        }

        return types;
    }

    // Ansichten, gemeinsame Styles und die Bausteine. Die Bausteine gehören dazu: Der
    // erste Fehler dieser Art steckte in einem von ihnen.
    private static IReadOnlyList<string> DesignFiles() =>
        [
            .. ViewFiles(),
            .. Directory.EnumerateFiles(
                Path.Combine(AppProjectDirectory(), "Controls"), "*.xaml", SearchOption.AllDirectories),
        ];

    // Die Ansichten und die gemeinsamen Styles. App.xaml gehört ausdrücklich dazu: Dort
    // standen drei Radien, die eine Prüfung allein über den Ordner „Views" nicht gesehen
    // hätte — und gerade die gemeinsamen Styles wirken auf jede Seite.
    private static IReadOnlyList<string> ViewFiles() =>
        [
            .. Directory.EnumerateFiles(
                Path.Combine(AppProjectDirectory(), "Views"), "*.xaml", SearchOption.AllDirectories),
            Path.Combine(AppProjectDirectory(), "App.xaml"),
        ];

    // Anders als in einer WPF-Anwendung stehen beide Belegungen in einer Datei, unter
    // ThemeDictionaries. Gelesen wird deshalb der Zweig zum jeweiligen Namen.
    private static HashSet<string> PaletteKeys(string theme)
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XDocument document = XDocument.Load(Path.Combine(ThemesDirectory(), "AppColors.xaml"));

        XElement? palette = document
            .Descendants()
            .FirstOrDefault(element => element.Attribute(x + "Key")?.Value == theme);

        Assert.NotNull(palette);

        return
        [
            .. palette.Descendants()
                .Select(element => element.Attribute(x + "Key")?.Value)
                .Where(key => key is not null)
                .Select(key => key!)
        ];
    }

    private static HashSet<string> ResourceKeys(string path)
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        return
        [
            .. XDocument.Load(path)
                .Descendants()
                .Select(element => element.Attribute(x + "Key")?.Value)
                .Where(key => key is not null)
                .Select(key => key!)
        ];
    }

    private static string ThemesDirectory() => Path.Combine(AppProjectDirectory(), "Themes");

    // Vom Testausgabeverzeichnis nach oben, bis die Projektmappe auftaucht. So bleibt der
    // Pfad unabhängig davon, wo das Repository liegt.
    private static string AppProjectDirectory()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PictureSorter.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);

        return Path.Combine(directory.FullName, "src", "PictureSorter.App");
    }
}
