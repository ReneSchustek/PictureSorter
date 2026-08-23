using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Core.Tests.ValueObjects;

/// <summary>
/// Tests der Zeichen, die man sieht, aber nicht unterscheiden kann. Der Anlass ist
/// ein Bestand aus iCloud: Sein Ordner trug ein geschütztes Leerzeichen zwischen zwei
/// Wörtern. Wer den Pfad abtippte, traf ihn nicht — und bekam eine Meldung zu lesen,
/// die zwar stimmte, aber niemanden weiterbrachte.
/// </summary>
public sealed class InvisibleCharactersTests
{
    private const char NoBreakSpace = '\u00a0';
    private const char ZeroWidthSpace = '\u200b';
    private const char NarrowNoBreakSpace = '\u202f';

    [Fact]
    public void Normalize_TurnsEveryKindOfSpaceIntoAPlainSpace()
    {
        string mit = $"iCloud{NoBreakSpace}Fotos{NarrowNoBreakSpace}2024";

        Assert.Equal("iCloud Fotos 2024", InvisibleCharacters.Normalize(mit));
    }

    [Fact]
    public void Normalize_DropsCharactersWithoutWidth()
    {
        // Ein Zeichen ohne Breite hinterlässt keine Spur auf dem Bildschirm. Beim
        // Vergleich muss es deshalb entfallen und nicht zu einem Leerzeichen werden —
        // sonst gälte „iCloudFotos" plötzlich als „iCloud Fotos".
        string mit = $"iCloud{ZeroWidthSpace}Fotos";

        Assert.Equal("iCloudFotos", InvisibleCharacters.Normalize(mit));
    }

    [Fact]
    public void Normalize_LeavesAnOrdinaryNameAlone()
    {
        Assert.Equal("Urlaub 2023", InvisibleCharacters.Normalize("Urlaub 2023"));
    }

    [Theory]
    [InlineData('\u00a0')]
    [InlineData('\u2007')]
    [InlineData('\u202f')]
    [InlineData('\u200b')]
    public void ContainsAny_FindsTheCharacterOneCannotSee(char versteckt)
    {
        Assert.True(InvisibleCharacters.ContainsAny($"iCloud{versteckt}Fotos"));
    }

    [Fact]
    public void ContainsAny_ForAnOrdinaryNameIsFalse()
    {
        Assert.False(InvisibleCharacters.ContainsAny("iCloud Fotos"));
    }

    [Fact]
    public void LookAlikeButDiffer_ForTheNoBreakSpaceIsTrue()
    {
        Assert.True(InvisibleCharacters.LookAlikeButDiffer("iCloud Fotos", $"iCloud{NoBreakSpace}Fotos"));
    }

    [Fact]
    public void LookAlikeButDiffer_ForTwoIdenticalNamesIsFalse()
    {
        // Gleiche Namen sind kein Fall zum Erklären. Wäre die Antwort hier wahr, böte
        // die Anwendung als Lösung genau den Pfad an, der eben nicht funktioniert hat.
        Assert.False(InvisibleCharacters.LookAlikeButDiffer("iCloud Fotos", "iCloud Fotos"));
    }

    [Fact]
    public void LookAlikeButDiffer_ForTwoRealDifferencesIsFalse()
    {
        Assert.False(InvisibleCharacters.LookAlikeButDiffer("iCloud Fotos", "iCloud Videos"));
    }

    [Fact]
    public void Normalize_WithoutNameIsRejected() =>
        Assert.Throws<ArgumentNullException>(() => InvisibleCharacters.Normalize(null!));

    [Fact]
    public void ContainsAny_WithoutNameIsRejected() =>
        Assert.Throws<ArgumentNullException>(() => InvisibleCharacters.ContainsAny(null!));

    [Fact]
    public void LookAlikeButDiffer_WithoutNameIsRejected()
    {
        _ = Assert.Throws<ArgumentNullException>(() => InvisibleCharacters.LookAlikeButDiffer(null!, "x"));
        _ = Assert.Throws<ArgumentNullException>(() => InvisibleCharacters.LookAlikeButDiffer("x", null!));
    }
}
