namespace PictureSorter.App.Tests.Design;

/// <summary>
/// Hält fest, dass die Bildleser ihre Dateien ohne <c>StorageFile</c> öffnen. An einem echten Bestand
/// blieb damit der Aufräum-Thread von .NET stehen: Danach wurde nichts mehr freigegeben, das Programm
/// wuchs auf über 14 GB und starb. In kleinen Tests fällt das nicht auf, es zeigt sich erst nach Tausenden
/// Bildern. Deshalb steht die Regel hier und nicht nur im Gedächtnis.
/// </summary>
public sealed class ImageFileAccessGuardTests
{
    [Fact]
    public void ImageReaders_DoNotOpenFilesThroughStorageFile()
    {
        string[] offenders = [.. Directory
            .EnumerateFiles(Path.Combine(RepositoryRoot(), "src", "PictureSorter.Imaging"), "*.cs", SearchOption.AllDirectories)
            .Where(static file => !IsBuildOutput(file) && File.ReadAllText(file).Contains("StorageFile", StringComparison.Ordinal))
            .Select(static file => Path.GetFileName(file))];

        Assert.True(offenders.Length == 0, "Benutzen StorageFile: " + string.Join(", ", offenders));
    }

    [Fact]
    public void Lists_LoadTheirPreviewsThroughTheThumbnailLoader()
    {
        // Ein BitmapImage mit Dateipfad je Zeile dekodiert ohne Grenze und ohne Abbruch. Erlaubt ist es nur dort,
        // wo genau ein Bild groß gezeigt wird.
        string[] allowed = ["PhotoPreviewDialog.xaml.cs", "PhotoZoomWindow.xaml.cs"];
        string[] offenders = [.. Directory
            .EnumerateFiles(Path.Combine(RepositoryRoot(), "src", "PictureSorter.App"), "*.cs", SearchOption.AllDirectories)
            .Where(static file => !IsBuildOutput(file) && File.ReadAllText(file).Contains("new BitmapImage(new Uri", StringComparison.Ordinal))
            .Select(static file => Path.GetFileName(file))
            .Where(file => !allowed.Contains(file))];

        Assert.True(offenders.Length == 0, "Laden Bilder am Lader vorbei: " + string.Join(", ", offenders));
    }

    private static bool IsBuildOutput(string file) =>
        file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

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
}
