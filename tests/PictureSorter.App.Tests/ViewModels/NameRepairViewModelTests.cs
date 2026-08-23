using Microsoft.Extensions.Logging.Abstractions;
using PictureSorter.App.Tests.Fakes;
using PictureSorter.App.ViewModels;
using PictureSorter.Core.Enums;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.App.Tests.ViewModels;

/// <summary>
/// Tests der Namensreparatur-Ansicht. Sie ändert Dateinamen — geprüft wird deshalb
/// vor allem, dass nichts ohne Bestätigung geschieht und dass das Angebot zum
/// Zurücknehmen erscheint, sobald es etwas zurückzunehmen gibt.
/// </summary>
public sealed class NameRepairViewModelTests
{
    private const string Folder = @"C:\Fotos";

    [Fact]
    public async Task Scan_ShowsThePreviewAndNothingElseHappens()
    {
        // Nichts wird verändert, bevor die Nutzerin es bestätigt: Nach dem Prüfen
        // steht eine Vorschau, und keine einzige Datei ist angefasst.
        FakeFileNameRepairService repair = new([Proposal("kaputt.jpg", "2021-07-14_19-42-03.jpg")]);
        using NameRepairViewModel sut = CreateSut(repair);
        sut.SourceFolder = Folder;

        await sut.ScanCommand.ExecuteAsync(parameter: null);

        Assert.True(sut.HasPreview);
        Assert.False(sut.ShowsEmptyState);
        _ = Assert.Single(sut.Items);
        Assert.Empty(repair.Applied);
    }

    [Fact]
    public async Task Scan_WithoutAnyFindingSaysSoInsteadOfShowingAnEmptyList()
    {
        FakeFileNameRepairService repair = new([]);
        using NameRepairViewModel sut = CreateSut(repair);
        sut.SourceFolder = Folder;

        await sut.ScanCommand.ExecuteAsync(parameter: null);

        Assert.False(sut.HasPreview);
        Assert.Equal(SortState.Completed, sut.State);
    }

    [Fact]
    public async Task Scan_MarksEveryFindingButLeavesTheChoice()
    {
        FakeFileNameRepairService repair = new(
            [Proposal("a.jpg", "neu-a.jpg"), Proposal("b.jpg", "neu-b.jpg")]);
        using NameRepairViewModel sut = CreateSut(repair);
        sut.SourceFolder = Folder;

        await sut.ScanCommand.ExecuteAsync(parameter: null);
        sut.Items[0].IsSelected = false;

        Assert.Equal(1, sut.SelectedCount);
        Assert.True(sut.CanApply);
    }

    [Fact]
    public async Task Apply_WhenNothingIsSelectedIsNotPossible()
    {
        FakeFileNameRepairService repair = new([Proposal("a.jpg", "neu-a.jpg")]);
        using NameRepairViewModel sut = CreateSut(repair);
        sut.SourceFolder = Folder;
        await sut.ScanCommand.ExecuteAsync(parameter: null);

        sut.Items[0].IsSelected = false;

        Assert.False(sut.CanApply);
    }

    [Fact]
    public async Task Apply_WithoutConfirmationChangesNothing()
    {
        // Ein Name ist alles, was die Nutzerin im Explorer von einem Foto sieht. Wer
        // die Rückfrage verneint, erwartet, dass wirklich nichts geschieht.
        FakeFileNameRepairService repair = new([Proposal("a.jpg", "neu-a.jpg")]);
        using NameRepairViewModel sut = CreateSut(repair, confirms: false);
        sut.SourceFolder = Folder;
        await sut.ScanCommand.ExecuteAsync(parameter: null);

        await sut.ApplyCommand.ExecuteAsync(parameter: null);

        Assert.Empty(repair.Applied);
        Assert.True(sut.HasPreview);
    }

    [Fact]
    public async Task Apply_RenamesOnlyWhatIsSelected()
    {
        FakeFileNameRepairService repair = new(
            [Proposal("a.jpg", "neu-a.jpg"), Proposal("b.jpg", "neu-b.jpg")]);
        using NameRepairViewModel sut = CreateSut(repair);
        sut.SourceFolder = Folder;
        await sut.ScanCommand.ExecuteAsync(parameter: null);
        sut.Items[0].IsSelected = false;

        await sut.ApplyCommand.ExecuteAsync(parameter: null);

        RenameProposal applied = Assert.Single(repair.Applied);
        Assert.Equal("neu-b.jpg", applied.SuggestedName);
    }

