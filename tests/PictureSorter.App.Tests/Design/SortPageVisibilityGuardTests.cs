using System.Xml.Linq;

namespace PictureSorter.App.Tests.Design;

/// <summary>
/// Hält fest, dass die Wahl des Dateiwegs in beiden Ansichten der Sortierseite sichtbar ist. Die
/// Eigenschaften dahinter funktionierten auch, als das Kästchen in der geführten Ansicht fehlte;
/// ein Test am Anzeigemodell sieht das nicht, deshalb wird die Seite selbst gelesen.
/// </summary>
public sealed class SortPageVisibilityGuardTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Theory]
    [InlineData("SortPage_CopyInsteadOfMoveCheck")]
    [InlineData("SortPage_TargetRootBox")]
    [InlineData("SortPage_PickTargetRoot")]
    public void Option_IsNotLimitedToTheStandardView(string uid)
    {
        XDocument page = XDocument.Load(SortPagePath());
        XElement element = page.Descendants().Single(node => (string?)node.Attribute(Xaml + "Uid") == uid);

        // Die Karte des Schritts darf sichtbar schalten. Verboten ist nur die Bindung an die
        // Standard-Ansicht, ganz gleich auf welcher Ebene sie steht.
        IEnumerable<string> bindings = element
            .AncestorsAndSelf()
            .Select(node => (string?)node.Attribute("Visibility") ?? string.Empty);

        Assert.DoesNotContain(bindings, binding => binding.Contains("ShowStandard", StringComparison.Ordinal));
    }

    private static string SortPagePath()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PictureSorter.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(directory.FullName, "src", "PictureSorter.App", "Views", "SortPage.xaml");
    }
}
