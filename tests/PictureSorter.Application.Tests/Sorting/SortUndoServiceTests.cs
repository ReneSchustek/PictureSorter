using Microsoft.Extensions.Logging.Abstractions;
using PictureSorter.Application.Sorting;
using PictureSorter.Application.Tests.Fakes;
using PictureSorter.Core.Enums;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Application.Tests.Sorting;

/// <summary>
/// Tests des Rückgängigmachens. Ein Sortierlauf verschiebt die Fotos der Nutzerin –
/// es ist der einzige Schritt der Anwendung, der ihre Dateien wirklich bewegt, und
/// er muss umkehrbar sein. Genauso wichtig ist die Gegenprobe: Beim Zurückholen darf
/// nichts überschrieben werden, sonst wäre ausgerechnet das Rückgängig die Stelle,
/// an der Daten verloren gehen.
/// </summary>
public sealed class SortUndoServiceTests
{
    private const string SourceFolder = @"C:\fotos";
    private const string TargetFolder = @"C:\fotos\Familie";
    private const string Category = "Familie";

    [Fact]
    public async Task UndoLastRunAsync_MovesEveryFileBackToItsOriginalPath()
    {
        FakeFileOrganizer organizer = new();
        FakeSortJournal journal = Seeded("a.jpg", "b.jpg");
        SortUndoService sut = CreateSut(journal, organizer, new FakeSortMemory());

        UndoResult? result = await sut.UndoLastRunAsync(CancellationToken.None);

        Assert.Equal(2, result!.Restored);
        Assert.Equal(0, result.Skipped);
        Assert.Equal(
            [
                (Path.Combine(TargetFolder, "a.jpg"), Path.Combine(SourceFolder, "a.jpg")),
                (Path.Combine(TargetFolder, "b.jpg"), Path.Combine(SourceFolder, "b.jpg")),
            ],
            organizer.Restored);
    }

    [Fact]
    public async Task UndoLastRunAsync_ForgetsThatThePhotosWereSorted()
    {
        // Bliebe der Eintrag „einsortiert" stehen, würde das Foto nie wieder
        // vorgeschlagen – die Anwendung hätte ein Gedächtnis an eine Sortierung, die
        // es gar nicht mehr gibt.
        FakeSortMemory memory = new();
        memory.Records.Add(CreateRecord("sig-a"));
        FakeSortJournal journal = Seeded("a.jpg");
        SortUndoService sut = CreateSut(journal, new FakeFileOrganizer(), memory);

        _ = await sut.UndoLastRunAsync(CancellationToken.None);

        Assert.Empty(memory.Records);
    }

    [Fact]
    public async Task UndoLastRunAsync_WhenAFileCannotBeRestored_KeepsItRememberedAndCountsIt()
    {
        // Die Datei liegt nicht mehr am Zielort oder ihr Ursprungsort ist wieder
        // belegt. Sie bleibt, wo sie ist – und weil sie dort bleibt, bleibt sie auch
        // als einsortiert gemerkt. Die Nutzerin erfährt über die Zahl, dass nicht
        // alles zurückkam.
        FakeSortMemory memory = new();
        memory.Records.Add(CreateRecord("sig-a"));
        FakeFileOrganizer organizer = new();
        _ = organizer.Unrestorable.Add(Path.Combine(TargetFolder, "a.jpg"));
        SortUndoService sut = CreateSut(Seeded("a.jpg"), organizer, memory);

        UndoResult? result = await sut.UndoLastRunAsync(CancellationToken.None);

        Assert.Equal(0, result!.Restored);
        Assert.Equal(1, result.Skipped);
        _ = Assert.Single(memory.Records);
    }

    [Fact]
    public async Task UndoLastRunAsync_WhenAFileIsLocked_ContinuesWithTheRest()
    {
        // Eine gesperrte Datei darf nicht verhindern, dass die übrigen zurückkommen.
        FakeSortJournal journal = Seeded("foto0.jpg", "foto1.jpg", "foto2.jpg");
        SortUndoService sut = CreateSut(journal, new FailingFileOrganizer(failOn: "foto1.jpg"), new FakeSortMemory());

        UndoResult? result = await sut.UndoLastRunAsync(CancellationToken.None);

        Assert.Equal(2, result!.Restored);
        Assert.Equal(1, result.Skipped);
    }

    [Fact]
    public async Task UndoLastRunAsync_RemovesTheEmptyCategoryFolder()
    {
        // Der leere Kategorie-Ordner soll nicht zurückbleiben; er weckte den Eindruck,
        // es sei noch etwas einsortiert.
        FakeFileOrganizer organizer = new();
        SortUndoService sut = CreateSut(Seeded("a.jpg", "b.jpg"), organizer, new FakeSortMemory());

        _ = await sut.UndoLastRunAsync(CancellationToken.None);

        Assert.Equal(TargetFolder, Assert.Single(organizer.CheckedFolders));
    }

    [Fact]
    public async Task UndoLastRunAsync_MarksTheRunAsUndone_SoItIsNotOfferedAgain()
    {
        FakeSortJournal journal = Seeded("a.jpg");
        SortUndoService sut = CreateSut(journal, new FakeFileOrganizer(), new FakeSortMemory());

        _ = await sut.UndoLastRunAsync(CancellationToken.None);

        Assert.Null(await sut.GetUndoableRunAsync(CancellationToken.None));
        _ = Assert.Single(journal.Undone);
    }

