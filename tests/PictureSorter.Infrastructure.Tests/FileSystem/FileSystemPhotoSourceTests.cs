using Microsoft.Extensions.Logging.Abstractions;
using PictureSorter.Core.Entities;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;
using PictureSorter.Infrastructure.FileSystem;

namespace PictureSorter.Infrastructure.Tests.FileSystem;

/// <summary>
/// Randfall-Tests der Datei-Fotoquelle gegen ein echtes Dateisystem: fehlender und
/// leerer Ordner, nicht unterstützte Endungen, Unterordner und Abbruch.
/// </summary>
public sealed class FileSystemPhotoSourceTests : IDisposable
{
    private readonly string _root;
    private readonly FileSystemPhotoSource _source;

    public FileSystemPhotoSourceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "PictureSorterTests", Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(_root);
        _source = new FileSystemPhotoSource(new NullMetadataReader(), NullLogger<FileSystemPhotoSource>.Instance);
    }

    [Fact]
    public async Task GetPhotosAsync_MissingFolder_ThrowsDirectoryNotFound()
    {
        string missing = Path.Combine(_root, "gibt-es-nicht");

        _ = await Assert.ThrowsAsync<DirectoryNotFoundException>(
            () => _source.GetPhotosAsync(missing, includeSubfolders: false, skip: 0, maxCount: null, CancellationToken.None));
    }

    [Fact]
    public async Task GetPhotosAsync_EmptyFolder_ReturnsEmpty()
    {
        IReadOnlyList<Photo> photos = await _source.GetPhotosAsync(_root, includeSubfolders: false, skip: 0, maxCount: null, CancellationToken.None);

        Assert.Empty(photos);
    }

    [Fact]
    public async Task GetPhotosAsync_OnlyUnsupportedExtensions_ReturnsEmpty()
    {
        Write("notiz.txt");
        Write("video.mp4");

        IReadOnlyList<Photo> photos = await _source.GetPhotosAsync(_root, includeSubfolders: false, skip: 0, maxCount: null, CancellationToken.None);

        Assert.Empty(photos);
    }

    [Fact]
    public async Task GetPhotosAsync_FiltersToSupportedImages()
    {
        Write("a.jpg");
        Write("b.png");
        Write("c.txt");

        IReadOnlyList<Photo> photos = await _source.GetPhotosAsync(_root, includeSubfolders: false, skip: 0, maxCount: null, CancellationToken.None);

        Assert.Equal(2, photos.Count);
        Assert.All(photos, photo => Assert.True(Path.GetExtension(photo.FileName) is ".jpg" or ".png"));
    }

    [Fact]
    public async Task GetPhotosAsync_IncludeSubfolders_FindsNestedImages()
    {
        Write("oben.jpg");
        Write(Path.Combine("Unterordner", "unten.jpg"));

        IReadOnlyList<Photo> flat = await _source.GetPhotosAsync(_root, includeSubfolders: false, skip: 0, maxCount: null, CancellationToken.None);
        IReadOnlyList<Photo> deep = await _source.GetPhotosAsync(_root, includeSubfolders: true, skip: 0, maxCount: null, CancellationToken.None);

        _ = Assert.Single(flat);
        Assert.Equal(2, deep.Count);
    }

    [Fact]
    public async Task GetPhotosAsync_Cancelled_Throws()
    {
        Write("a.jpg");
        using CancellationTokenSource cts = new();
        await cts.CancelAsync();

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _source.GetPhotosAsync(_root, includeSubfolders: false, skip: 0, maxCount: null, cts.Token));
    }

    [Fact]
    public async Task GetPhotosAsync_WithMaxCount_ReadsOnlyThatManyFiles()
    {
        // Der teure Teil ist das Öffnen jeder Datei für die Metadaten. Liegt der Ordner
        // in einem Cloud-Speicher (iCloud-Fotos unter Windows), zieht jedes Öffnen einen
        // vollständigen Download nach sich. Die Höchstzahl muss deshalb schon vor dem
        // Einlesen greifen – ein nachträgliches Abschneiden hätte längst alles geholt.
        Write("a.jpg");
        Write("b.jpg");
        Write("c.jpg");
        CountingMetadataReader reader = new();
        FileSystemPhotoSource source = new(reader, NullLogger<FileSystemPhotoSource>.Instance);

        IReadOnlyList<Photo> photos = await source.GetPhotosAsync(_root, includeSubfolders: false, skip: 0, maxCount: 2, CancellationToken.None);

        Assert.Equal(2, photos.Count);
        Assert.Equal(2, reader.CallCount);
    }

    [Fact]
    public async Task GetPhotosAsync_WithMaxCountAboveTheFileCount_ReturnsAll()
    {
        Write("a.jpg");
        Write("b.jpg");

        IReadOnlyList<Photo> photos = await _source.GetPhotosAsync(_root, includeSubfolders: false, skip: 0, maxCount: 10, CancellationToken.None);

        Assert.Equal(2, photos.Count);
    }

    [Fact]
    public async Task GetPhotosAsync_WithMaxCountZero_Throws()
    {
        _ = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => _source.GetPhotosAsync(_root, includeSubfolders: false, skip: 0, maxCount: 0, CancellationToken.None));
    }

    // Nicht-asynchroner Helfer: synchrones Datei-Schreiben in einer async-Testmethode
    // löst sonst CA1849 aus.
    private void Write(string relativePath)
    {
        string path = Path.Combine(_root, relativePath);
        _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [1, 2, 3]);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class NullMetadataReader : IImageMetadataReader
    {
        public Task<PhotoMetadata?> ReadAsync(string filePath, CancellationToken cancellationToken) =>
            Task.FromResult<PhotoMetadata?>(null);
    }

    // Zählt die Zugriffe auf die Metadaten. Nur so lässt sich belegen, dass wirklich
    // weniger Dateien geöffnet werden – die Zahl der zurückgegebenen Fotos allein
    // sähe auch dann richtig aus, wenn erst hinterher abgeschnitten würde.
    private sealed class CountingMetadataReader : IImageMetadataReader
    {
        public int CallCount { get; private set; }

        public Task<PhotoMetadata?> ReadAsync(string filePath, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult<PhotoMetadata?>(null);
        }
    }
}
