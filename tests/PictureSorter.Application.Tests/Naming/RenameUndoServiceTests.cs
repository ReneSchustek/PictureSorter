using Microsoft.Extensions.Logging.Abstractions;
using PictureSorter.Application.Naming;
using PictureSorter.Application.Tests.Fakes;
using PictureSorter.Core.Enums;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Application.Tests.Naming;

/// <summary>
/// Tests des Zurücknehmens. Es wiegt hier schwerer als beim Sortieren: Nach einem
/// Umbenennen ist der alte Name fort, und niemand könnte ihn aus dem Kopf
/// wiederherstellen. Er steht nur noch im Protokoll.
/// </summary>
public sealed class RenameUndoServiceTests
{
    private const string Folder = @"C:\Fotos";

    [Fact]
    public async Task UndoLastRunAsync_WithoutARunAnswersWithNothing()
    {
        RenameUndoService sut = CreateSut(out _, out _);

        Assert.Null(await sut.UndoLastRunAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UndoLastRunAsync_GivesEveryFileItsOldNameBack()
    {
        RenameUndoService sut = CreateSut(out FakePhotoFileEditor editor, out FakeRenameJournal journal);
        journal.Runs.Add(Run(Guid.NewGuid(), ("kaputt.jpg", "neu.jpg"), ("auch-kaputt.jpg", "neu-2.jpg")));

        UndoResult? result = await sut.UndoLastRunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, result!.Restored);
        Assert.Equal(0, result.Skipped);
        Assert.Equal(
            [(Path.Combine(Folder, "neu.jpg"), "kaputt.jpg"), (Path.Combine(Folder, "neu-2.jpg"), "auch-kaputt.jpg")],
            editor.Renames);
    }

    [Fact]
    public async Task UndoLastRunAsync_MarksTheRunSoItIsNotOfferedTwice()
    {
        RenameUndoService sut = CreateSut(out _, out FakeRenameJournal journal);
        Guid runId = Guid.NewGuid();
        journal.Runs.Add(Run(runId, ("kaputt.jpg", "neu.jpg")));

        _ = await sut.UndoLastRunAsync(TestContext.Current.CancellationToken);

        Assert.Contains(runId, journal.Undone);
        Assert.Null(await sut.GetUndoableRunAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UndoLastRunAsync_WhenOneNameIsTakenAgainKeepsTheRest()
    {
        // Liegt unter dem alten Namen inzwischen wieder etwas, bleibt die Datei, wie
        // sie ist. Überschrieben wird nie — auch nicht beim Zurücknehmen.
        RenameUndoService sut = CreateSut(out FakePhotoFileEditor editor, out FakeRenameJournal journal);
        editor.FailFor(Path.Combine(Folder, "neu.jpg"), FileEditOutcome.NameTaken);
        journal.Runs.Add(Run(Guid.NewGuid(), ("kaputt.jpg", "neu.jpg"), ("auch-kaputt.jpg", "neu-2.jpg")));

        UndoResult? result = await sut.UndoLastRunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, result!.Restored);
        Assert.Equal(1, result.Skipped);
    }

    [Fact]
    public async Task UndoLastRunAsync_EvenWithHindrancesCountsAsDone()
    {
        // Ein zweiter Versuch scheiterte an denselben Hindernissen und führte die
        // Nutzerin nur erneut in die Irre.
        RenameUndoService sut = CreateSut(out FakePhotoFileEditor editor, out FakeRenameJournal journal);
        editor.FailFor(Path.Combine(Folder, "neu.jpg"), FileEditOutcome.Locked);
        Guid runId = Guid.NewGuid();
        journal.Runs.Add(Run(runId, ("kaputt.jpg", "neu.jpg")));

        _ = await sut.UndoLastRunAsync(TestContext.Current.CancellationToken);

        Assert.Contains(runId, journal.Undone);
    }

    private static RenameRun Run(Guid id, params (string Original, string New)[] items) => new()
    {
        Id = id,
        StartedAt = new DateTimeOffset(2026, 8, 23, 12, 0, 0, TimeSpan.Zero),
        Folder = Folder,
        Items =
        [
            .. items.Select(item => new RenameRunItem
            {
                OriginalPath = Path.Combine(Folder, item.Original),
                NewPath = Path.Combine(Folder, item.New),
            }),
        ],
    };

    private static RenameUndoService CreateSut(out FakePhotoFileEditor editor, out FakeRenameJournal journal)
    {
        editor = new FakePhotoFileEditor();
        journal = new FakeRenameJournal();

        return new RenameUndoService(journal, editor, NullLogger<RenameUndoService>.Instance);
    }
}
