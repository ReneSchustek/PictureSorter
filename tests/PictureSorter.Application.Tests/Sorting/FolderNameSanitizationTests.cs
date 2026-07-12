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
        string result = PhotoSortingService.SanitizeFolderName(name);

        Assert.Equal("Sonstige", result);
    }

    [Theory]
    [InlineData("Urlaub/2025", "Urlaub_2025")]
    [InlineData("Familie\\Fest", "Familie_Fest")]
    [InlineData("Foto: Strand", "Foto_ Strand")]
    public void SanitizeFolderName_InvalidCharacters_AreReplaced(string name, string expected)
    {
        string result = PhotoSortingService.SanitizeFolderName(name);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("Urlaub")]
    [InlineData("Weihnachten 2025")]
    [InlineData("Oma & Opa")]
    public void SanitizeFolderName_ValidNames_ArePreserved(string name)
    {
        string result = PhotoSortingService.SanitizeFolderName(name);

        Assert.Equal(name, result);
    }
}
