using PictureSorter.Data.Context;

namespace PictureSorter.Data.Tests.Context;

/// <summary>
/// Tests der Pfadermittlung. Sie entscheidet, wo Sortier-Gedächtnis und Protokoll
/// liegen — im Benutzerprofil und nicht im Programmverzeichnis, damit auch eine
/// installierte Anwendung ohne Administratorrechte schreiben kann.
/// </summary>
public sealed class DatabasePathProviderTests
{
    [Fact]
    public void GetDatabasePath_WithoutArgument_PointsIntoTheUserProfile()
    {
        // Die Überladung ohne Argument ist der Weg, den die Anwendung im Betrieb
        // nimmt. Läge der Pfad neben dem Programm, scheiterte jede Installation nach
        // „C:\Programme" am fehlenden Schreibrecht.
        string erwartet = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PictureSorter",
            "picturesorter.db");

        Assert.Equal(erwartet, DatabasePathProvider.GetDatabasePath());
    }

    [Fact]
    public void GetDatabasePath_WithoutArgument_CreatesTheFolder()
    {
        string pfad = DatabasePathProvider.GetDatabasePath();

        // Der Ordner gehört der Anwendung und wird hier bewusst nicht wieder
        // entfernt: Er ist derselbe, in dem eine laufende Installation ihre Daten
        // hält.
        Assert.True(Directory.Exists(Path.GetDirectoryName(pfad)));
    }

    [Fact]
    public void GetDatabasePath_WithAFolder_CreatesItAndKeepsTheFileName()
    {
        string ordner = Path.Combine(Path.GetTempPath(), "PictureSorterTests", "pfad-probe");
        if (Directory.Exists(ordner)) { Directory.Delete(ordner, recursive: true); }

        try
        {
            string pfad = DatabasePathProvider.GetDatabasePath(ordner);

            Assert.True(Directory.Exists(ordner));
            Assert.Equal(Path.Combine(ordner, "picturesorter.db"), pfad);
        }
        finally
        {
            if (Directory.Exists(ordner)) { Directory.Delete(ordner, recursive: true); }
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void GetDatabasePath_WithoutAFolder_Throws(string ordner)
    {
        _ = Assert.Throws<ArgumentException>(() => DatabasePathProvider.GetDatabasePath(ordner));
    }

    [Fact]
    public void GetDatabasePath_WithNull_Throws()
    {
        _ = Assert.Throws<ArgumentNullException>(() => DatabasePathProvider.GetDatabasePath(null!));
    }
}
