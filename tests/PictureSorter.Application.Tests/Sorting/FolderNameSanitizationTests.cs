using PictureSorter.Application.Sorting;

namespace PictureSorter.Application.Tests.Sorting;

/// <summary>
/// Prüft die Bereinigung der Zielordnernamen. Schwerpunkt ist die Pfad-Sicherheit:
/// ein Kategoriename darf nicht aus dem Quellordner heraus in den Elternordner
/// zeigen ("." / ".."), und ungültige Dateizeichen werden neutralisiert.
/// </summary>
public sealed class FolderNameSanitizationTests
{
    [Theory]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("...")]
    [InlineData("   ")]
    [InlineData("")]
    public void SanitizeFolderName_PathEscapingOrEmptyNames_FallBackToNeutral(string name)
    {
        string result = TargetFolderNaming.SanitizeFolderName(name);

        Assert.Equal("Sonstige", result);
    }

    [Theory]
    [InlineData("Urlaub/2025", "Urlaub_2025")]
    [InlineData("Familie\\Fest", "Familie_Fest")]
    [InlineData("Foto: Strand", "Foto_ Strand")]
    public void SanitizeFolderName_InvalidCharacters_AreReplaced(string name, string expected)
    {
        string result = TargetFolderNaming.SanitizeFolderName(name);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("Urlaub")]
    [InlineData("Weihnachten 2025")]
    [InlineData("Oma & Opa")]
    public void SanitizeFolderName_ValidNames_ArePreserved(string name)
    {
        string result = TargetFolderNaming.SanitizeFolderName(name);

        Assert.Equal(name, result);
    }

    [Theory]
    [InlineData("CON", "CON_")]
    [InlineData("nul", "nul_")]
    [InlineData("COM1", "COM1_")]
    [InlineData("LPT9", "LPT9_")]
    [InlineData("Prn.jpg", "Prn.jpg_")]
    public void SanitizeFolderName_WindowsDeviceNames_AreMadeUsable(string name, string expected)
    {
        // Windows hält diese Namen für Geräte, unabhängig von Groß-/Kleinschreibung und
        // Endung. Ein Ordner dieses Namens lässt sich nicht anlegen – die Nutzerin sähe
        // eine Fehlermeldung, die ihr nichts sagt.
        string result = TargetFolderNaming.SanitizeFolderName(name);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("Urlaub.", "Urlaub")]
    [InlineData("Ostern..", "Ostern")]
    public void SanitizeFolderName_TrailingDots_AreRemoved(string name, string expected)
    {
        // Windows schneidet Punkte am Ende beim Anlegen still ab. Bliebe der Punkt im
        // Namen stehen, wiche der protokollierte Pfad vom tatsächlichen ab – und genau
        // der Pfad ist die Grundlage des Rückgängigmachens.
        string result = TargetFolderNaming.SanitizeFolderName(name);

        Assert.Equal(expected, result);
    }
}
