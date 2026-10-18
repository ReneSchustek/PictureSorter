using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Core.Tests.ValueObjects;

/// <summary>
/// Tests der Namensfindung. Sie entscheidet, wie eine Datei künftig heißt, und ein
/// Name ist alles, was die Nutzerin im Explorer von einem Foto sieht. Ein Fehler hier
/// tauscht einen unlesbaren Namen gegen einen falschen, und das ist keine
/// Verbesserung.
/// </summary>
public sealed class FileNameRepairTests
{
    private static readonly DateTimeOffset Captured =
        new(2021, 7, 14, 19, 42, 3, TimeSpan.Zero);

    [Fact]
    public void NeedsRepair_ForTheReplacementCharacterIsTrue()
    {
        // So sahen 159 Dateien eines aus iCloud geladenen Bestands aus: Das
        // Ersatzzeichen steht für „hier stand etwas, das sich nicht abbilden ließ".
        Assert.True(FileNameRepair.NeedsRepair("\uFFFD4\uFFFDӝ___.JPEG"));
    }

    // Die Zeichen stehen hier als Code und nicht als Zeichen. Als Zeichen geschrieben
    // überleben sie den Weg durch Editor und Zwischenablage nicht: Aus dem geschützten
    // wird ein gewöhnliches Leerzeichen, ein Steuerzeichen fällt ganz weg. Der Test
    // wäre grün, ohne je geprüft zu haben, worum es geht.
    [Theory]
    [InlineData("bild\u0007.jpg")]
    [InlineData("bild\u0000.jpg")]
    [InlineData("bild\u00a0kopie.jpg")]
    [InlineData("bild\u200bkopie.jpg")]
    public void NeedsRepair_ForUnreadableCharactersIsTrue(string fileName)
    {
        Assert.True(FileNameRepair.NeedsRepair(fileName));
    }

    [Theory]
    [InlineData("IMG_2041.jpg")]
    [InlineData("Urlaub 2023 - Kopie.jpeg")]
    [InlineData("Schöne Aussicht (2).PNG")]
    public void NeedsRepair_ForAnOrdinaryNameIsFalse(string fileName)
    {
        Assert.False(FileNameRepair.NeedsRepair(fileName));
    }

    [Fact]
    public void NeedsRepair_ForAnEmojiIsFalse()
    {
        // Ein Emoji besteht aus einem Paar von Surrogaten und ist ein gültiges
        // Zeichen. Wer nur auf „Surrogat" prüft, bietet jeden Namen mit Emoji zum
        // Umbenennen an, und benennt der Nutzerin ihre Urlaubsbilder um.
        Assert.False(FileNameRepair.NeedsRepair("Strand \U0001F334.jpg"));
    }

    [Fact]
    public void NeedsRepair_ForAHalfCharacterIsTrue()
    {
        // Ein Surrogat ohne Partner ist ein halbes Zeichen: kaputt, nicht darstellbar.
        Assert.True(FileNameRepair.NeedsRepair("Strand \ud83c.jpg"));
    }

    [Fact]
    public void HasUnreadableExtension_ForABrokenExtensionIsTrue()
    {
        // Aus einer kaputten Endung ließe sich das Bildformat nur raten, und eine
        // geratene Endung macht die Datei mit einem Klick unbrauchbar.
        Assert.True(FileNameRepair.HasUnreadableExtension("bild.j\uFFFDg"));
    }

    [Fact]
    public void HasUnreadableExtension_ForABrokenStemIsFalse()
    {
        Assert.False(FileNameRepair.HasUnreadableExtension("\uFFFD\uFFFD.jpg"));
    }

    [Fact]
    public void Suggest_UsesTheCaptureDateWhenThereIsOne()
    {
        string suggestion = FileNameRepair.Suggest("\uFFFD\uFFFD.JPEG", Captured, "iCloud Fotos", 1);

        Assert.Equal("2021-07-14_19-42-03.JPEG", suggestion);
    }

