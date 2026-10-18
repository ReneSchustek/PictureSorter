using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;
using PictureSorter.Data.Context;
using PictureSorter.Data.DependencyInjection;

namespace PictureSorter.Data.Tests.Repositories;

/// <summary>
/// Integrationstests des Umbenennungs-Protokolls gegen eine echte SQLite-Datei.
/// Es ist die einzige Stelle, an der nach einem Lauf noch steht, wie eine Datei
/// vorher hieß. Geht es verloren, ist der alte Name unwiederbringlich fort.
/// </summary>
public sealed class RenameJournalRepositoryTests : IAsyncLifetime
{
    private const string Folder = @"C:\Fotos";

    private readonly string _dataDirectory =
        Path.Combine(Path.GetTempPath(), "PictureSorterTests", Guid.NewGuid().ToString("N"));

    private ServiceProvider _provider = null!;
    private IRenameJournal _sut = null!;

    public async ValueTask InitializeAsync()
    {
        ServiceCollection services = new();
        _ = services.AddLogging();
        _ = services.AddPictureSorterData(_dataDirectory);
        _provider = services.BuildServiceProvider();

        // Schema anlegen, wie beim App-Start. Damit läuft auch die neue Migration.
        bool ready = await _provider.GetRequiredService<DatabaseInitializer>()
            .InitializeAsync(CancellationToken.None)
            .ConfigureAwait(false);
        Assert.True(ready, "Die Testdatenbank konnte nicht initialisiert werden.");

        _sut = _provider.GetRequiredService<IRenameJournal>();
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
    public async Task Record_ThenGetLastUndoable_ReturnsBothNamesOfEveryFile()
    {
        RenameRun run = CreateRun(("kaputt-a.jpg", "2021-07-14_19-42-03.jpg"), ("kaputt-b.jpg", "Fotos 001.jpg"));

        await _sut.RecordAsync(run, CancellationToken.None);
        RenameRun? loaded = await _sut.GetLastUndoableAsync(CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(run.Id, loaded.Id);
        Assert.Equal(Folder, loaded.Folder);
        Assert.Equal(2, loaded.Items.Count);
        Assert.Equal(Path.Combine(Folder, "kaputt-a.jpg"), loaded.Items[0].OriginalPath);
        Assert.Equal(Path.Combine(Folder, "2021-07-14_19-42-03.jpg"), loaded.Items[0].NewPath);
    }

    [Fact]
    public async Task GetLastUndoable_WithoutAnyRunAnswersWithNothing()
    {
        Assert.Null(await _sut.GetLastUndoableAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Record_WithoutItemsWritesNothing()
    {
        // Ein leerer Eintrag verdeckte im Angebot den letzten Lauf, der wirklich etwas
        // geändert hat.
        await _sut.RecordAsync(
            new RenameRun
            {
                Id = Guid.NewGuid(),
                StartedAt = DateTimeOffset.UtcNow,
                Folder = Folder,
                Items = [],
            },
            CancellationToken.None);

        Assert.Null(await _sut.GetLastUndoableAsync(CancellationToken.None));
    }

    [Fact]
    public async Task MarkUndone_TakesTheRunOutOfTheOffer()
    {
        RenameRun run = CreateRun(("kaputt.jpg", "neu.jpg"));
        await _sut.RecordAsync(run, CancellationToken.None);

        await _sut.MarkUndoneAsync(run.Id, CancellationToken.None);

        Assert.Null(await _sut.GetLastUndoableAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GetLastUndoable_ReturnsTheYoungestRun()
    {
        RenameRun older = CreateRun(DateTimeOffset.UtcNow.AddHours(-2), ("alt.jpg", "alt-neu.jpg"));
        RenameRun younger = CreateRun(DateTimeOffset.UtcNow, ("jung.jpg", "jung-neu.jpg"));
        await _sut.RecordAsync(older, CancellationToken.None);
        await _sut.RecordAsync(younger, CancellationToken.None);

        RenameRun? loaded = await _sut.GetLastUndoableAsync(CancellationToken.None);

        Assert.Equal(younger.Id, loaded!.Id);
    }

    [Fact]
    public async Task GetLastUndoable_AfterUndoingTheYoungestOffersTheOneBefore()
    {
        RenameRun older = CreateRun(DateTimeOffset.UtcNow.AddHours(-2), ("alt.jpg", "alt-neu.jpg"));
        RenameRun younger = CreateRun(DateTimeOffset.UtcNow, ("jung.jpg", "jung-neu.jpg"));
        await _sut.RecordAsync(older, CancellationToken.None);
        await _sut.RecordAsync(younger, CancellationToken.None);

        await _sut.MarkUndoneAsync(younger.Id, CancellationToken.None);

        Assert.Equal(older.Id, (await _sut.GetLastUndoableAsync(CancellationToken.None))!.Id);
    }

    private static RenameRun CreateRun(params (string Original, string New)[] items) =>
        CreateRun(DateTimeOffset.UtcNow, items);

    private static RenameRun CreateRun(DateTimeOffset startedAt, params (string Original, string New)[] items) => new()
    {
        Id = Guid.NewGuid(),
        StartedAt = startedAt,
        Folder = Folder,
        Items =
        [
            .. items.Select(item => new RenameRunItem
            {
                OriginalPath = Path.Combine(Folder, item.Original),
                NewPath = Path.Combine(Folder, item.New),
            }),
        ],
    };
}