    [Fact]
    public async Task Apply_ThenTheUndoOfferStands()
    {
        // Nach dem Lauf ist die Vorschau leer. Wer ihn bereut, muss den Weg zurück
        // trotzdem finden.
        FakeFileNameRepairService repair = new([Proposal("a.jpg", "neu-a.jpg")]);
        FakeRenameUndoService undo = new();
        using NameRepairViewModel sut = CreateSut(repair, undo: undo);
        sut.SourceFolder = Folder;
        await sut.ScanCommand.ExecuteAsync(parameter: null);

        undo.Run = CreateRun();
        await sut.ApplyCommand.ExecuteAsync(parameter: null);

        Assert.True(sut.HasUndoableRun);
        Assert.Contains(Folder, sut.UndoSummary, StringComparison.Ordinal);
        Assert.Empty(sut.Items);
    }

    [Fact]
    public async Task Undo_GivesTheOldNamesBackAndClearsTheOffer()
    {
        FakeRenameUndoService undo = new() { Run = CreateRun() };
        using NameRepairViewModel sut = CreateSut(new FakeFileNameRepairService([]), undo: undo);
        await sut.RefreshUndoOfferAsync();
        Assert.True(sut.CanUndo);

        await sut.UndoCommand.ExecuteAsync(parameter: null);

        Assert.True(undo.WasUndone);
        Assert.False(sut.HasUndoableRun);
    }

    [Fact]
    public async Task ToggleAll_SwitchesEveryLineAtOnce()
    {
        // Bei 159 Zeilen ist das Häkchen einzeln zu setzen keine Bedienung, sondern
        // eine Zumutung.
        FakeFileNameRepairService repair = new(
            [Proposal("a.jpg", "neu-a.jpg"), Proposal("b.jpg", "neu-b.jpg")]);
        using NameRepairViewModel sut = CreateSut(repair);
        sut.SourceFolder = Folder;
        await sut.ScanCommand.ExecuteAsync(parameter: null);

        sut.ToggleAllCommand.Execute(parameter: null);
        Assert.Equal(0, sut.SelectedCount);

        sut.ToggleAllCommand.Execute(parameter: null);
        Assert.Equal(2, sut.SelectedCount);
    }

    [Fact]
    public async Task Scan_WhenTheFolderIsMissingNamesTheOneThatIsMeant()
    {
        // Wer unlesbare Namen sucht, hat einen Bestand, in dem auch der Ordnername
        // unsichtbare Zeichen tragen kann — der Fall ist hier besonders wahrscheinlich.
        string meant = "E:\\Fotos\\iCloud\u00a0Fotos";
        StatusBarViewModel status = new(new ReswLocalizer());
        using NameRepairViewModel sut = CreateSut(
            new FailingFileNameRepairService(new DirectoryNotFoundException()),
            lookalike: meant,
            status: status);
        sut.SourceFolder = "E:\\Fotos\\iCloud Fotos";

        await sut.ScanCommand.ExecuteAsync(parameter: null);

        Assert.Equal(SortState.Error, sut.State);
        Assert.Contains(meant, status.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Scan_ReportsAFailureInsteadOfBringingTheAppDown()
    {
        StatusBarViewModel status = new(new ReswLocalizer());
        using NameRepairViewModel sut = CreateSut(
            new FailingFileNameRepairService(new IOException("Platte weg")),
            status: status);
        sut.SourceFolder = Folder;

        await sut.ScanCommand.ExecuteAsync(parameter: null);

        Assert.Equal(SortState.Error, sut.State);
        Assert.Contains("Platte weg", status.Message, StringComparison.Ordinal);
    }

    private static RenameProposal Proposal(string current, string suggestion) => new()
    {
        FilePath = Path.Combine(Folder, current),
        CurrentName = current,
        SuggestedName = suggestion,
        Source = RenameSource.CaptureDate,
    };

    private static RenameRun CreateRun() => new()
    {
        Id = Guid.NewGuid(),
        StartedAt = DateTimeOffset.UtcNow,
        Folder = Folder,
        Items = [new RenameRunItem { OriginalPath = @"C:\Fotos\a.jpg", NewPath = @"C:\Fotos\neu-a.jpg" }],
    };

    private static NameRepairViewModel CreateSut(
        IFileNameRepairService repair,
        FakeRenameUndoService? undo = null,
        bool confirms = true,
        string? lookalike = null,
        StatusBarViewModel? status = null)
    {
        ReswLocalizer localizer = new();

        return new NameRepairViewModel(
            repair,
            undo ?? new FakeRenameUndoService(),
            new FakeFolderPicker(Folder),
            new FakeFolderLookup(lookalike),
            new StubConfirmationService(confirms),
            status ?? new StatusBarViewModel(localizer),
            localizer,
            NullLogger<NameRepairViewModel>.Instance);
    }
}
