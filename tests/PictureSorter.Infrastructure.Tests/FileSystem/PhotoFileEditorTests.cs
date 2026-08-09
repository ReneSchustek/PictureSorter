using Microsoft.Extensions.Logging.Abstractions;
using PictureSorter.Core.Enums;
using PictureSorter.Core.ValueObjects;
using PictureSorter.Infrastructure.FileSystem;

namespace PictureSorter.Infrastructure.Tests.FileSystem;

/// <summary>
/// Tests der Bearbeitung einzelner Bilddateien: umbenennen und verschieben.
/// </summary>
public sealed class PhotoFileEditorTests : IDisposable
{
    private readonly string _root;
    private readonly PhotoFileEditor _editor;

    /// <summary>
    /// Legt einen eigenen Ordner für den Testlauf an.
    /// </summary>
    public PhotoFileEditorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "PictureSorterTests", Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(_root);
        _editor = new PhotoFileEditor(NullLogger<PhotoFileEditor>.Instance);
    }

    [Fact]
    public async Task Rename_GivesTheFileItsNewNameAndKeepsTheFolder()
    {
        string file = CreateFile("alt.jpg");

        FileEditResult result = await _editor.RenameAsync(file, "urlaub.jpg", TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal(Path.Combine(_root, "urlaub.jpg"), result.Path);
        Assert.True(File.Exists(result.Path));
        Assert.False(File.Exists(file));
    }

    [Fact]
    public async Task Rename_WithoutExtension_KeepsTheOldOne()
    {
        // Ohne Endung öffnet ein Doppelklick das Bild nicht mehr — und niemand denkt
        // beim Umbenennen daran, „.jpg" mitzutippen.
        string file = CreateFile("alt.jpg");

        FileEditResult result = await _editor.RenameAsync(file, "urlaub", TestContext.Current.CancellationToken);

        Assert.Equal(Path.Combine(_root, "urlaub.jpg"), result.Path);
    }

    [Fact]
    public async Task Rename_ToAnExistingName_IsRefusedAndSaysWhy()
    {
        string file = CreateFile("alt.jpg");
        _ = CreateFile("belegt.jpg");

        FileEditResult result = await _editor.RenameAsync(file, "belegt.jpg", TestContext.Current.CancellationToken);

        Assert.Equal(FileEditOutcome.NameTaken, result.Outcome);
        Assert.True(File.Exists(file));
    }

    [Fact]
    public async Task Rename_ChangingOnlyTheCase_IsAllowed()
    {
        // Windows „findet" die Datei am Ziel, weil es die Schreibweise ignoriert. Ohne
        // Ausnahme ließe sich „foto.jpg" nie in „Foto.jpg" ändern.
        string file = CreateFile("foto.jpg");

        FileEditResult result = await _editor.RenameAsync(file, "Foto.jpg", TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal("Foto.jpg", Path.GetFileName(result.Path));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("neu?.jpg")]
    [InlineData("...")]
    public async Task Rename_WithAnUnusableName_IsRefused(string name)
    {
        string file = CreateFile("alt.jpg");

        FileEditResult result = await _editor.RenameAsync(file, name, TestContext.Current.CancellationToken);

        Assert.Equal(FileEditOutcome.NameInvalid, result.Outcome);
        Assert.True(File.Exists(file));
    }

    [Fact]
    public async Task Rename_WithAPathInTheName_IsRefused()
    {
        // Ein Pfad im Namensfeld wäre ein stilles Verschieben, womöglich aus dem Ordner
        // heraus. Umbenennen heißt umbenennen.
        string file = CreateFile("alt.jpg");

        FileEditResult result = await _editor.RenameAsync(
            file, Path.Combine("..", "woanders.jpg"), TestContext.Current.CancellationToken);

        Assert.Equal(FileEditOutcome.NameInvalid, result.Outcome);
    }

    [Fact]
    public async Task Rename_WhenTheFileIsGone_SaysSo()
    {
        FileEditResult result = await _editor.RenameAsync(
            Path.Combine(_root, "weg.jpg"), "neu.jpg", TestContext.Current.CancellationToken);

        Assert.Equal(FileEditOutcome.SourceMissing, result.Outcome);
    }

    [Fact]
    public async Task Move_TakesTheFileToTheNewFolderAndCreatesIt()
    {
        string file = CreateFile("bild.jpg");
        string target = Path.Combine(_root, "2021-07");

        FileEditResult result = await _editor.MoveAsync(file, target, TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal(Path.Combine(target, "bild.jpg"), result.Path);
        Assert.True(File.Exists(result.Path));
    }

    [Fact]
    public async Task Move_IntoItsOwnFolder_ChangesNothingAndIsNoError()
    {
        string file = CreateFile("bild.jpg");

        FileEditResult result = await _editor.MoveAsync(file, _root, TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal(file, result.Path);
        Assert.True(File.Exists(file));
    }

    [Fact]
    public async Task Move_WhereTheNameIsTaken_IsRefusedAndLeavesBothFiles()
    {
        string file = CreateFile("bild.jpg");
        string target = Path.Combine(_root, "ziel");
        _ = Directory.CreateDirectory(target);
        await File.WriteAllTextAsync(
            Path.Combine(target, "bild.jpg"), "schon da", TestContext.Current.CancellationToken);

        FileEditResult result = await _editor.MoveAsync(file, target, TestContext.Current.CancellationToken);

        Assert.Equal(FileEditOutcome.NameTaken, result.Outcome);
        Assert.True(File.Exists(file));
        Assert.Equal(
            "schon da",
            await File.ReadAllTextAsync(Path.Combine(target, "bild.jpg"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Move_WhenTheFileIsOpenElsewhere_SaysItIsLocked()
    {
        // Der häufigste Fall im Alltag: Die Bildanzeige hält die Datei noch offen.
        string file = CreateFile("bild.jpg");
        string target = Path.Combine(_root, "ziel");

        using (FileStream _ = new(file, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            FileEditResult result = await _editor.MoveAsync(file, target, TestContext.Current.CancellationToken);

            Assert.Equal(FileEditOutcome.Locked, result.Outcome);
        }

        Assert.True(File.Exists(file));
    }

    [Fact]
    public async Task Move_WhereTheTargetFolderCannotBeCreated_SaysSo()
    {
        // Der Zielordner trägt den Namen einer bestehenden Datei: Windows legt ihn nicht
        // an, und die Bilddatei bleibt unangetastet.
        string file = CreateFile("bild.jpg");
        string blocker = CreateFile("ziel");

        FileEditResult result = await _editor.MoveAsync(file, blocker, TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.True(File.Exists(file));
    }

    private string CreateFile(string name)
    {
        string path = Path.Combine(_root, name);
        File.WriteAllText(path, "bild");
        return path;
    }

    /// <summary>
    /// Räumt den Testordner ab.
    /// </summary>
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // Ein Rest im Temp-Ordner ist kein Grund, den Testlauf rot zu machen.
        }
    }
}