    [Fact]
    public async Task UndoLastRunAsync_WithoutAnyRun_DoesNothing()
    {
        SortUndoService sut = CreateSut(new FakeSortJournal(), new FakeFileOrganizer(), new FakeSortMemory());

        Assert.Null(await sut.UndoLastRunAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GetUndoableRunAsync_ReturnsTheMostRecentRun()
    {
        FakeSortJournal journal = new();
        await journal.RecordAsync(CreateRun("alt.jpg"), CancellationToken.None);
        await journal.RecordAsync(CreateRun("neu.jpg"), CancellationToken.None);
        SortUndoService sut = CreateSut(journal, new FakeFileOrganizer(), new FakeSortMemory());

        SortRun? run = await sut.GetUndoableRunAsync(CancellationToken.None);

        Assert.Equal("neu.jpg", Path.GetFileName(Assert.Single(run!.Items).SourcePath));
    }

    private static SortUndoService CreateSut(
        FakeSortJournal journal,
        Core.Interfaces.IFileOrganizer organizer,
        FakeSortMemory memory) => new(
            new SortJournalGateway(journal, NullLogger<SortJournalGateway>.Instance),
            new SortMemoryGateway(
                memory,
                new FakeClock(new DateTimeOffset(2026, 7, 1, 8, 0, 0, TimeSpan.Zero)),
                NullLogger<SortMemoryGateway>.Instance),
            organizer,
            NullLogger<SortUndoService>.Instance);

    [Fact]
    public async Task UndoLastRunAsync_AfterACopyRun_RemovesTheCopiesInsteadOfMovingThemBack()
    {
        // Nach einem Kopierlauf liegt das Original noch im Quellordner. „Zurückholen"
        // hieße dort, es ein zweites Mal hinzulegen – rückgängig ist hier das
        // Entfernen der Kopie.
        FakeFileOrganizer organizer = new();
        FakeSortJournal journal = SeededCopyRun("a.jpg", "b.jpg");
        SortUndoService sut = CreateSut(journal, organizer, new FakeSortMemory());

        UndoResult? result = await sut.UndoLastRunAsync(CancellationToken.None);

        Assert.Equal(2, result!.Restored);
        Assert.Empty(organizer.Restored);
        Assert.Equal(
            [Path.Combine(TargetFolder, "a.jpg"), Path.Combine(TargetFolder, "b.jpg")],
            organizer.Discarded);
    }

    [Fact]
    public async Task UndoLastRunAsync_AfterACopyRun_KeepsACopyThatWasEditedAndCountsIt()
    {
        FakeFileOrganizer organizer = new();
        _ = organizer.Undiscardable.Add(Path.Combine(TargetFolder, "b.jpg"));
        FakeSortJournal journal = SeededCopyRun("a.jpg", "b.jpg");
        SortUndoService sut = CreateSut(journal, organizer, new FakeSortMemory());

        UndoResult? result = await sut.UndoLastRunAsync(CancellationToken.None);

        Assert.Equal(1, result!.Restored);
        Assert.Equal(1, result.Skipped);
        Assert.Equal([Path.Combine(TargetFolder, "a.jpg")], organizer.Discarded);
    }

    private static FakeSortJournal Seeded(params string[] fileNames)
    {
        FakeSortJournal journal = new();
        journal.Runs.Add(CreateRun(fileNames));
        return journal;
    }

    private static FakeSortJournal SeededCopyRun(params string[] fileNames)
    {
        FakeSortJournal journal = new();
        SortRun run = CreateRun(fileNames);
        journal.Runs.Add(run with
        {
            Operation = FileOperationMode.Copy,
            Items =
            [
                .. run.Items.Select(item => item with
                {
                    TargetLength = 1024,
                    TargetLastWriteUtc = new DateTime(2026, 7, 1, 8, 0, 0, DateTimeKind.Utc),
                }),
            ],
        });
        return journal;
    }

    private static SortRun CreateRun(params string[] fileNames) => new()
    {
        Id = Guid.NewGuid(),
        StartedAt = new DateTimeOffset(2026, 7, 1, 8, 0, 0, TimeSpan.Zero),
        SourceFolder = SourceFolder,
        CategoryName = Category,
        Items =
        [
            .. fileNames.Select(name => new SortRunItem
            {
                SourcePath = Path.Combine(SourceFolder, name),
                TargetPath = Path.Combine(TargetFolder, name),
                FileSignature = $"sig-{Path.GetFileNameWithoutExtension(name)}",
            }),
        ],
    };

    private static SortMemoryRecord CreateRecord(string signature) => new()
    {
        FolderPath = SourceFolder,
        FileSignature = signature,
        PhotoPath = Path.Combine(TargetFolder, "a.jpg"),
        CategoryName = Category,
        Status = SortMemoryStatus.Sorted,
        Confidence = 1.0,
        UpdatedAt = new DateTimeOffset(2026, 7, 1, 8, 0, 0, TimeSpan.Zero),
    };
}
