using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PictureSorter.Application.Sorting;
using PictureSorter.Core.Entities;
using PictureSorter.Core.Enums;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;
using PictureSorter.Data.Context;
using PictureSorter.Data.DependencyInjection;
using PictureSorter.Infrastructure.Time;

namespace PictureSorter.Integration.Tests.Sorting;

/// <summary>
/// Die Rettung eines Laufs, den es nur noch im Gedächtnis gibt: echte Dateien, echte
/// SQLite-Datenbank.
///
/// Das Ergebnis eines unterbrochenen Laufs steht vollständig im Gedächtnis — genutzt
/// wurde davon aber nur die Ablehnung, sodass jeder zweite Anlauf die Vorschläge neu
/// berechnen musste. Diese Tests halten fest, dass sie sich zurückholen lassen.
/// </summary>
public sealed class AnalysisRecoveryIntegrationTests : IAsyncLifetime
{
    private const string Category = "Urlaub";

    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "PictureSorterTests", Guid.NewGuid().ToString("N"));

    private string _photoFolder = null!;
    private ServiceProvider _provider = null!;
    private ISortMemory _memory = null!;
    private SortMemoryRecovery _recovery = null!;

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
        _memory = _provider.GetRequiredService<ISortMemory>();
        _recovery = new SortMemoryRecovery(
            _memory,
            new NoMetadataReader(),
            NullLogger<SortMemoryRecovery>.Instance);
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
    public async Task RecoverAsync_BringsBackTheProposalsOfAnInterruptedRun()
    {
        Photo first = CreatePhoto("a.jpg");
        Photo second = CreatePhoto("b.jpg");
        await RememberAsync(first, SortMemoryStatus.Proposed, 0.91);
        await RememberAsync(second, SortMemoryStatus.Proposed, 0.83);

        // Eine abgelehnte Datei darf nicht wieder auftauchen: Das wäre das Gegenteil
        // eines Gedächtnisses.
        Photo rejected = CreatePhoto("c.jpg");
        await RememberAsync(rejected, SortMemoryStatus.Rejected, 0.1);

        IReadOnlyList<SortProposal> proposals = await _recovery.RecoverAsync(
            _photoFolder, Category, CategoryKind.Topic, CancellationToken.None);

        Assert.Equal(2, proposals.Count);
        Assert.Equal(
            [first.FullPath, second.FullPath],
            proposals.Select(proposal => proposal.Photo.FullPath).Order(StringComparer.Ordinal));
        Assert.All(proposals, proposal => Assert.Equal(Category, proposal.CategoryName));
    }

    [Fact]
    public async Task RecoverAsync_WhenTheFileChanged_LeavesItOut()
    {
        Photo photo = CreatePhoto("a.jpg");
        await RememberAsync(photo, SortMemoryStatus.Proposed, 0.9);

        // Nach der Analyse verändert: Die Signatur passt nicht mehr, und das gemerkte
        // Urteil gilt nicht für das, was jetzt dort liegt.
        await File.WriteAllTextAsync(
            photo.FullPath, "ein deutlich längerer Inhalt als vorher", TestContext.Current.CancellationToken);

        IReadOnlyList<SortProposal> proposals = await _recovery.RecoverAsync(
            _photoFolder, Category, CategoryKind.Topic, CancellationToken.None);

        Assert.Empty(proposals);
    }

    [Fact]
    public async Task RecoverAsync_WhenTheFileIsGone_LeavesItOut()
    {
        Photo photo = CreatePhoto("a.jpg");
        await RememberAsync(photo, SortMemoryStatus.Proposed, 0.9);
        File.Delete(photo.FullPath);

        IReadOnlyList<SortProposal> proposals = await _recovery.RecoverAsync(
            _photoFolder, Category, CategoryKind.Topic, CancellationToken.None);

        Assert.Empty(proposals);
    }

    [Fact]
    public async Task RecoverAsync_ForAnotherCategory_FindsNothing()
    {
        Photo photo = CreatePhoto("a.jpg");
        await RememberAsync(photo, SortMemoryStatus.Proposed, 0.9);

        IReadOnlyList<SortProposal> proposals = await _recovery.RecoverAsync(
            _photoFolder, "Weihnachten", CategoryKind.Topic, CancellationToken.None);

        Assert.Empty(proposals);
    }

    private Photo CreatePhoto(string name)
    {
        string path = Path.Combine(_photoFolder, name);
        File.WriteAllText(path, $"Inhalt von {name}");
        FileInfo info = new(path);

        return new Photo
        {
            FullPath = info.FullName,
            FileName = info.Name,
            SizeBytes = info.Length,
            CapturedAt = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero),
        };
    }

    /// <summary>
    /// Liefert keine Metadaten. Der echte Leser hängt an Windows-Bild-Schnittstellen und
    /// braucht ein echtes Bild; hier geht es um die Signatur und darum, welche Einträge
    /// zurückkommen — nicht um EXIF.
    /// </summary>
    private sealed class NoMetadataReader : IImageMetadataReader
    {
        public Task<PhotoMetadata?> ReadAsync(string filePath, CancellationToken cancellationToken) =>
            Task.FromResult<PhotoMetadata?>(null);
    }

    private Task RememberAsync(Photo photo, SortMemoryStatus status, double confidence) =>
        _memory.UpsertAsync(
            new SortMemoryRecord
            {
                FolderPath = _photoFolder,
                FileSignature = photo.ComputeSignature(),
                PhotoPath = photo.FullPath,
                CategoryName = Category,
                Status = status,
                Confidence = confidence,
                UpdatedAt = new SystemClock().UtcNow,
            },
            CancellationToken.None);
}
