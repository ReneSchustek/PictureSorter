using Microsoft.Extensions.Logging.Abstractions;
using PictureSorter.Infrastructure.FileSystem;

namespace PictureSorter.Infrastructure.Tests.FileSystem;

/// <summary>
/// Tests des Löschers. Er ist die einzige Stelle, an der die Anwendung Fotos der
/// Nutzerin entfernt – und die Zusage lautet, dass nichts unwiederbringlich
/// verschwindet: gelöscht wird in den Papierkorb, nicht endgültig. Die Tests löschen
/// deshalb echte Dateien; ein Attrappen-Test würde genau die Eigenschaft nicht
/// prüfen, auf die es ankommt.
/// </summary>
public sealed class RecycleBinFileDeleterTests : IDisposable
{
    private readonly string _root;
    private readonly RecycleBinFileDeleter _sut = new(NullLogger<RecycleBinFileDeleter>.Instance);

    public RecycleBinFileDeleterTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "PictureSorterTests", Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task DeleteAsync_RemovesTheFileFromItsFolder()
    {
        string path = CreateFile("duplikat.jpg");

        await _sut.DeleteAsync(path, CancellationToken.None);

        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task DeleteAsync_LeavesTheOtherFilesUntouched()
    {
        // Der Duplikat-Lauf löscht immer nur einzelne Bilder einer Gruppe; das
        // behaltene Original muss bleiben.
        string doomed = CreateFile("kopie.jpg");
        string keeper = CreateFile("original.jpg");

        await _sut.DeleteAsync(doomed, CancellationToken.None);

        Assert.False(File.Exists(doomed));
        Assert.True(File.Exists(keeper));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task DeleteAsync_WithoutPath_IsRejected(string path) =>
        await Assert.ThrowsAsync<ArgumentException>(() => _sut.DeleteAsync(path, CancellationToken.None));

    [Fact]
    public async Task DeleteAsync_ForMissingFile_Throws()
    {
        // Ein fehlgeschlagenes Löschen darf nicht als Erfolg durchgehen: Das
        // aufrufende ViewModel zählt sonst eine Datei als entfernt, die noch da ist.
        string path = Path.Combine(_root, "gibt-es-nicht.jpg");

        _ = await Assert.ThrowsAsync<FileNotFoundException>(
            () => _sut.DeleteAsync(path, CancellationToken.None));
    }

    private string CreateFile(string name)
    {
        string path = Path.Combine(_root, name);
        File.WriteAllBytes(path, [1, 2, 3, 4]);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
