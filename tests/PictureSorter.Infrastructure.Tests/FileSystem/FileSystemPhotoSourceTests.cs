using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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
        _source = new FileSystemPhotoSource(new NullMetadataReader(), Options.Create(new PhotoSourceOptions()), NullLogger<FileSystemPhotoSource>.Instance);
    }

    [Fact]
    public async Task GetPhotosAsync_MissingFolder_ThrowsDirectoryNotFound()
    {
        string missing = Path.Combine(_root, "gibt-es-nicht");

        _ = await Assert.ThrowsAsync<DirectoryNotFoundException>(
            () => _source.GetPhotosAsync(missing, includeSubfolders: false, skip: 0, maxCount: null, progress: null, CancellationToken.None));
    }

    [Fact]
    public async Task GetPhotosAsync_EmptyFolder_ReturnsEmpty()
    {
        IReadOnlyList<Photo> photos = await _source.GetPhotosAsync(_root, includeSubfolders: false, skip: 0, maxCount: null, progress: null, CancellationToken.None);

        Assert.Empty(photos);
    }

    [Fact]
    public async Task GetPhotosAsync_OnlyUnsupportedExtensions_ReturnsEmpty()
    {
        Write("notiz.txt");
        Write("video.mp4");

        IReadOnlyList<Photo> photos = await _source.GetPhotosAsync(_root, includeSubfolders: false, skip: 0, maxCount: null, progress: null, CancellationToken.None);

        Assert.Empty(photos);
    }

    [Fact]
    public async Task GetPhotosAsync_FiltersToSupportedImages()
    {
        Write("a.jpg");
        Write("b.png");
        Write("c.txt");

        IReadOnlyList<Photo> photos = await _source.GetPhotosAsync(_root, includeSubfolders: false, skip: 0, maxCount: null, progress: null, CancellationToken.None);

        Assert.Equal(2, photos.Count);
        Assert.All(photos, photo => Assert.True(Path.GetExtension(photo.FileName) is ".jpg" or ".png"));
    }

    [Fact]
    public async Task GetPhotosAsync_IncludeSubfolders_FindsNestedImages()
    {
        Write("oben.jpg");
        Write(Path.Combine("Unterordner", "unten.jpg"));

        IReadOnlyList<Photo> flat = await _source.GetPhotosAsync(_root, includeSubfolders: false, skip: 0, maxCount: null, progress: null, CancellationToken.None);
        IReadOnlyList<Photo> deep = await _source.GetPhotosAsync(_root, includeSubfolders: true, skip: 0, maxCount: null, progress: null, CancellationToken.None);

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
            () => _source.GetPhotosAsync(_root, includeSubfolders: false, skip: 0, maxCount: null, progress: null, cts.Token));
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
        FileSystemPhotoSource source = new(reader, Options.Create(new PhotoSourceOptions()), NullLogger<FileSystemPhotoSource>.Instance);

        IReadOnlyList<Photo> photos = await source.GetPhotosAsync(_root, includeSubfolders: false, skip: 0, maxCount: 2, progress: null, CancellationToken.None);

        Assert.Equal(2, photos.Count);
        Assert.Equal(2, reader.CallCount);
    }

    [Fact]
    public async Task GetPhotosAsync_WithMaxCountAboveTheFileCount_ReturnsAll()
    {
        Write("a.jpg");
        Write("b.jpg");

        IReadOnlyList<Photo> photos = await _source.GetPhotosAsync(_root, includeSubfolders: false, skip: 0, maxCount: 10, progress: null, CancellationToken.None);

        Assert.Equal(2, photos.Count);
    }

    [Fact]
    public async Task GetPhotosAsync_WithMaxCountZero_Throws()
    {
        _ = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => _source.GetPhotosAsync(_root, includeSubfolders: false, skip: 0, maxCount: 0, progress: null, CancellationToken.None));
    }

    // Nicht-asynchroner Helfer: synchrones Datei-Schreiben in einer async-Testmethode
    // löst sonst CA1849 aus.
    [Fact]
    public async Task GetPhotosAsync_WhenAFileDisappearsWhileReading_SkipsItAndKeepsTheRest()
    {
        // Genau so passiert es im Betrieb: Zwischen dem Auflisten und dem Lesen zieht
        // ein Virenscanner die Datei weg. Bis hierher riss der Zugriff auf die
        // Dateigröße den gesamten Lauf mit – für jede Funktion, die Fotos einliest.
        Write("a.jpg");
        Write("verschwindet.jpg");
        Write("b.jpg");
        FileSystemPhotoSource source = new(
            new DeletingMetadataReader(Path.Combine(_root, "verschwindet.jpg")),
            Options.Create(new PhotoSourceOptions()),
            NullLogger<FileSystemPhotoSource>.Instance);

        IReadOnlyList<Photo> photos = await source.GetPhotosAsync(
            _root, includeSubfolders: false, skip: 0, maxCount: null, progress: null, CancellationToken.None);

        Assert.Equal(2, photos.Count);
        Assert.DoesNotContain(photos, photo => photo.FileName == "verschwindet.jpg");
    }

    [Fact]
    public async Task GetPhotosAsync_ReportsTheTotalUpFrontAndThenCountsEveryFile()
    {
        // Das Einlesen öffnet jede Datei einzeln und ist bei einem großen Ordner der
        // längste Teil eines Laufs. Er lief bis hierher ohne jede Rückmeldung ab: Die
        // Oberfläche zeigte nur einen unbestimmten Balken, und der Zählstand erschien
        // erst danach – zeitlich also viel zu spät.
        Write("a.jpg");
        Write("b.jpg");
        Write("c.jpg");
        RecordingProgress reported = new();

        _ = await _source.GetPhotosAsync(
            _root, includeSubfolders: false, skip: 0, maxCount: null, reported, CancellationToken.None);

        // Zuerst die Gesamtzahl – ohne sie stünde in der Statusleiste ein Zählstand
        // ohne Bezugsgröße. Danach je Datei ein Schritt bis ans Ende.
        Assert.Equal(
            [
                new PhotoScanProgress(0, 3),
                new PhotoScanProgress(1, 3),
                new PhotoScanProgress(2, 3),
                new PhotoScanProgress(3, 3),
            ],
            reported.Reports);
    }

    [Fact]
    public async Task GetPhotosAsync_CountsAFileItHadToSkip()
    {
        // Gezählt werden die bearbeiteten, nicht die brauchbaren Dateien: Sonst käme der
        // Balken bei einem Ordner mit einer unlesbaren Datei nie bei 100 % an.
        Write("a.jpg");
        Write("verschwindet.jpg");
        Write("b.jpg");
        FileSystemPhotoSource source = new(
            new DeletingMetadataReader(Path.Combine(_root, "verschwindet.jpg")),
            Options.Create(new PhotoSourceOptions()),
            NullLogger<FileSystemPhotoSource>.Instance);
        RecordingProgress reported = new();

        IReadOnlyList<Photo> photos = await source.GetPhotosAsync(
            _root, includeSubfolders: false, skip: 0, maxCount: null, reported, CancellationToken.None);

        Assert.Equal(2, photos.Count);
        Assert.Equal(new PhotoScanProgress(3, 3), reported.Reports[^1]);
    }

    [Fact]
    public async Task GetPhotosAsync_ReadsSeveralFilesAtOnce()
    {
        // Der Kern der Beschleunigung: Das Einlesen wartet fast nur — auf die Platte, bei
        // einem Cloud-Ordner auf den Download der Datei. Nacheinander summiert sich diese
        // Wartezeit zum längsten Abschnitt eines Laufs, obwohl sie sich übereinanderlegen
        // lässt. Eine Zusicherung über die Laufzeit wäre auf einer beliebigen Maschine
        // nicht verlässlich; deshalb wird gezählt, wie viele Zugriffe gleichzeitig
        // offen waren.
        foreach (int index in Enumerable.Range(0, 8))
        {
            Write($"bild{index}.jpg");
        }

        ConcurrencyTrackingMetadataReader reader = new();
        FileSystemPhotoSource source = new(
            reader,
            Options.Create(new PhotoSourceOptions { MaxParallelReads = 4 }),
            NullLogger<FileSystemPhotoSource>.Instance);

        _ = await source.GetPhotosAsync(
            _root, includeSubfolders: false, skip: 0, maxCount: null, progress: null, CancellationToken.None);

        Assert.True(
            reader.MaxConcurrent > 1,
            $"Es war immer nur eine Datei offen ({reader.MaxConcurrent}); das Einlesen läuft also weiterhin nacheinander.");
    }

    [Fact]
    public async Task GetPhotosAsync_KeepsTheFolderOrderDespiteParallelReading()
    {
        // Die Beispielauswahl blättert über einen Startpunkt durch den Ordner. Hinge die
        // Reihenfolge davon ab, welche Datei zufällig zuerst gelesen ist, bekäme sie beim
        // Nachfordern Bilder doppelt zu sehen und andere nie.
        foreach (int index in Enumerable.Range(0, 8))
        {
            Write($"bild{index}.jpg");
        }

        // Die erste Datei braucht am längsten, die letzte am kürzesten: Ohne feste Plätze
        // käme die Liste genau verkehrt herum heraus.
        ConcurrencyTrackingMetadataReader reader = new(
            path => TimeSpan.FromMilliseconds(80 - (int.Parse(Path.GetFileNameWithoutExtension(path)[4..], CultureInfo.InvariantCulture) * 10)));
        FileSystemPhotoSource source = new(
            reader,
            Options.Create(new PhotoSourceOptions { MaxParallelReads = 8 }),
            NullLogger<FileSystemPhotoSource>.Instance);

        IReadOnlyList<Photo> photos = await source.GetPhotosAsync(
            _root, includeSubfolders: false, skip: 0, maxCount: null, progress: null, CancellationToken.None);

        // Gegen die Aufzählung des Ordners geprüft statt gegen eine angenommene
        // Sortierung: Die Quelle verspricht die Reihenfolge des Dateisystems, keine eigene.
        IEnumerable<string> expected = Directory
            .EnumerateFiles(_root, "*", SearchOption.TopDirectoryOnly)
            .Select(path => Path.GetFileName(path));

        Assert.Equal(expected, photos.Select(photo => photo.FileName));
    }

    [Fact]
    public async Task StreamPhotosAsync_YieldsEveryPhotoWithItsPlaceAndTheTotal()
    {
        // Die Bilder kommen in der Reihenfolge, in der sie fertig werden. Ohne Index
        // ließe sich daraus die Reihenfolge des Ordners nicht wiederherstellen, und ohne
        // Gesamtzahl fehlte beiden Fortschrittsbalken die Bezugsgröße.
        foreach (int index in Enumerable.Range(0, 5))
        {
            Write($"bild{index}.jpg");
        }

        List<ScannedPhoto> streamed = [];
        await foreach (ScannedPhoto scanned in _source.StreamPhotosAsync(
            _root, includeSubfolders: false, skip: 0, maxCount: null, progress: null, CancellationToken.None))
        {
            streamed.Add(scanned);
        }

        Assert.Equal(5, streamed.Count);
        Assert.All(streamed, scanned => Assert.Equal(5, scanned.Total));
        Assert.Equal([0, 1, 2, 3, 4], [.. streamed.Select(scanned => scanned.Index).Order()]);
    }

    [Fact]
    public async Task StreamPhotosAsync_CountsAPhotoAsLoadedBeforeHandingItOver()
    {
        // Damit die Bewertung dem Laden immer hinterherläuft, muss ein Bild als geladen
        // gezählt sein, bevor es überhaupt weitergereicht wird. Andersherum griffe die
        // Bewertung es auf und meldete es, ehe der Ladebalken davon weiß — der
        // Analysebalken stünde dann vor dem Ladebalken.
        //
        // Der Empfänger meldet bewusst langsam. Ohne das liefe die vertauschte Reihenfolge
        // im Test genauso durch: Zwischen Weiterreichen und Melden lägen nur Nanosekunden,
        // und der Verbraucher käme fast nie dazwischen. Erst die Wartezeit reißt das
        // Zeitfenster auf, in dem der Fehler überhaupt sichtbar wird.
        foreach (int index in Enumerable.Range(0, 10))
        {
            Write($"bild{index}.jpg");
        }

        RecordingProgress reported = new(TimeSpan.FromMilliseconds(20));
        int received = 0;

        await foreach (ScannedPhoto scanned in _source.StreamPhotosAsync(
            _root, includeSubfolders: false, skip: 0, maxCount: null, reported, CancellationToken.None))
        {
            received++;

            Assert.True(
                reported.LastProcessed >= received,
                $"Beim {received}. Bild waren erst {reported.LastProcessed} Dateien als geladen gemeldet.");
        }

        Assert.Equal(10, received);
    }

    [Fact]
    public async Task StreamPhotosAsync_DoesNotLoadTheWholeFolderAheadOfTheConsumer()
    {
        // Der Kern des begrenzten Vorlaufs: Ohne ihn zöge die Anwendung bei einem
        // Cloud-Ordner alle Dateien herunter, während die Bewertung noch beim ersten Bild
        // steht — Bandbreite und Plattenplatz für etwas, das noch lange niemand braucht,
        // und bei einem Abbruch vollständig umsonst.
        foreach (int index in Enumerable.Range(0, 40))
        {
            Write($"bild{index}.jpg");
        }

        CountingMetadataReader reader = new();
        FileSystemPhotoSource source = new(
            reader,
            Options.Create(new PhotoSourceOptions { MaxParallelReads = 2, PrefetchBuffer = 4 }),
            NullLogger<FileSystemPhotoSource>.Instance);

        await foreach (ScannedPhoto scanned in source.StreamPhotosAsync(
            _root, includeSubfolders: false, skip: 0, maxCount: null, progress: null, CancellationToken.None))
        {
            // Nach dem ersten Bild aussteigen, dem Erzeuger aber Gelegenheit geben, den
            // Vorlauf zu füllen — mehr als den Vorlauf darf er nicht laden.
            await Task.Delay(TimeSpan.FromMilliseconds(150), TestContext.Current.CancellationToken);
            break;
        }

        // Puffer (4) + gleichzeitige Leser (2) + das entnommene Bild, großzügig bemessen:
        // Der Test soll den Unterschied zu „alle 40" belegen, nicht eine exakte Zahl.
        Assert.True(
            reader.CallCount < 20,
            $"Es wurden {reader.CallCount} von 40 Dateien gelesen, obwohl der Verbraucher nur eine abgenommen hat.");
    }

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

    // Nimmt die Meldungen unmittelbar entgegen. Bewusst kein Progress<T>: Das meldet über
    // den Synchronisationskontext und damit nicht zwingend vor der Rückkehr – der Test
    // wäre zeitabhängig.
    private sealed class RecordingProgress(TimeSpan delay = default) : IProgress<PhotoScanProgress>
    {
        private readonly Lock _gate = new();
        private readonly List<PhotoScanProgress> _reports = [];

        public IReadOnlyList<PhotoScanProgress> Reports
        {
            get
            {
                lock (_gate)
                {
                    return [.. _reports];
                }
            }
        }

        /// <summary>Der zuletzt gemeldete Zählstand; 0, wenn noch nichts gemeldet wurde.</summary>
        public int LastProcessed
        {
            get
            {
                lock (_gate)
                {
                    return _reports.Count == 0 ? 0 : _reports[^1].Processed;
                }
            }
        }

        public void Report(PhotoScanProgress value)
        {
            // Die Wartezeit liegt vor dem Eintragen: Nur so entsteht ein Zeitfenster, in
            // dem die Meldung noch nicht sichtbar ist. Reicht die Quelle das Bild vor dem
            // Melden weiter, sieht der Verbraucher es genau in diesem Fenster — und der
            // Test schlägt fehl, wie er soll.
            if (delay > TimeSpan.Zero)
            {
                Thread.Sleep(delay);
            }

            lock (_gate)
            {
                _reports.Add(value);
            }
        }
    }

    // Wartet kurz und hält fest, wie viele Zugriffe dabei gleichzeitig offen waren. Nur
    // so lässt sich belegen, dass wirklich parallel gelesen wird – die Zahl der
    // zurückgegebenen Fotos sähe auch bei sequentiellem Einlesen richtig aus.
    private sealed class ConcurrencyTrackingMetadataReader(Func<string, TimeSpan>? delay = null)
        : IImageMetadataReader
    {
        private readonly Lock _gate = new();
        private int _current;

        public int MaxConcurrent { get; private set; }

        public async Task<PhotoMetadata?> ReadAsync(string filePath, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                _current++;
                MaxConcurrent = Math.Max(MaxConcurrent, _current);
            }

            try
            {
                await Task.Delay(delay?.Invoke(filePath) ?? TimeSpan.FromMilliseconds(30), cancellationToken)
                    .ConfigureAwait(false);
                return null;
            }
            finally
            {
                lock (_gate)
                {
                    _current--;
                }
            }
        }
    }

    private sealed class NullMetadataReader : IImageMetadataReader
    {
        public Task<PhotoMetadata?> ReadAsync(string filePath, CancellationToken cancellationToken) =>
            Task.FromResult<PhotoMetadata?>(null);
    }

    // Löscht beim Lesen genau die eine Datei und trifft damit das reale Zeitfenster:
    // Die Quelle hat sie bereits aufgelistet und fragt danach ihre Größe ab.
    private sealed class DeletingMetadataReader(string pathToDelete) : IImageMetadataReader
    {
        public Task<PhotoMetadata?> ReadAsync(string filePath, CancellationToken cancellationToken)
        {
            if (string.Equals(filePath, pathToDelete, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(filePath);
            }

            return Task.FromResult<PhotoMetadata?>(null);
        }
    }

    // Zählt die Zugriffe auf die Metadaten. Nur so lässt sich belegen, dass wirklich
    // weniger Dateien geöffnet werden – die Zahl der zurückgegebenen Fotos allein
    // sähe auch dann richtig aus, wenn erst hinterher abgeschnitten würde.
    private sealed class CountingMetadataReader : IImageMetadataReader
    {
        private int _callCount;

        // Interlocked, weil die Quelle mehrere Dateien gleichzeitig einliest: Ein
        // gewöhnliches ++ verlöre unter Last Zählschritte und der Test meldete einen
        // Fehler, den es nicht gibt.
        public int CallCount => Volatile.Read(ref _callCount);

        public Task<PhotoMetadata?> ReadAsync(string filePath, CancellationToken cancellationToken)
        {
            _ = Interlocked.Increment(ref _callCount);
            return Task.FromResult<PhotoMetadata?>(null);
        }
    }
}
