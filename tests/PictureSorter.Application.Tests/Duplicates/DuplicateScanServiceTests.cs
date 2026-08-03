using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PictureSorter.Application.Duplicates;
using PictureSorter.Core.Entities;
using PictureSorter.Core.Enums;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;
using PictureSorter.Application.Tests.Fakes;

namespace PictureSorter.Application.Tests.Duplicates;

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

    [Fact]
    public async Task ScanAsync_WhenOneFileCannotBeRead_ScansTheRestAndStillFindsTheDuplicates()
    {
        // Der Alltagsfall: Ein Virenscanner zieht eine Datei mitten im Lauf weg. Vorher
        // riss diese eine Datei den kompletten Lauf mit – nach Minuten Wartezeit stand
        // statt des Ergebnisses eine Fehlermeldung da.
        Dictionary<string, ImageFingerprint> fingerprints = new(StringComparer.Ordinal)
        {
            [@"C:\f\a.jpg"] = Fingerprint(@"C:\f\a.jpg", "AAA", null),
            [@"C:\f\b.jpg"] = Fingerprint(@"C:\f\b.jpg", "AAA", null),
            [@"C:\f\weg.jpg"] = Fingerprint(@"C:\f\weg.jpg", "ZZZ", null),
        };
        DuplicateScanService service = CreateService(
            fingerprints,
            unreadable: @"C:\f\weg.jpg");

        IReadOnlyList<DuplicateGroup> groups =
            await service.ScanAsync(@"C:\f", includeSubfolders: false, progress: null, CancellationToken.None);

        DuplicateGroup group = Assert.Single(groups);
        Assert.Equal(2, group.Photos.Count);
    }

    [Fact]
    public async Task ScanAsync_WhenOneFileCannotBeRead_ReportsProgressForItAsWell()
    {
        // Der Fortschritt muss auch über die übersprungene Datei hinweg laufen, sonst
        // bleibt der Balken bei einem gesperrten Bild stehen und wirkt wie ein Hänger.
        Dictionary<string, ImageFingerprint> fingerprints = new(StringComparer.Ordinal)
        {
            [@"C:\f\a.jpg"] = Fingerprint(@"C:\f\a.jpg", "A", null),
            [@"C:\f\weg.jpg"] = Fingerprint(@"C:\f\weg.jpg", "Z", null),
        };
        DuplicateScanService service = CreateService(fingerprints, unreadable: @"C:\f\weg.jpg");
        RecordingProgress<DuplicateScanProgress> reported = new();

        _ = await service.ScanAsync(
            @"C:\f",
            includeSubfolders: false,
            progress: reported,
            CancellationToken.None);

        // Zwei Meldungen mit Zählstand im Abschnitt „Auswerten" – eine je Datei, die
        // gesperrte eingeschlossen. Der Balken erreicht also das Ende.
        Assert.Equal(
            [new DuplicateScanProgress(1, 2, ScanPhase.Analyzing), new DuplicateScanProgress(2, 2, ScanPhase.Analyzing)],
            reported.Reports.Where(report => report.Phase == ScanPhase.Analyzing && report.Processed > 0));
    }

    [Fact]
    public async Task ScanAsync_ReportsGatheringBeforeAnalysing()
    {
        // Das Einlesen der Dateien geht der Auswertung voraus und dauert bei einem großen
        // Ordner am längsten. Bis hierher lief es stumm: Der Balken zeigte erst etwas an,
        // als dieser Abschnitt längst vorbei war.
        Dictionary<string, ImageFingerprint> fingerprints = new(StringComparer.Ordinal)
        {
            [@"C:\f\a.jpg"] = Fingerprint(@"C:\f\a.jpg", "A", null),
            [@"C:\f\b.jpg"] = Fingerprint(@"C:\f\b.jpg", "B", null),
        };
        DuplicateScanService service = CreateService(fingerprints);
        RecordingProgress<DuplicateScanProgress> reported = new();

        _ = await service.ScanAsync(@"C:\f", includeSubfolders: false, reported, CancellationToken.None);

        Assert.Contains(new DuplicateScanProgress(2, 2, ScanPhase.Gathering), reported.Reports);
        Assert.True(
            reported.Reports.FindIndex(report => report.Phase == ScanPhase.Gathering)
            < reported.Reports.FindIndex(report => report.Phase == ScanPhase.Analyzing),
            "Das Einlesen muss vor der Auswertung gemeldet werden.");
    }

    [Fact]
    public async Task ScanAsync_ReportsTheTotalBeforeTheFirstFileIsRead()
    {
        // Die Gesamtzahl muss von Anfang an feststehen – „Bild 1 von 1100" ist die
        // Auskunft, auf die es wartet, nicht ein Balken ohne Bezugsgröße.
        Dictionary<string, ImageFingerprint> fingerprints = new(StringComparer.Ordinal)
        {
            [@"C:\f\a.jpg"] = Fingerprint(@"C:\f\a.jpg", "A", null),
            [@"C:\f\b.jpg"] = Fingerprint(@"C:\f\b.jpg", "B", null),
        };
        DuplicateScanService service = CreateService(fingerprints);
        RecordingProgress<DuplicateScanProgress> reported = new();

        _ = await service.ScanAsync(@"C:\f", includeSubfolders: false, reported, CancellationToken.None);

        Assert.Equal(new DuplicateScanProgress(0, 2, ScanPhase.Gathering), reported.Reports[0]);
    }

    private static ImageFingerprint Fingerprint(string path, string contentHash, PerceptualHash? perceptual) =>
        new() { FilePath = path, ContentHash = contentHash, Perceptual = perceptual };

    private static DuplicateScanService CreateService(
        Dictionary<string, ImageFingerprint> fingerprints,
        string? unreadable = null)
    {
        List<Photo> photos = [.. fingerprints.Keys.Select(path => new Photo
        {
            FullPath = path,
            FileName = Path.GetFileName(path),
        })];

        FakePhotoSource photoSource = new(photos);
        FakePerceptualHasher hasher = new(path => path == unreadable
            ? throw new IOException($"Die Datei „{path}\" ist gesperrt.")
            : fingerprints[path]);
        IOptions<DuplicateScanOptions> options =
            Options.Create(new DuplicateScanOptions { DetectSimilar = true, MaxHammingDistance = 8 });

        return new DuplicateScanService(photoSource, hasher, options, NullLogger<DuplicateScanService>.Instance);
    }
}
