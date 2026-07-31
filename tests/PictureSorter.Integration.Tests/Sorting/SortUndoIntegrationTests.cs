using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PictureSorter.Application.Sorting;
using PictureSorter.Core.Enums;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;
using PictureSorter.Data.Context;
using PictureSorter.Data.DependencyInjection;
using PictureSorter.Infrastructure.FileSystem;
using PictureSorter.Infrastructure.Time;

namespace PictureSorter.Integration.Tests.Sorting;

/// <summary>
/// Das Rückgängigmachen von Anfang bis Ende: echte Dateien auf der Platte, echte
/// SQLite-Datenbank, echter Datei-Organizer – nur die Oberfläche fehlt. Die
/// Einzelteile sind je für sich getestet; dieser Test prüft, dass sie zusammen
/// tatsächlich das tun, was der Nutzerin versprochen wird: Die Fotos liegen danach
/// wieder da, wo sie waren, und die Anwendung hat vergessen, dass sie einsortiert
/// waren.
/// </summary>
public sealed class SortUndoIntegrationTests : IAsyncLifetime
{
    private const string Category = "Urlaub";

    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "PictureSorterTests", Guid.NewGuid().ToString("N"));

    private string _photoFolder = null!;
    private string _categoryFolder = null!;
    private ServiceProvider _provider = null!;
    private SortJournalGateway _journal = null!;
    private SortMemoryGateway _memory = null!;
    private ISortMemory _rawMemory = null!;
    private SortUndoService _undo = null!;
    private FileOrganizer _organizer = null!;

    public async Task InitializeAsync()
    {
        _photoFolder = Path.Combine(_root, "Fotos");
        _categoryFolder = Path.Combine(_photoFolder, Category);
        _ = Directory.CreateDirectory(_photoFolder);

        ServiceCollection services = new();
        _ = services.AddLogging();
        _ = services.AddPictureSorterData(Path.Combine(_root, "daten"));
        _provider = services.BuildServiceProvider();

        bool ready = await _provider.GetRequiredService<DatabaseInitializer>()
            .InitializeAsync(CancellationToken.None)
            .ConfigureAwait(false);
        Assert.True(ready, "Die Testdatenbank konnte nicht initialisiert werden.");

        _rawMemory = _provider.GetRequiredService<ISortMemory>();
        _organizer = new FileOrganizer(NullLogger<FileOrganizer>.Instance);
        _journal = new SortJournalGateway(
            _provider.GetRequiredService<ISortJournal>(),
            NullLogger<SortJournalGateway>.Instance);
        _memory = new SortMemoryGateway(_rawMemory, new SystemClock(), NullLogger<SortMemoryGateway>.Instance);
        _undo = new SortUndoService(_journal, _memory, _organizer, NullLogger<SortUndoService>.Instance);
    }

    public async Task DisposeAsync()
    {
        await _provider.DisposeAsync().ConfigureAwait(false);
        SqliteConnection.ClearAllPools();
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task SortThenUndo_PutsEveryPhotoBackAndForgetsTheSorting()
    {
        SortRunItem[] moved = await SortAsync("a.jpg", "b.jpg");
        Assert.All(moved, item => Assert.True(File.Exists(item.TargetPath)));
        Assert.All(moved, item => Assert.False(File.Exists(item.SourcePath)));

        UndoResult? result = await _undo.UndoLastRunAsync(CancellationToken.None);

        Assert.Equal(2, result!.Restored);
        Assert.Equal(0, result.Skipped);

        // Die Fotos liegen wieder im Quellordner, mit unverändertem Inhalt.
        foreach (SortRunItem item in moved)
        {
            Assert.True(File.Exists(item.SourcePath));
            Assert.False(File.Exists(item.TargetPath));
            Assert.Equal(Path.GetFileName(item.SourcePath), await File.ReadAllTextAsync(item.SourcePath));
        }

        // Der leere Kategorie-Ordner ist verschwunden.
        Assert.False(Directory.Exists(_categoryFolder));

        // Und die Anwendung merkt sich nicht länger, dass sie einsortiert waren –
        // sonst würden sie nie wieder vorgeschlagen.
        Assert.Empty(await _rawMemory.GetAllAsync(CancellationToken.None));

        // Ein zweites Rückgängig gibt es nicht.
        Assert.Null(await _undo.GetUndoableRunAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Undo_WhenTheOriginalPlaceIsTakenAgain_KeepsBothFiles()
    {
        // Der Ernstfall: Nach dem Sortieren landet unter demselben Namen wieder ein
        // Foto im Quellordner – etwa vom Handy nachgeladen. Das Zurückholen darf es
        // nicht überschreiben.
        SortRunItem[] moved = await SortAsync("a.jpg");
        await File.WriteAllTextAsync(moved[0].SourcePath, "ein anderes Foto");

        UndoResult? result = await _undo.UndoLastRunAsync(CancellationToken.None);

        Assert.Equal(0, result!.Restored);
        Assert.Equal(1, result.Skipped);
        Assert.Equal("ein anderes Foto", await File.ReadAllTextAsync(moved[0].SourcePath));
        Assert.True(File.Exists(moved[0].TargetPath));

        // Das sortierte Foto liegt weiter im Kategorie-Ordner – also gilt es auch
        // weiterhin als einsortiert.
        _ = Assert.Single(await _rawMemory.GetAllAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Undo_SurvivesARestart()
    {
        // Das Protokoll liegt in der Datenbank, nicht im Arbeitsspeicher: Ein Lauf von
        // gestern muss sich heute noch zurücknehmen lassen.
        SortRunItem[] moved = await SortAsync("a.jpg");

        // Neue Dienste auf derselben Datenbank – wie nach einem Neustart der Anwendung.
        SortUndoService afterRestart = new(
            new SortJournalGateway(
                _provider.GetRequiredService<ISortJournal>(),
                NullLogger<SortJournalGateway>.Instance),
            _memory,
            _organizer,
            NullLogger<SortUndoService>.Instance);

        UndoResult? result = await afterRestart.UndoLastRunAsync(CancellationToken.None);

        Assert.Equal(1, result!.Restored);
        Assert.True(File.Exists(moved[0].SourcePath));
    }

    // Verschiebt Fotos wie ein echter Sortierlauf: über den Datei-Organizer, mit
    // Eintrag im Gedächtnis und im Lauf-Protokoll.
    private async Task<SortRunItem[]> SortAsync(params string[] fileNames)
    {
        List<SortRunItem> moved = [];
        foreach (string name in fileNames)
        {
            string sourcePath = Path.Combine(_photoFolder, name);
            await File.WriteAllTextAsync(sourcePath, name).ConfigureAwait(false);

            SortProposal proposal = new()
            {
                Photo = new Core.Entities.Photo { FullPath = sourcePath, FileName = name },
                CategoryName = Category,
                SourceFolder = _photoFolder,
                TargetFolderPath = _categoryFolder,
                Confidence = 1.0,
                Method = Core.Enums.ClassificationMethod.Embedding,
            };

            string targetPath = await _organizer
                .ApplyAsync(proposal, FileOperationMode.Move, dryRun: false, CancellationToken.None)
                .ConfigureAwait(false);
            await _memory.MarkSortedAsync(proposal, CancellationToken.None).ConfigureAwait(false);

            moved.Add(new SortRunItem
            {
                SourcePath = sourcePath,
                TargetPath = targetPath,
                FileSignature = proposal.Photo.ComputeSignature(),
            });
        }

        await _journal.RecordAsync(
            new SortRun
            {
                Id = Guid.NewGuid(),
                StartedAt = DateTimeOffset.UtcNow,
                SourceFolder = _photoFolder,
                CategoryName = Category,
                Items = moved,
            },
            CancellationToken.None).ConfigureAwait(false);

        return [.. moved];
    }
}
