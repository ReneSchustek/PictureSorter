using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;
using PictureSorter.Data.Context;
using PictureSorter.Data.DependencyInjection;

namespace PictureSorter.Data.Tests.Repositories;

/// <summary>
/// Integrationstests des Lauf-Protokolls gegen eine echte SQLite-Datei. Das Protokoll
/// ist die einzige Stelle, an der nach einem Sortierlauf noch steht, wo eine Datei
/// vorher lag – geht es verloren oder kommt es unvollständig zurück, ist der Lauf
/// nicht mehr umkehrbar.
/// </summary>
public sealed class SortJournalRepositoryTests : IAsyncLifetime
{
    private const string SourceFolder = @"C:\Fotos";
    private const string Category = "Urlaub";

    private readonly string _dataDirectory =
        Path.Combine(Path.GetTempPath(), "PictureSorterTests", Guid.NewGuid().ToString("N"));

    private ServiceProvider _provider = null!;
    private ISortJournal _sut = null!;

    public async ValueTask InitializeAsync()
    {
        ServiceCollection services = new();
        _ = services.AddLogging();
        _ = services.AddPictureSorterData(_dataDirectory);
        _provider = services.BuildServiceProvider();

        // Schema anlegen – wie beim App-Start. Damit läuft auch die neue Migration.
        bool ready = await _provider.GetRequiredService<DatabaseInitializer>()
            .InitializeAsync(CancellationToken.None)
            .ConfigureAwait(false);
        Assert.True(ready, "Die Testdatenbank konnte nicht initialisiert werden.");

        _sut = _provider.GetRequiredService<ISortJournal>();
    }

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync().ConfigureAwait(false);

        // SQLite hält die Datei über den Verbindungs-Pool offen; ohne das Leeren
        // bleibt die Datenbankdatei gesperrt und das Testverzeichnis unlöschbar.
        SqliteConnection.ClearAllPools();
        Directory.Delete(_dataDirectory, recursive: true);
    }

    [Fact]
    public async Task Record_ThenGetLastUndoable_ReturnsTheRunWithEveryMove()
    {
        SortRun run = CreateRun("a.jpg", "b.jpg");

        await _sut.RecordAsync(run, CancellationToken.None);
        SortRun? loaded = await _sut.GetLastUndoableAsync(CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(run.Id, loaded.Id);
        Assert.Equal(SourceFolder, loaded.SourceFolder);
        Assert.Equal(Category, loaded.CategoryName);
        Assert.Equal(2, loaded.Items.Count);

        SortRunItem first = loaded.Items[0];
        Assert.Equal(Path.Combine(SourceFolder, "a.jpg"), first.SourcePath);
        Assert.Equal(Path.Combine(SourceFolder, Category, "a.jpg"), first.TargetPath);
        Assert.Equal("sig-a", first.FileSignature);
    }

    [Fact]
    public async Task GetLastUndoable_WithSeveralRuns_ReturnsTheMostRecentOne()
    {
        await _sut.RecordAsync(CreateRun("alt.jpg") with { StartedAt = Stamp(hour: 8) }, CancellationToken.None);
        SortRun newest = CreateRun("neu.jpg") with { StartedAt = Stamp(hour: 10) };
        await _sut.RecordAsync(newest, CancellationToken.None);

        SortRun? loaded = await _sut.GetLastUndoableAsync(CancellationToken.None);

        Assert.Equal(newest.Id, loaded!.Id);
    }

    [Fact]
    public async Task GetLastUndoable_AfterMarkUndone_FallsBackToThePreviousRun()
    {
        // Ein zurückgenommener Lauf darf nicht erneut angeboten werden – sonst
        // versuchte die Anwendung, dieselben Dateien ein zweites Mal zurückzuholen.
        SortRun older = CreateRun("alt.jpg") with { StartedAt = Stamp(hour: 8) };
        SortRun newer = CreateRun("neu.jpg") with { StartedAt = Stamp(hour: 10) };
        await _sut.RecordAsync(older, CancellationToken.None);
        await _sut.RecordAsync(newer, CancellationToken.None);

        await _sut.MarkUndoneAsync(newer.Id, CancellationToken.None);

        SortRun? loaded = await _sut.GetLastUndoableAsync(CancellationToken.None);
        Assert.Equal(older.Id, loaded!.Id);
    }

    [Fact]
    public async Task GetLastUndoable_WhenEverythingIsUndone_ReturnsNull()
    {
        SortRun run = CreateRun("a.jpg");
        await _sut.RecordAsync(run, CancellationToken.None);

        await _sut.MarkUndoneAsync(run.Id, CancellationToken.None);

        Assert.Null(await _sut.GetLastUndoableAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Record_WithoutAnyMove_StoresNothing()
    {
        // Ein Lauf, in dem sich keine Datei bewegt hat, ist nichts, was man
        // zurücknehmen könnte – er darf den Hinweis nicht auslösen.
        await _sut.RecordAsync(CreateRun(), CancellationToken.None);

        Assert.Null(await _sut.GetLastUndoableAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GetLastUndoable_WithoutAnyRun_ReturnsNull() =>
        Assert.Null(await _sut.GetLastUndoableAsync(CancellationToken.None));

    private static DateTimeOffset Stamp(int hour) => new(2026, 7, 1, hour, 0, 0, TimeSpan.Zero);

    private static SortRun CreateRun(params string[] fileNames) => new()
    {
        Id = Guid.NewGuid(),
        StartedAt = Stamp(hour: 9),
        SourceFolder = SourceFolder,
        CategoryName = Category,
        Items =
        [
            .. fileNames.Select(name => new SortRunItem
            {
                SourcePath = Path.Combine(SourceFolder, name),
                TargetPath = Path.Combine(SourceFolder, Category, name),
                FileSignature = $"sig-{Path.GetFileNameWithoutExtension(name)}",
            }),
        ],
    };
}
