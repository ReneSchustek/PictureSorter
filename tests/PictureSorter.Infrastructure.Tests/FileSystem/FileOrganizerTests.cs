using Microsoft.Extensions.Logging.Abstractions;
using PictureSorter.Core.Entities;
using PictureSorter.Core.Enums;
using PictureSorter.Core.ValueObjects;
using PictureSorter.Infrastructure.FileSystem;

namespace PictureSorter.Infrastructure.Tests.FileSystem;

/// <summary>
/// Integrationstests des Datei-Organizers gegen ein echtes Dateisystem. Schwerpunkt
/// ist der Datenverlust-/Korruptionsschutz: Ein bereits einsortiertes Foto darf bei
/// erneutem Lauf nicht gegen sich selbst ausweichen und fortlaufend umbenannt werden.
/// </summary>
public sealed class FileOrganizerTests : IDisposable
{
    private readonly string _root;
    private readonly FileOrganizer _organizer;

    /// <summary>
    /// Legt ein temporäres Arbeitsverzeichnis an.
    /// </summary>
    public FileOrganizerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "PictureSorterTests", Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(_root);
        _organizer = new FileOrganizer(NullLogger<FileOrganizer>.Instance);
    }

    [Fact]
    public async Task ApplyAsync_MovesFileIntoTargetFolder()
    {
        string source = CreateFile(_root, "urlaub.jpg", "inhalt");
        string targetFolder = Path.Combine(_root, "Urlaub");
        SortProposal proposal = ProposalFor(source, targetFolder);

        string result = await _organizer.ApplyAsync(proposal, dryRun: false, CancellationToken.None);

        Assert.Equal(Path.Combine(targetFolder, "urlaub.jpg"), result);
        Assert.True(File.Exists(result));
        Assert.False(File.Exists(source));
    }

    [Fact]
    public async Task ApplyAsync_WhenPhotoAlreadyInTargetFolder_DoesNotRename()
    {
        // Das Foto liegt bereits in seinem Zielordner (typisch beim erneuten Lauf mit
        // „Unterordner einschließen"). Es darf weder verschoben noch umbenannt werden.
        string targetFolder = Path.Combine(_root, "Urlaub");
        _ = Directory.CreateDirectory(targetFolder);
        string source = CreateFile(targetFolder, "strand.jpg", "inhalt");
        SortProposal proposal = ProposalFor(source, targetFolder);

        string result = await _organizer.ApplyAsync(proposal, dryRun: false, CancellationToken.None);

        Assert.Equal(source, result);
        Assert.True(File.Exists(source));
        _ = Assert.Single(Directory.GetFiles(targetFolder));
        Assert.False(File.Exists(Path.Combine(targetFolder, "strand (1).jpg")));
    }

    [Fact]
    public async Task ApplyAsync_RepeatedRunsOnSettledPhoto_KeepStableName()
    {
        // Regressionstest für die fortschreitende Umbenennung: mehrere Läufe auf dem
        // bereits einsortierten Foto dürfen keine „ (1) (1)"-Ketten erzeugen.
        string targetFolder = Path.Combine(_root, "Urlaub");
        _ = Directory.CreateDirectory(targetFolder);
        string source = CreateFile(targetFolder, "strand.jpg", "inhalt");
        SortProposal proposal = ProposalFor(source, targetFolder);

        for (int run = 0; run < 3; run++)
        {
            string moved = await _organizer.ApplyAsync(proposal, dryRun: false, CancellationToken.None);
            Assert.Equal(source, moved);
        }

        string[] files = Directory.GetFiles(targetFolder);
        _ = Assert.Single(files);
        Assert.Equal("strand.jpg", Path.GetFileName(files[0]));
    }

    [Fact]
    public async Task ApplyAsync_WhenDifferentFileOccupiesName_AppendsSuffix()
    {
        // Echte Kollision mit einer ANDEREN Datei: hier ist das Ausweichen auf
        // „ (1)" korrekt und darf durch den Selbst-Guard nicht verhindert werden.
        string targetFolder = Path.Combine(_root, "Urlaub");
        _ = Directory.CreateDirectory(targetFolder);
        _ = CreateFile(targetFolder, "strand.jpg", "bereits vorhanden");
        string source = CreateFile(_root, "strand.jpg", "neu");
        SortProposal proposal = ProposalFor(source, targetFolder);

        string result = await _organizer.ApplyAsync(proposal, dryRun: false, CancellationToken.None);

        Assert.Equal(Path.Combine(targetFolder, "strand (1).jpg"), result);
        Assert.True(File.Exists(result));
        Assert.Equal("neu", await File.ReadAllTextAsync(result));
        Assert.Equal("bereits vorhanden", await File.ReadAllTextAsync(Path.Combine(targetFolder, "strand.jpg")));
    }

    [Fact]
    public async Task ApplyAsync_DryRun_MovesNothing()
    {
        string source = CreateFile(_root, "urlaub.jpg", "inhalt");
        string targetFolder = Path.Combine(_root, "Urlaub");
        SortProposal proposal = ProposalFor(source, targetFolder);

        string result = await _organizer.ApplyAsync(proposal, dryRun: true, CancellationToken.None);

        Assert.Equal(Path.Combine(targetFolder, "urlaub.jpg"), result);
        Assert.True(File.Exists(source));
        Assert.False(Directory.Exists(targetFolder));
    }

    [Fact]
    public async Task RestoreAsync_MovesTheFileBackToItsOriginalPath()
    {
        string source = CreateFile(_root, "urlaub.jpg", "inhalt");
        string targetFolder = Path.Combine(_root, "Urlaub");
        string target = await _organizer.ApplyAsync(
            ProposalFor(source, targetFolder),
            dryRun: false,
            CancellationToken.None);

        bool restored = await _organizer.RestoreAsync(target, source, CancellationToken.None);

        Assert.True(restored);
        Assert.True(File.Exists(source));
        Assert.False(File.Exists(target));
        Assert.Equal("inhalt", await File.ReadAllTextAsync(source));
    }

    [Fact]
    public async Task RestoreAsync_WhenSomethingIsBackAtTheOriginalPath_DoesNotOverwriteIt()
    {
        // Das ist der Kern der Zusage: Beim Zurückholen darf nichts überschrieben
        // werden. Sonst wäre ausgerechnet das Rückgängig die Stelle, an der die
        // Nutzerin Daten verliert.
        string source = CreateFile(_root, "urlaub.jpg", "das alte Foto");
        string targetFolder = Path.Combine(_root, "Urlaub");
        string target = await _organizer.ApplyAsync(
            ProposalFor(source, targetFolder),
            dryRun: false,
            CancellationToken.None);

        // Inzwischen liegt am Ursprungsort wieder eine – andere – Datei gleichen Namens.
        _ = CreateFile(_root, "urlaub.jpg", "ein neues Foto");

        bool restored = await _organizer.RestoreAsync(target, source, CancellationToken.None);

        Assert.False(restored);
        Assert.Equal("ein neues Foto", await File.ReadAllTextAsync(source));
        Assert.True(File.Exists(target));
    }

    [Fact]
    public async Task RestoreAsync_WhenTheFileIsGone_ReportsFailureInsteadOfGuessing()
    {
        string source = Path.Combine(_root, "urlaub.jpg");
        string target = Path.Combine(_root, "Urlaub", "urlaub.jpg");

        Assert.False(await _organizer.RestoreAsync(target, source, CancellationToken.None));
    }

    [Fact]
    public async Task RemoveFolderIfEmptyAsync_RemovesOnlyTheEmptyFolder()
    {
        string empty = Path.Combine(_root, "Leer");
        _ = Directory.CreateDirectory(empty);
        string filled = Path.Combine(_root, "Voll");
        _ = CreateFile(filled, "foto.jpg", "inhalt");

        await _organizer.RemoveFolderIfEmptyAsync(empty, CancellationToken.None);
        await _organizer.RemoveFolderIfEmptyAsync(filled, CancellationToken.None);

        Assert.False(Directory.Exists(empty));
        Assert.True(Directory.Exists(filled));
    }

    private static string CreateFile(string folder, string name, string content)
    {
        _ = Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static SortProposal ProposalFor(string source, string targetFolder) => new()
    {
        Photo = new Photo { FullPath = source, FileName = Path.GetFileName(source) },
        CategoryName = "Urlaub",
        SourceFolder = Path.GetDirectoryName(source)!,
        TargetFolderPath = targetFolder,
        Confidence = 0.9,
        Method = ClassificationMethod.Embedding,
    };

    /// <summary>
    /// Entfernt das temporäre Arbeitsverzeichnis.
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
