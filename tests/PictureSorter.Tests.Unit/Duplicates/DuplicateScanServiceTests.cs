using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PictureSorter.Application.Duplicates;
using PictureSorter.Core.Entities;
using PictureSorter.Core.Enums;
using PictureSorter.Core.ValueObjects;
using PictureSorter.Tests.Unit.Fakes;

namespace PictureSorter.Tests.Unit.Duplicates;

/// <summary>
/// Tests der Duplikat-Gruppierung (exakt über Inhalts-Hash, ähnlich über die
/// Hamming-Distanz der Wahrnehmungs-Hashes).
/// </summary>
public sealed class DuplicateScanServiceTests
{
    [Fact]
    public async Task ScanAsync_IdenticalContentHashes_GroupsAsExact()
    {
        Dictionary<string, ImageFingerprint> fingerprints = new(StringComparer.Ordinal)
        {
            [@"C:\f\a.jpg"] = Fingerprint(@"C:\f\a.jpg", "AAA", null),
            [@"C:\f\b.jpg"] = Fingerprint(@"C:\f\b.jpg", "AAA", null),
            [@"C:\f\c.jpg"] = Fingerprint(@"C:\f\c.jpg", "CCC", new PerceptualHash(1UL)),
        };
        DuplicateScanService service = CreateService(fingerprints);

        IReadOnlyList<DuplicateGroup> groups =
            await service.ScanAsync(@"C:\f", includeSubfolders: false, progress: null, CancellationToken.None);

        DuplicateGroup group = Assert.Single(groups);
        Assert.Equal(DuplicateKind.Exact, group.Kind);
        Assert.Equal(2, group.Photos.Count);
    }

    [Fact]
    public async Task ScanAsync_ClosePerceptualHashes_GroupsAsSimilar()
    {
        Dictionary<string, ImageFingerprint> fingerprints = new(StringComparer.Ordinal)
        {
            [@"C:\f\a.jpg"] = Fingerprint(@"C:\f\a.jpg", "A", new PerceptualHash(0UL)),
            [@"C:\f\b.jpg"] = Fingerprint(@"C:\f\b.jpg", "B", new PerceptualHash(0UL)),
            [@"C:\f\c.jpg"] = Fingerprint(@"C:\f\c.jpg", "C", new PerceptualHash(ulong.MaxValue)),
        };
        DuplicateScanService service = CreateService(fingerprints);

        IReadOnlyList<DuplicateGroup> groups =
            await service.ScanAsync(@"C:\f", includeSubfolders: false, progress: null, CancellationToken.None);

        DuplicateGroup group = Assert.Single(groups);
        Assert.Equal(DuplicateKind.Similar, group.Kind);
        Assert.Equal(2, group.Photos.Count);
    }

    [Fact]
    public async Task ScanAsync_NoDuplicates_ReturnsEmpty()
    {
        Dictionary<string, ImageFingerprint> fingerprints = new(StringComparer.Ordinal)
        {
            [@"C:\f\a.jpg"] = Fingerprint(@"C:\f\a.jpg", "A", new PerceptualHash(0UL)),
            [@"C:\f\b.jpg"] = Fingerprint(@"C:\f\b.jpg", "B", new PerceptualHash(0xFFFFUL)),
            [@"C:\f\c.jpg"] = Fingerprint(@"C:\f\c.jpg", "C", new PerceptualHash(0xFFFFFFFFUL)),
        };
        DuplicateScanService service = CreateService(fingerprints);

        IReadOnlyList<DuplicateGroup> groups =
            await service.ScanAsync(@"C:\f", includeSubfolders: false, progress: null, CancellationToken.None);

        Assert.Empty(groups);
    }

    private static ImageFingerprint Fingerprint(string path, string contentHash, PerceptualHash? perceptual) =>
        new() { FilePath = path, ContentHash = contentHash, Perceptual = perceptual };

    private static DuplicateScanService CreateService(Dictionary<string, ImageFingerprint> fingerprints)
    {
        List<Photo> photos = [.. fingerprints.Keys.Select(path => new Photo
        {
            FullPath = path,
            FileName = Path.GetFileName(path),
        })];

        FakePhotoSource photoSource = new(photos);
        FakePerceptualHasher hasher = new(path => fingerprints[path]);
        IOptions<DuplicateScanOptions> options =
            Options.Create(new DuplicateScanOptions { DetectSimilar = true, MaxHammingDistance = 8 });

        return new DuplicateScanService(photoSource, hasher, options, NullLogger<DuplicateScanService>.Instance);
    }
}