    [Fact]
    public void Suggest_WithoutACaptureDateUsesTheFolderAndANumber()
    {
        // Keine der 159 kaputt benannten Dateien trug ein Aufnahmedatum. Dann bleibt
        // nur der Zusammenhang, in dem die Datei steht.
        string suggestion = FileNameRepair.Suggest("\uFFFD\uFFFD.JPEG", capturedAt: null, "iCloud Fotos", 7);

        Assert.Equal("iCloud Fotos 007.JPEG", suggestion);
    }

    [Fact]
    public void Suggest_CleansTheFolderNameItUses()
    {
        // Der Ordnername stammt aus demselben Bestand und ist selbst nicht über jeden
        // Zweifel erhaben: Genau dieser Ordner trug ein geschütztes Leerzeichen.
        string suggestion = FileNameRepair.Suggest("\uFFFD.jpg", capturedAt: null, "iCloud\u00a0Fotos", 3);

        Assert.Equal("iCloud Fotos 003.jpg", suggestion);
    }

    [Fact]
    public void Suggest_KeepsTheExtensionUntouched()
    {
        // Die Endung ist die einzige verlässliche Auskunft darüber, was in der Datei
        // steht. Sie wird nie geraten.
        Assert.EndsWith(".JPEG", FileNameRepair.Suggest("\uFFFD.JPEG", Captured, "Ordner", 1), StringComparison.Ordinal);
        Assert.EndsWith(".heic", FileNameRepair.Suggest("\uFFFD.heic", Captured, "Ordner", 1), StringComparison.Ordinal);
    }

    [Fact]
    public void Suggest_WithoutAnyExtensionKeepsItThatWay()
    {
        Assert.Equal("2021-07-14_19-42-03", FileNameRepair.Suggest("\uFFFD\uFFFD", Captured, "Ordner", 1));
    }

    [Fact]
    public void MakeUnique_ForAFreeNameChangesNothing()
    {
        Assert.Equal("bild.jpg", FileNameRepair.MakeUnique("bild.jpg", _ => false));
    }

    [Fact]
    public void MakeUnique_ForATakenNameAddsANumber()
    {
        // Zwei Fotos derselben Sekunde sind bei einer Serienaufnahme der Normalfall.
        HashSet<string> taken = new(StringComparer.OrdinalIgnoreCase) { "2021-07-14_19-42-03.jpg" };

        Assert.Equal("2021-07-14_19-42-03-2.jpg", FileNameRepair.MakeUnique("2021-07-14_19-42-03.jpg", taken.Contains));
    }

    [Fact]
    public void MakeUnique_CountsOnUntilTheNameIsFree()
    {
        HashSet<string> taken = new(StringComparer.OrdinalIgnoreCase)
        {
            "bild.jpg", "bild-2.jpg", "bild-3.jpg",
        };

        Assert.Equal("bild-4.jpg", FileNameRepair.MakeUnique("bild.jpg", taken.Contains));
    }

    [Fact]
    public void Suggest_WithAnUnusableFolderNameStillFindsAName()
    {
        // Bliebe vom Ordnernamen nichts übrig, hieße die Datei „ 001.jpg", ein Name,
        // der mit einem Leerzeichen beginnt und unter Windows Ärger macht.
        Assert.Equal("Bild 001.jpg", FileNameRepair.Suggest("\uFFFD.jpg", capturedAt: null, "\uFFFD\uFFFD", 1));
    }

    [Fact]
    public void Suggest_WithoutAFileNameIsRejected() =>
        Assert.Throws<ArgumentNullException>(() => FileNameRepair.Suggest(null!, Captured, "Ordner", 1));

    [Fact]
    public void Suggest_WithoutANumberIsRejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => FileNameRepair.Suggest("a.jpg", Captured, "Ordner", 0));

    [Fact]
    public void NeedsRepair_WithoutANameIsRejected() =>
        Assert.Throws<ArgumentNullException>(() => FileNameRepair.NeedsRepair(null!));

    [Fact]
    public void MakeUnique_WithoutASuggestionIsRejected() =>
        Assert.Throws<ArgumentException>(() => FileNameRepair.MakeUnique(" ", _ => false));
}
