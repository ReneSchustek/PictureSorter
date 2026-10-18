using Microsoft.Extensions.Logging.Abstractions;
using PictureSorter.Application.Naming;
using PictureSorter.Application.Tests.Fakes;
using PictureSorter.Core.Entities;
using PictureSorter.Core.Enums;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Application.Tests.Naming;

/// <summary>
/// Tests der Namensreparatur. Sie ändert Dateinamen, das Einzige, was die Nutzerin
/// im Explorer von einem Foto sieht. Geprüft wird deshalb beides: dass nur angefasst
/// wird, was wirklich unlesbar ist, und dass jeder Schritt im Protokoll steht.
/// </summary>
public sealed class FileNameRepairServiceTests
{
    private const string Folder = @"C:\Fotos";
    private static readonly DateTimeOffset Now = new(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ProposeAsync_LeavesReadableNamesAlone()
    {
        // Der weitaus größte Teil eines Bestands ist in Ordnung: 1348 von 1507 Dateien
        // im gemessenen Fall. Sie dürfen gar nicht erst in der Vorschau auftauchen.
        FileNameRepairService sut = CreateSut(out _, out _, [Photo("IMG_2041.jpg"), Photo("Urlaub 2023.jpeg")]);

        IReadOnlyList<RenameProposal> proposals =
            await sut.ProposeAsync(Folder, includeSubfolders: false, progress: null, TestContext.Current.CancellationToken);

        Assert.Empty(proposals);
    }

    [Fact]
    public async Task ProposeAsync_UsesTheCaptureDateWhenThereIsOne()
    {
        FileNameRepairService sut = CreateSut(
            out _,
            out _,
            [Photo("\uFFFD\uFFFD.JPEG", new DateTimeOffset(2021, 7, 14, 19, 42, 3, TimeSpan.Zero))]);

        RenameProposal proposal = Assert.Single(
            await sut.ProposeAsync(Folder, includeSubfolders: false, progress: null, TestContext.Current.CancellationToken));

        Assert.Equal("2021-07-14_19-42-03.JPEG", proposal.SuggestedName);
        Assert.Equal(RenameSource.CaptureDate, proposal.Source);
    }

    [Fact]
    public async Task ProposeAsync_WithoutACaptureDateNumbersThemInOrder()
    {
        // Keine der 159 kaputt benannten Dateien des gemessenen Bestands trug ein
        // Aufnahmedatum. Der Fall ist also nicht die Ausnahme, sondern die Regel.
        FileNameRepairService sut = CreateSut(out _, out _, [Photo("\uFFFDa.jpg"), Photo("\uFFFDb.jpg")]);

        IReadOnlyList<RenameProposal> proposals =
            await sut.ProposeAsync(Folder, includeSubfolders: false, progress: null, TestContext.Current.CancellationToken);

        Assert.Equal(["Fotos 001.jpg", "Fotos 002.jpg"], proposals.Select(item => item.SuggestedName));
        Assert.All(proposals, item => Assert.Equal(RenameSource.FolderAndNumber, item.Source));
    }

    [Fact]
    public async Task ProposeAsync_CountsOnlyWhereTheNumberIsActuallyUsed()
    {
        // Zwischen zwei Dateien ohne Datum liegt eine mit. Zählte die Nummer auch für
        // sie mit, hieße die letzte „002" und es fehlte ein „001" davor. Das sieht
        // aus, als sei etwas verlorengegangen.
        FileNameRepairService sut = CreateSut(
            out _,
            out _,
            [
                Photo("�a.jpg"),
                Photo("�b.jpg", new DateTimeOffset(2021, 7, 14, 19, 42, 3, TimeSpan.Zero)),
                Photo("�c.jpg"),
            ]);

        IReadOnlyList<RenameProposal> proposals =
            await sut.ProposeAsync(Folder, includeSubfolders: false, progress: null, TestContext.Current.CancellationToken);

        Assert.Equal(
            ["Fotos 001.jpg", "2021-07-14_19-42-03.jpg", "Fotos 002.jpg"],
            proposals.Select(item => item.SuggestedName));
    }

    [Fact]
    public async Task ProposeAsync_DoesNotSuggestANameThatIsAlreadyTaken()
    {
        // Der vorgeschlagene Name darf keine vorhandene Datei treffen, sonst
        // scheiterte die Umbenennung, oder schlimmer: Sie überschriebe ein Foto.
        FileNameRepairService sut = CreateSut(
            out _,
            out _,
            [
                Photo("2021-07-14_19-42-03.jpg"),
                Photo("\uFFFD.jpg", new DateTimeOffset(2021, 7, 14, 19, 42, 3, TimeSpan.Zero)),
            ]);

        RenameProposal proposal = Assert.Single(
            await sut.ProposeAsync(Folder, includeSubfolders: false, progress: null, TestContext.Current.CancellationToken));

        Assert.Equal("2021-07-14_19-42-03-2.jpg", proposal.SuggestedName);
    }

    [Fact]
    public async Task ProposeAsync_LeavesFilesWithAnUnreadableExtensionUntouched()
    {
        // Aus einer kaputten Endung ließe sich das Format nur raten, und eine geratene
        // Endung macht die Datei mit einem Klick unbrauchbar.
        FileNameRepairService sut = CreateSut(out _, out _, [Photo("bild.j\uFFFDg")]);

        Assert.Empty(
            await sut.ProposeAsync(Folder, includeSubfolders: false, progress: null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ApplyAsync_RenamesAndRecordsBothNames()
    {
        // Der alte Name ist nach dem Umbenennen nirgends mehr zu finden. Steht er nicht
        // im Protokoll, ist der Schritt unumkehrbar.
        FileNameRepairService sut = CreateSut(out FakePhotoFileEditor editor, out FakeRenameJournal journal, []);
        RenameProposal proposal = new()
        {
            FilePath = Path.Combine(Folder, "kaputt.jpg"),
            CurrentName = "kaputt.jpg",
            SuggestedName = "2021-07-14_19-42-03.jpg",
            Source = RenameSource.CaptureDate,
        };

        RenameResult result = await sut.ApplyAsync(Folder, [proposal], progress: null, TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Renamed);
        Assert.Equal(0, result.Skipped);
        _ = Assert.Single(editor.Renames);

        RenameRun run = Assert.Single(journal.Runs);
        RenameRunItem item = Assert.Single(run.Items);
        Assert.Equal(Path.Combine(Folder, "kaputt.jpg"), item.OriginalPath);
        Assert.Equal(Path.Combine(Folder, "2021-07-14_19-42-03.jpg"), item.NewPath);
        Assert.Equal(Now, run.StartedAt);
    }

    [Fact]
    public async Task ApplyAsync_WhenOneFileResistsTheOthersStillGetTheirName()
    {
        // Eine geöffnete Datei ist Alltag. Die übrigen können nichts dafür.
        FileNameRepairService sut = CreateSut(out FakePhotoFileEditor editor, out FakeRenameJournal journal, []);
        editor.FailFor(Path.Combine(Folder, "a.jpg"), FileEditOutcome.Locked);

        RenameResult result = await sut.ApplyAsync(
            Folder,
            [Proposal("a.jpg", "neu-a.jpg"), Proposal("b.jpg", "neu-b.jpg")],
            progress: null,
            TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Renamed);
        Assert.Equal(1, result.Skipped);
        _ = Assert.Single(Assert.Single(journal.Runs).Items);
    }

    [Fact]
    public async Task ApplyAsync_WhenNothingSucceededWritesNoRun()
    {
        // Ein leerer Eintrag verdeckte im Angebot den letzten Lauf, der wirklich etwas
        // geändert hat, und „Rückgängig" täte dann nichts.
        FileNameRepairService sut = CreateSut(out FakePhotoFileEditor editor, out FakeRenameJournal journal, []);
        editor.FailFor(Path.Combine(Folder, "a.jpg"), FileEditOutcome.NameTaken);

        RenameResult result = await sut.ApplyAsync(
            Folder,
            [Proposal("a.jpg", "neu-a.jpg")],
            progress: null,
            TestContext.Current.CancellationToken);

        Assert.Equal(0, result.Renamed);
        Assert.Empty(journal.Runs);
    }

    [Fact]
    public async Task ApplyAsync_ReportsItsProgress()
    {
        // Ein Lauf über hunderte Dateien ohne sichtbaren Fortschritt sieht aus wie ein
        // hängendes Programm.
        FileNameRepairService sut = CreateSut(out _, out _, []);
        RecordingProgress<PhotoScanProgress> progress = new();

        _ = await sut.ApplyAsync(
            Folder,
            [Proposal("a.jpg", "neu-a.jpg"), Proposal("b.jpg", "neu-b.jpg")],
            progress,
            TestContext.Current.CancellationToken);

        Assert.Equal([new PhotoScanProgress(1, 2), new PhotoScanProgress(2, 2)], progress.Reports);
    }

    [Fact]
    public async Task ProposeAsync_WithoutAFolderIsRejected()
    {
        FileNameRepairService sut = CreateSut(out _, out _, []);

        _ = await Assert.ThrowsAsync<ArgumentException>(
            () => sut.ProposeAsync(" ", includeSubfolders: false, progress: null, TestContext.Current.CancellationToken));
    }

    private static RenameProposal Proposal(string current, string suggestion) => new()
    {
        FilePath = Path.Combine(Folder, current),
        CurrentName = current,
        SuggestedName = suggestion,
        Source = RenameSource.FolderAndNumber,
    };

    private static Photo Photo(string fileName, DateTimeOffset? capturedAt = null) => new()
    {
        FullPath = Path.Combine(Folder, fileName),
        FileName = fileName,
        CapturedAt = capturedAt,
    };

    private static FileNameRepairService CreateSut(
        out FakePhotoFileEditor editor,
        out FakeRenameJournal journal,
        IReadOnlyList<Photo> photos)
    {
        editor = new FakePhotoFileEditor();
        journal = new FakeRenameJournal();

        return new FileNameRepairService(
            new FakePhotoSource(photos),
            new FakeMetadataReader(photos),
            editor,
            journal,
            new FakeClock(Now),
            NullLogger<FileNameRepairService>.Instance);
    }
}
