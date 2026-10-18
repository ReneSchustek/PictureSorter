using PictureSorter.Infrastructure.FileSystem;

namespace PictureSorter.Infrastructure.Tests.FileSystem;

/// <summary>
/// Tests der Suche nach dem Ordner, der gemeint war. Gemessen an einem echten
/// Bestand aus iCloud: Der Ordner hieß nicht „iCloud Fotos", sondern trug zwischen
/// den beiden Wörtern ein geschütztes Leerzeichen. Er erschien in jeder Auflistung
/// und war über den abgetippten Pfad trotzdem nicht zu erreichen.
/// </summary>
public sealed class FolderLookupTests : IDisposable
{
    private const char NoBreakSpace = '\u00a0';

    private readonly string _root;
    private readonly FolderLookup _sut = new();

    public FolderLookupTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "PictureSorterTests", Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(_root);
    }

    [Fact]
    public void FindLookalike_FindsTheFolderWithTheNoBreakSpace()
    {
        string echt = Path.Combine(_root, $"iCloud{NoBreakSpace}Fotos");
        _ = Directory.CreateDirectory(echt);
        string getippt = Path.Combine(_root, "iCloud Fotos");

        Assert.Equal(echt, _sut.FindLookalike(getippt));
    }

    [Fact]
    public void FindLookalike_FindsItEvenWhenTheHiddenCharacterSitsFurtherUp()
    {
        // Das unsichtbare Zeichen kann überall im Pfad stecken, nicht nur im letzten
        // Ordner. Deshalb wird Abschnitt für Abschnitt gesucht.
        string oben = Path.Combine(_root, $"Meine{NoBreakSpace}Bilder");
        string echt = Path.Combine(oben, "2024");
        _ = Directory.CreateDirectory(echt);
        string getippt = Path.Combine(_root, "Meine Bilder", "2024");

        Assert.Equal(echt, _sut.FindLookalike(getippt));
    }

    [Fact]
    public void FindLookalike_ForAnExistingFolderReturnsNothing()
    {
        // Gibt es den Ordner, ist nichts zu erklären. Ein Vorschlag wäre hier nur
        // Lärm, und im schlimmsten Fall ein zweiter, gleich aussehender Ordner.
        string vorhanden = Path.Combine(_root, "Urlaub");
        _ = Directory.CreateDirectory(vorhanden);

        Assert.Null(_sut.FindLookalike(vorhanden));
    }

    [Fact]
    public void FindLookalike_ForAFolderThatSimplyDoesNotExistReturnsNothing()
    {
        Assert.Null(_sut.FindLookalike(Path.Combine(_root, "gibt-es-nicht")));
    }

    [Fact]
    public void FindLookalike_DoesNotMistakeADifferentFolderForTheOneMeant()
    {
        // Ein Ordner, der anders heißt, ist ein anderer Ordner. Ein falscher Vorschlag
        // wäre hier schlimmer als gar keiner: Die Nutzerin ließe einen Sortierlauf über
        // einen Bestand laufen, den sie nicht gemeint hat.
        _ = Directory.CreateDirectory(Path.Combine(_root, "Urlaub 2023"));

        Assert.Null(_sut.FindLookalike(Path.Combine(_root, "Urlaub 2024")));
    }

    [Fact]
    public void FindLookalike_WithoutPathIsRejected() =>
        Assert.Throws<ArgumentException>(() => _sut.FindLookalike(" "));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
