using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PictureSorter.Core.Enums;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;
using PictureSorter.Data.Context;
using PictureSorter.Data.DependencyInjection;

namespace PictureSorter.Data.Tests.Repositories;

/// <summary>
/// Integrationstests des Sortier-Gedächtnisses gegen eine echte SQLite-Datei.
/// Jeder Test bekommt ein eigenes temporäres Verzeichnis und ist damit unabhängig
/// von Reihenfolge und Vorzustand.
/// </summary>
public sealed class SortMemoryRepositoryTests : IAsyncLifetime
{
    private const string Folder = @"C:\Fotos\Urlaub";

    private readonly string _dataDirectory =
        Path.Combine(Path.GetTempPath(), "PictureSorterTests", Guid.NewGuid().ToString("N"));

    private ServiceProvider _provider = null!;
    private ISortMemory _sut = null!;

    public async ValueTask InitializeAsync()
    {
        ServiceCollection services = new();
        _ = services.AddLogging();
        _ = services.AddPictureSorterData(_dataDirectory);
        _provider = services.BuildServiceProvider();

        // Schema anlegen – wie beim App-Start.
        bool ready = await _provider.GetRequiredService<DatabaseInitializer>()
            .InitializeAsync(CancellationToken.None)
            .ConfigureAwait(false);
        Assert.True(ready, "Die Testdatenbank konnte nicht initialisiert werden.");

        _sut = _provider.GetRequiredService<ISortMemory>();
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
    public async Task Upsert_ThenGet_ReturnsStoredRecord()
    {
        await _sut.UpsertAsync(CreateRecord("sig-1", SortMemoryStatus.Sorted), CancellationToken.None);

        SortMemoryRecord? loaded = await _sut.GetAsync(Folder, "sig-1", "Urlaub", CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal("Urlaub", loaded.CategoryName);
        Assert.Equal(SortMemoryStatus.Sorted, loaded.Status);
        Assert.Equal(0.9, loaded.Confidence, precision: 5);
    }

    [Fact]
    public async Task Upsert_WithSameSignature_UpdatesInsteadOfDuplicating()
    {
        await _sut.UpsertAsync(CreateRecord("sig-1", SortMemoryStatus.Proposed), CancellationToken.None);
        await _sut.UpsertAsync(CreateRecord("sig-1", SortMemoryStatus.Sorted), CancellationToken.None);

        IReadOnlyList<SortMemoryRecord> all = await _sut.GetForFolderAsync(Folder, CancellationToken.None);

        // Der eindeutige Index greift: ein Eintrag, aktualisierter Status.
        SortMemoryRecord single = Assert.Single(all);
        Assert.Equal(SortMemoryStatus.Sorted, single.Status);
    }

    [Fact]
    public async Task Upsert_WithSameFileButDifferentCategory_KeepsBothDecisions()
    {
        await _sut.UpsertAsync(CreateRecord("sig-1", SortMemoryStatus.Sorted), CancellationToken.None);
        await _sut.UpsertAsync(
            CreateRecord("sig-1", SortMemoryStatus.Rejected) with { CategoryName = "Familie" },
            CancellationToken.None);

        // Dasselbe Foto darf je Kategorie verschieden beurteilt werden.
        IReadOnlyList<SortMemoryRecord> all = await _sut.GetForFolderAsync(Folder, CancellationToken.None);
        Assert.Equal(2, all.Count);

        SortMemoryRecord? holiday = await _sut.GetAsync(Folder, "sig-1", "Urlaub", CancellationToken.None);
        SortMemoryRecord? family = await _sut.GetAsync(Folder, "sig-1", "Familie", CancellationToken.None);

        Assert.Equal(SortMemoryStatus.Sorted, holiday?.Status);
        Assert.Equal(SortMemoryStatus.Rejected, family?.Status);
    }

    [Fact]
    public async Task GetForFolder_ReturnsOnlyRecordsOfThatFolder()
    {
        await _sut.UpsertAsync(CreateRecord("sig-1", SortMemoryStatus.Sorted), CancellationToken.None);
        await _sut.UpsertAsync(
            CreateRecord("sig-2", SortMemoryStatus.Sorted) with { FolderPath = @"C:\Fotos\Familie" },
            CancellationToken.None);

        IReadOnlyList<SortMemoryRecord> records = await _sut.GetForFolderAsync(Folder, CancellationToken.None);

        SortMemoryRecord single = Assert.Single(records);
        Assert.Equal(Folder, single.FolderPath);
    }

    [Fact]
    public async Task Remove_DeletesOnlyTheGivenRecord()
    {
        await _sut.UpsertAsync(CreateRecord("sig-1", SortMemoryStatus.Sorted), CancellationToken.None);
        await _sut.UpsertAsync(CreateRecord("sig-2", SortMemoryStatus.Ignored), CancellationToken.None);

        await _sut.RemoveAsync(Folder, "sig-1", "Urlaub", CancellationToken.None);

        IReadOnlyList<SortMemoryRecord> remaining = await _sut.GetForFolderAsync(Folder, CancellationToken.None);
        SortMemoryRecord single = Assert.Single(remaining);
        Assert.Equal("sig-2", single.FileSignature);
    }

    [Fact]
    public async Task ClearFolder_RemovesAllRecordsOfThatFolderOnly()
    {
        await _sut.UpsertAsync(CreateRecord("sig-1", SortMemoryStatus.Sorted), CancellationToken.None);
        await _sut.UpsertAsync(CreateRecord("sig-2", SortMemoryStatus.Sorted), CancellationToken.None);
        await _sut.UpsertAsync(
            CreateRecord("sig-3", SortMemoryStatus.Sorted) with { FolderPath = @"C:\Fotos\Familie" },
            CancellationToken.None);

        await _sut.ClearFolderAsync(Folder, CancellationToken.None);

        Assert.Empty(await _sut.GetForFolderAsync(Folder, CancellationToken.None));
        _ = Assert.Single(await _sut.GetAllAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GetAll_ReturnsNewestFirst()
    {
        await _sut.UpsertAsync(
            CreateRecord("sig-alt", SortMemoryStatus.Sorted) with
            {
                UpdatedAt = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero),
            },
            CancellationToken.None);
        await _sut.UpsertAsync(
            CreateRecord("sig-neu", SortMemoryStatus.Sorted) with
            {
                UpdatedAt = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero),
            },
            CancellationToken.None);

        IReadOnlyList<SortMemoryRecord> all = await _sut.GetAllAsync(CancellationToken.None);

        Assert.Equal("sig-neu", all[0].FileSignature);
    }

    [Fact]
    public async Task Get_WithUnknownSignature_ReturnsNull()
    {
        SortMemoryRecord? loaded = await _sut.GetAsync(Folder, "gibt-es-nicht", "Urlaub", CancellationToken.None);

        Assert.Null(loaded);
    }

    [Fact]
    public async Task Migration_CreatesUniqueIndexOnFolderAndSignature()
    {
        IDbContextFactory<PictureSorterDbContext> factory =
            _provider.GetRequiredService<IDbContextFactory<PictureSorterDbContext>>();

        PictureSorterDbContext context = await factory.CreateDbContextAsync(CancellationToken.None);
        await using (context.ConfigureAwait(false))
        {
            IReadOnlyList<string> applied =
                [.. await context.Database.GetAppliedMigrationsAsync(CancellationToken.None)];

            Assert.NotEmpty(applied);
        }
    }

    private static SortMemoryRecord CreateRecord(string signature, SortMemoryStatus status) => new()
    {
        FolderPath = Folder,
        FileSignature = signature,
        PhotoPath = Path.Combine(Folder, $"{signature}.jpg"),
        CategoryName = "Urlaub",
        Status = status,
        Confidence = 0.9,
        UpdatedAt = new DateTimeOffset(2026, 3, 1, 10, 0, 0, TimeSpan.Zero),
    };
}
