using System.Text.RegularExpressions;

namespace PictureSorter.App.Tests.Design;

/// <summary>
/// Hält fest, dass jeder Typ sagt, wofür es ihn gibt. Der Übersetzer erzwingt das nur für
/// öffentliche Typen; die Anzeigemodelle und die verschachtelten Typen fielen sonst heraus.
/// </summary>
/// <remarks>
/// In den Tests zählt nur die oberste Ebene: Eine eingebettete Attrappe ist Innenleben eines Tests,
/// ein Kopf an jeder von ihnen verdrängte den Satz, auf den es ankommt.
/// </remarks>
public sealed partial class TypeHeadGuardTests
{
    [Fact]
    public void EveryTypeInTheSourceTree_HasAHead() =>
        AssertNoFindings(Scan(Path.Combine(RepositoryRoot(), "src"), nestedToo: true));

    [Fact]
    public void EveryTopLevelTypeInTheTests_HasAHead() =>
        AssertNoFindings(Scan(Path.Combine(RepositoryRoot(), "tests"), nestedToo: false));

    [Theory]
    [InlineData("internal sealed class Bare\n{\n}\n", 1)]
    [InlineData("/// <summary>Sagt etwas.</summary>\ninternal sealed class Headed\n{\n}\n", 0)]
    [InlineData("/// <summary>Sagt etwas.</summary>\n[Fact]\ninternal sealed class Attributed\n{\n}\n", 0)]
    [InlineData("// Nur ein Zeilenkommentar.\ninternal sealed class LineComment\n{\n}\n", 1)]
    public void Finder_RecognisesAMissingHead(string source, int expected)
    {
        ArgumentNullException.ThrowIfNull(source);

        Assert.Equal(expected, FindHeadless(source.Split('\n'), nestedToo: true).Count());
    }

    private static void AssertNoFindings(List<string> findings) =>
        Assert.True(findings.Count == 0, "Typen ohne Kopf:\n  " + string.Join("\n  ", findings));

    private static List<string> Scan(string root, bool nestedToo)
    {
        List<string> findings = [];
        foreach (string file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            // Erzeugter Code gehört niemandem, der ihn kommentieren könnte.
            string[] parts = file.Split(Path.DirectorySeparatorChar);
            if (parts.Contains("obj") || parts.Contains("bin") || parts.Contains("Migrations"))
            {
                continue;
            }

            findings.AddRange(FindHeadless(File.ReadAllLines(file), nestedToo)
                .Select(name => $"{Path.GetRelativePath(root, file)}: {name}"));
        }

        return findings;
    }

    private static IEnumerable<string> FindHeadless(string[] lines, bool nestedToo)
    {
        for (int index = 0; index < lines.Length; index++)
        {
            Match match = TypeDeclaration().Match(lines[index]);
            if (!match.Success || (!nestedToo && match.Groups["indent"].Length > 0))
            {
                continue;
            }

            // Attribute und Leerzeilen stehen zwischen Kopf und Typ, ohne etwas zu ändern.
            int above = index - 1;
            while (above >= 0 && (lines[above].TrimStart().StartsWith('[') || IsAttributeBody(lines[above]) || string.IsNullOrWhiteSpace(lines[above])))
            {
                above--;
            }

            if (above < 0 || !lines[above].TrimStart().StartsWith("///", StringComparison.Ordinal))
            {
                yield return match.Groups["name"].Value;
            }
        }
    }

    // Die Fortsetzungszeilen eines mehrzeiligen Attributs wie SuppressMessage.
    private static bool IsAttributeBody(string line)
    {
        string trimmed = line.TrimStart();
        return trimmed.StartsWith('"') || trimmed.StartsWith("Justification", StringComparison.Ordinal);
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PictureSorter.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory.FullName;
    }

    [GeneratedRegex(@"^(?<indent>\s*)(?:(?:public|internal|private|protected|file|static|sealed|abstract|partial|readonly|unsafe)\s+)*(?:class|record|struct|interface|enum)\s+(?<name>[A-Z]\w*)")]
    private static partial Regex TypeDeclaration();
}
