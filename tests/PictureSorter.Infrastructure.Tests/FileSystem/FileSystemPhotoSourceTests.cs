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
            () => _source.GetPhotosAsync(missing, includeSubfolders: false, CancellationToken.None));
    }

    [Fact]
    public async Task GetPhotosAsync_EmptyFolder_ReturnsEmpty()
    {
        IReadOnlyList<Photo> photos = await _source.GetPhotosAsync(_root, includeSubfolders: false, CancellationToken.None);

        Assert.Empty(photos);
    }

    [Fact]
    public async Task GetPhotosAsync_OnlyUnsupportedExtensions_ReturnsEmpty()
    {
        Write("notiz.txt");
        Write("video.mp4");

        IReadOnlyList<Photo> photos = await _source.GetPhotosAsync(_root, includeSubfolders: false, CancellationToken.None);

        Assert.Empty(photos);
    }

    [Fact]
    public async Task GetPhotosAsync_FiltersToSupportedImages()
    {
        Write("a.jpg");
        Write("b.png");
        Write("c.txt");

        IReadOnlyList<Photo> photos = await _source.GetPhotosAsync(_root, includeSubfolders: false, CancellationToken.None);

        Assert.Equal(2, photos.Count);
        Assert.All(photos, photo => Assert.True(Path.GetExtension(photo.FileName) is ".jpg" or ".png"));
    }

    [Fact]
    public async Task GetPhotosAsync_IncludeSubfolders_FindsNestedImages()
    {
        Write("oben.jpg");
        Write(Path.Combine("Unterordner", "unten.jpg"));

        IReadOnlyList<Photo> flat = await _source.GetPhotosAsync(_root, includeSubfolders: false, CancellationToken.None);
        IReadOnlyList<Photo> deep = await _source.GetPhotosAsync(_root, includeSubfolders: true, CancellationToken.None);

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
            () => _source.GetPhotosAsync(_root, includeSubfolders: false, cts.Token));
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
}
