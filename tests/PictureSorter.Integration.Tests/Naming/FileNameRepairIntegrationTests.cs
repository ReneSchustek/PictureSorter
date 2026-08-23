using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PictureSorter.Application.Naming;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;
using PictureSorter.Data.Context;
using PictureSorter.Data.DependencyInjection;
using PictureSorter.Infrastructure.FileSystem;
using PictureSorter.Infrastructure.Time;

namespace PictureSorter.Integration.Tests.Naming;

/// <summary>
/// Die Namensreparatur an echten Dateien und einer echten SQLite-Datenbank.
///
/// Hier zählt, was die Bausteine allein nicht zeigen können: dass die Datei nach dem
/// Lauf wirklich anders heißt, dass der alte Name im Protokoll steht und dass er
/// zurückkommt. Der alte Name ist nach dem Umbenennen sonst nirgends mehr zu finden —
/// wäre das Protokoll falsch, wäre er unwiederbringlich fort.
/// </summary>
public sealed class FileNameRepairIntegrationTests : IAsyncLifetime
{
    // Das Ersatzzeichen steht hier als Code und nicht als Zeichen: Als Zeichen
    // geschrieben überlebt es den Weg durch Editor und Zwischenablage nicht, und der
    // Test prüfte am Ende einen ganz gewöhnlichen Namen.
    private const string BrokenName = "\uFFFD4\uFFFDx___.jpg";
    private const string ReadableName = "IMG_2041.jpg";

    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "PictureSorterTests", Guid.NewGuid().ToString("N"));

    private string _photoFolder = null!;
    private ServiceProvider _provider = null!;
    private FileNameRepairService _repair = null!;
    private RenameUndoService _undo = null!;

    public async ValueTask InitializeAsync()
    {
        _photoFolder = Path.Combine(_root, "Fotos");
        _ = Directory.CreateDirectory(_photoFolder);

        ServiceCollection services = new();
        _ = services.AddLogging();
        _ = services.AddPictureSorterData(_root);
        _provider = services.BuildServiceProvider();

        _ = await _provider.GetRequiredService<DatabaseInitializer>()
            .InitializeAsync(CancellationToken.None)
            .ConfigureAwait(false);

        FileSystemPhotoSource source = new(
            new NoMetadataReader(),
            Options.Create(new PhotoSourceOptions()),
            NullLogger<FileSystemPhotoSource>.Instance);
        PhotoFileEditor editor = new(NullLogger<PhotoFileEditor>.Instance);
        IRenameJournal journal = _provider.GetRequiredService<IRenameJournal>();

        _repair = new FileNameRepairService(
            source,
            new NoMetadataReader(),
            editor,
            journal,
            new SystemClock(),
            NullLogger<FileNameRepairService>.Instance);
        _undo = new RenameUndoService(journal, editor, NullLogger<RenameUndoService>.Instance);
    }

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync().ConfigureAwait(false);

        // Ohne das Leeren der Verbindungspools hält SQLite die Datei weiter offen, und
        // das Aufräumen scheitert mit „wird von einem anderen Prozess verwendet".
        SqliteConnection.ClearAllPools();
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task ProposeApplyUndo_TheFileGetsAReadableNameAndItsOldOneBack()
    {
        WriteFile(BrokenName);
        WriteFile(ReadableName);

        IReadOnlyList<RenameProposal> proposals = await _repair
            .ProposeAsync(_photoFolder, includeSubfolders: false, progress: null, TestContext.Current.CancellationToken);

        // Nur die kaputte Datei steht in der Vorschau; die lesbare bleibt unbehelligt.
        RenameProposal proposal = Assert.Single(proposals);
        Assert.Equal(BrokenName, proposal.CurrentName);
        Assert.Equal("Fotos 001.jpg", proposal.SuggestedName);
        Assert.True(File.Exists(Path.Combine(_photoFolder, BrokenName)), "Die Vorschau darf nichts verändern.");

        RenameResult applied = await _repair
            .ApplyAsync(_photoFolder, proposals, progress: null, TestContext.Current.CancellationToken);

        Assert.Equal(1, applied.Renamed);
        Assert.True(File.Exists(Path.Combine(_photoFolder, "Fotos 001.jpg")));
        Assert.False(File.Exists(Path.Combine(_photoFolder, BrokenName)));
        Assert.True(File.Exists(Path.Combine(_photoFolder, ReadableName)), "Die lesbare Datei bleibt, wie sie war.");

        UndoResult? undone = await _undo.UndoLastRunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, undone!.Restored);
        Assert.True(File.Exists(Path.Combine(_photoFolder, BrokenName)), "Der alte Name muss zurückkommen.");
        Assert.False(File.Exists(Path.Combine(_photoFolder, "Fotos 001.jpg")));
    }

    [Fact]
    public async Task Undo_SurvivesARestart()
    {
        // Das Protokoll liegt in der Datenbank und nicht im Speicher: Wer den Lauf erst
        // am nächsten Tag bereut, muss ihn trotzdem zurücknehmen können.
        WriteFile(BrokenName);

        IReadOnlyList<RenameProposal> proposals = await _repair
            .ProposeAsync(_photoFolder, includeSubfolders: false, progress: null, TestContext.Current.CancellationToken);
        _ = await _repair.ApplyAsync(_photoFolder, proposals, progress: null, TestContext.Current.CancellationToken);

        // Ein zweiter Satz Dienste auf derselben Datenbank steht für den Neustart.
        IRenameJournal journal = _provider.GetRequiredService<IRenameJournal>();
        RenameUndoService nachNeustart = new(
            journal,
            new PhotoFileEditor(NullLogger<PhotoFileEditor>.Instance),
            NullLogger<RenameUndoService>.Instance);

        RenameRun? run = await nachNeustart.GetUndoableRunAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(run);

        UndoResult? undone = await nachNeustart.UndoLastRunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, undone!.Restored);
        Assert.True(File.Exists(Path.Combine(_photoFolder, BrokenName)));
    }

    [Fact]
    public async Task Apply_WhenTheNameIsAlreadyTakenTheFileKeepsItsOwn()
    {
        // Überschrieben wird nie: Liegt der Zielname schon da, bleibt die Datei, wie
        // sie ist, und der Lauf zählt sie als übersprungen.
        WriteFile(BrokenName);
        WriteFile("Fotos 001.jpg");

        IReadOnlyList<RenameProposal> proposals = await _repair
            .ProposeAsync(_photoFolder, includeSubfolders: false, progress: null, TestContext.Current.CancellationToken);

        // Der Vorschlag weicht dem vergebenen Namen bereits aus.
        RenameProposal proposal = Assert.Single(proposals);
        Assert.Equal("Fotos 001-2.jpg", proposal.SuggestedName);

        _ = await _repair.ApplyAsync(_photoFolder, proposals, progress: null, TestContext.Current.CancellationToken);

        Assert.True(File.Exists(Path.Combine(_photoFolder, "Fotos 001.jpg")));
        Assert.True(File.Exists(Path.Combine(_photoFolder, "Fotos 001-2.jpg")));
    }

    private void WriteFile(string name) =>
        File.WriteAllText(Path.Combine(_photoFolder, name), "kein echtes Bild, aber eine echte Datei");

    // Der Dateikopf wird hier nicht gelesen: Die Tests handeln vom Namen, nicht vom
    // Bildinhalt. Ohne Aufnahmedatum greift genau der Weg, der beim gemessenen
    // iCloud-Bestand für alle 159 Dateien galt.
    private sealed class NoMetadataReader : IImageMetadataReader
    {
        public Task<PhotoMetadata?> ReadAsync(string filePath, CancellationToken cancellationToken) =>
            Task.FromResult<PhotoMetadata?>(null);
    }
}
