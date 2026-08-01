using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;
using PictureSorter.Data.Context;
using PictureSorter.Data.DependencyInjection;

namespace PictureSorter.Data.Tests.Context;

/// <summary>
/// Tests der Sicherung vor einer Migration. In der Datenbank stehen das
/// Sortier-Gedächtnis und das Protokoll der Sortierläufe; geht sie bei einer
/// Schemaänderung verloren, ist kein Lauf mehr umkehrbar.
/// </summary>
public sealed class DatabaseBackupTests : IDisposable
{
    private readonly string _dataDirectory =
        Path.Combine(Path.GetTempPath(), "PictureSorterTests", Guid.NewGuid().ToString("N"));

    /// <summary>Legt das Testverzeichnis an.</summary>
    public DatabaseBackupTests() => Directory.CreateDirectory(_dataDirectory);

    /// <summary>Entfernt das Testverzeichnis.</summary>
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dataDirectory))
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
    }

    [Fact]
    public void TryCreate_WithoutDatabaseFile_SucceedsWithoutBackup()
    {
        DatabaseBackup backup = new(NullLogger<DatabaseBackup>.Instance);
        string databasePath = Path.Combine(_dataDirectory, "picturesorter.db");

        bool result = backup.TryCreate(databasePath, "20260101000000_Erste");

        // Beim allerersten Start gibt es noch nichts zu verlieren.
        Assert.True(result);
        Assert.False(File.Exists(DatabaseBackup.BuildBackupPath(databasePath, "20260101000000_Erste")));
    }

    [Fact]
    public void TryCreate_WithExistingDatabase_WritesReadableCopy()
    {
        string databasePath = CreateDatabaseWithOneRow();
        DatabaseBackup backup = new(NullLogger<DatabaseBackup>.Instance);

        bool result = backup.TryCreate(databasePath, "20260202000000_Zweite");

        Assert.True(result);
        string backupPath = DatabaseBackup.BuildBackupPath(databasePath, "20260202000000_Zweite");
        Assert.True(File.Exists(backupPath));
        Assert.Equal(1, CountRows(backupPath));
    }

    [Fact]
    public void TryCreate_WhenBackupAlreadyExists_LeavesItUntouched()
    {
        // Scheitert eine Migration und startet die Anwendung erneut, darf die
        // unbeschädigte Sicherung nicht durch die beschädigte Datenbank ersetzt werden.
        string databasePath = CreateDatabaseWithOneRow();
        DatabaseBackup backup = new(NullLogger<DatabaseBackup>.Instance);
        string backupPath = DatabaseBackup.BuildBackupPath(databasePath, "20260202000000_Zweite");
        File.WriteAllText(backupPath, "ältere Sicherung");

        bool result = backup.TryCreate(databasePath, "20260202000000_Zweite");

        Assert.True(result);
        Assert.Equal("ältere Sicherung", File.ReadAllText(backupPath));
    }

    [Fact]
    public async Task InitializeAsync_WithPendingMigration_SecuresDataBeforeChangingSchema()
    {
        // Der Weg, den der Anwendungsstart nach einem Programm-Update nimmt: Die
        // Datenbank steht auf einem älteren Schemastand, eine Migration ist fällig.
        // Genau davor – und nur davor – muss eine Sicherung entstehen.
        ServiceCollection services = new();
        _ = services.AddLogging();
        _ = services.AddPictureSorterData(_dataDirectory);
        await using ServiceProvider provider = services.BuildServiceProvider();

        IDbContextFactory<PictureSorterDbContext> factory =
            provider.GetRequiredService<IDbContextFactory<PictureSorterDbContext>>();

        string[] alleMigrationen;
        PictureSorterDbContext context = await factory.CreateDbContextAsync();
        await using (context.ConfigureAwait(true))
        {
            alleMigrationen = [.. context.Database.GetMigrations()];

            // Nur bis zur ersten Migration hochziehen – der Stand einer älteren
            // Programmfassung.
            await context.GetService<IMigrator>().MigrateAsync(alleMigrationen[0]);
        }

        SqliteConnection.ClearAllPools();

        bool ready = await provider.GetRequiredService<DatabaseInitializer>()
            .InitializeAsync(CancellationToken.None);

        Assert.True(ready);
        string erwartet = DatabaseBackup.BuildBackupPath(
            Path.Combine(_dataDirectory, "picturesorter.db"),
            alleMigrationen[1]);
        Assert.True(File.Exists(erwartet), $"Keine Sicherung unter {erwartet}.");

        // Und die Migration ist danach wirklich durch.
        PictureSorterDbContext migriert = await factory.CreateDbContextAsync();
        await using (migriert.ConfigureAwait(true))
        {
            Assert.Empty(await migriert.Database.GetPendingMigrationsAsync());
        }
    }

    [Fact]
    public async Task InitializeAsync_WhenNothingToMigrate_WritesNoFurtherBackup()
    {
        // Ein gewöhnlicher Start darf keine Datei anfassen.
        ServiceCollection services = new();
        _ = services.AddLogging();
        _ = services.AddPictureSorterData(_dataDirectory);
        await using ServiceProvider provider = services.BuildServiceProvider();
        DatabaseInitializer initializer = provider.GetRequiredService<DatabaseInitializer>();

        Assert.True(await initializer.InitializeAsync(CancellationToken.None));
        int afterFirstStart = Directory.GetFiles(_dataDirectory, "*.bak").Length;

        Assert.True(await initializer.InitializeAsync(CancellationToken.None));

        Assert.Equal(afterFirstStart, Directory.GetFiles(_dataDirectory, "*.bak").Length);
    }

    [Fact]
    public async Task InitializeAsync_AfterMigration_KeepsExistingDataIntact()
    {
        // Die Sicherung darf die Nutzdaten nicht ersetzen: Nach dem Start muss das
        // Gedächtnis weiterhin enthalten, was vorher darin stand.
        ServiceCollection services = new();
        _ = services.AddLogging();
        _ = services.AddPictureSorterData(_dataDirectory);
        await using ServiceProvider provider = services.BuildServiceProvider();

        Assert.True(await provider.GetRequiredService<DatabaseInitializer>()
            .InitializeAsync(CancellationToken.None));

        ISortMemory memory = provider.GetRequiredService<ISortMemory>();
        await memory.UpsertAsync(
            new SortMemoryRecord
            {
                FolderPath = @"C:\Fotos",
                FileSignature = "sig",
                PhotoPath = @"C:\Fotos\a.jpg",
                CategoryName = "Urlaub",
                Status = Core.Enums.SortMemoryStatus.Sorted,
                Confidence = 1.0,
                UpdatedAt = DateTimeOffset.UnixEpoch,
            },
            CancellationToken.None);

        Assert.True(await provider.GetRequiredService<DatabaseInitializer>()
            .InitializeAsync(CancellationToken.None));

        IReadOnlyList<SortMemoryRecord> records = await memory.GetAllAsync(CancellationToken.None);
        _ = Assert.Single(records);
    }

    // Eine kleine, für die Sicherung ausreichende SQLite-Datei mit genau einer Zeile.
    private string CreateDatabaseWithOneRow()
    {
        string databasePath = Path.Combine(_dataDirectory, "picturesorter.db");
        using (SqliteConnection connection = new($"Data Source={databasePath}"))
        {
            connection.Open();

            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE Probe (Id INTEGER PRIMARY KEY); INSERT INTO Probe (Id) VALUES (1);";
            _ = command.ExecuteNonQuery();
        }

        // Eine geschlossene Verbindung bleibt im Pool und hält die Datei offen; der
        // anschließende Wechsel in den WAL-Modus scheiterte daran mit „readonly
        // database". Im Betrieb gibt es diese zweite Verbindung nicht.
        SqliteConnection.ClearAllPools();
        return databasePath;
    }

    private static int CountRows(string databasePath)
    {
        using SqliteConnection connection = new($"Data Source={databasePath}");
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Probe;";
        return Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }
}
