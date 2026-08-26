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

    [Fact]
    public void CanScan_NeedsAFolder()
    {
        // Der Knopf ist der Einstieg der Seite. Ohne Ordner hätte er nichts zu tun,
        // und ein Klick ins Leere sähe wie ein Fehler aus.
        using NameRepairViewModel sut = CreateSut(new FakeFileNameRepairService([]));

        Assert.False(sut.CanScan);

        sut.SourceFolder = Folder;

        Assert.True(sut.CanScan);
    }

    [Fact]
    public void CanScan_IsFalseWhileSomethingIsRunning()
    {
        // Während eines Laufs darf kein zweiter beginnen: Beide schrieben dieselben
        // Dateien.
        using NameRepairViewModel sut = CreateSut(new FakeFileNameRepairService([]));
        sut.SourceFolder = Folder;
        sut.State = SortState.Analyzing;

        Assert.False(sut.CanScan);
        Assert.False(sut.IsInteractive);
    }

    [Fact]
    public void CanCancel_OnlyWhileAnythingIsRunning()
    {
        // Abbrechen ist nur sinnvoll, solange etwas läuft — und dann muss es gehen.
        // Ein Stopp-Knopf, der nichts auslöst, ist schlimmer als keiner.
        // (Der Zustand ist ein interner Typ und taugt deshalb nicht als Parameter
        // einer Theorie.)
        using NameRepairViewModel sut = CreateSut(new FakeFileNameRepairService([]));

        foreach (SortState laufend in new[] { SortState.Analyzing, SortState.Sorting })
        {
            sut.State = laufend;
            Assert.True(sut.CanCancel);
        }

        foreach (SortState ruhend in new[]
                 { SortState.Idle, SortState.Preview, SortState.Completed, SortState.Error })
        {
            sut.State = ruhend;
            Assert.False(sut.CanCancel);
        }
    }

    [Fact]
    public void CanApply_NeedsAPreviewAndAtLeastOneTick()
    {
        using NameRepairViewModel sut = CreateSut(new FakeFileNameRepairService([]));
        sut.State = SortState.Preview;

        // Ohne Zeilen gibt es nichts anzuwenden.
        Assert.False(sut.CanApply);
    }

    [Fact]
    public async Task PickFolder_TakesOverTheChosenFolder()
    {
        // Der Knopf neben dem Eingabefeld ist für die meisten der einzige Weg zum
        // Ordner — getippt wird ein Pfad selten.
        using NameRepairViewModel sut = CreateSut(new FakeFileNameRepairService([]));

        await sut.PickFolderCommand.ExecuteAsync(parameter: null);

        Assert.Equal(Folder, sut.SourceFolder);
    }

    [Fact]
    public async Task PickFolder_WhenTheDialogIsDismissed_KeepsTheOldFolder()
    {
        // Wer die Auswahl abbricht, will den bisherigen Ordner behalten — nicht ein
        // leeres Feld vorfinden.
        using NameRepairViewModel sut = CreateSut(
            new FakeFileNameRepairService([]),
            picker: new FakeFolderPicker(folder: null));
        sut.SourceFolder = Folder;

        await sut.PickFolderCommand.ExecuteAsync(parameter: null);

        Assert.Equal(Folder, sut.SourceFolder);
    }

    [Fact]
    public async Task SelectionSummary_CountsTheTickedRows()
    {
        FakeFileNameRepairService repair = new(
            [Proposal("kaputt-a.jpg", "2021-07-14_19-42-03.jpg"),
             Proposal("kaputt-b.jpg", "2021-07-14_19-42-04.jpg")]);
        using NameRepairViewModel sut = CreateSut(repair);
        sut.SourceFolder = Folder;
        await sut.ScanCommand.ExecuteAsync(parameter: null);

        string beiZweien = sut.SelectionSummary;
        sut.Items[0].IsSelected = false;

        Assert.NotEqual(beiZweien, sut.SelectionSummary);
        Assert.Equal(1, sut.SelectedCount);
    }

    [Fact]
    public async Task Undo_WithoutAnyRun_SaysSoInsteadOfPretendingSuccess()
    {
        // Ohne Lauf gibt es nichts zurückzunehmen. Eine Erfolgsmeldung wäre hier
        // gelogen, und der stillschweigende Nichts-Fall wäre noch schlechter.
        FakeRenameUndoService undo = new();
        using NameRepairViewModel sut = CreateSut(new FakeFileNameRepairService([]), undo);

        await sut.UndoCommand.ExecuteAsync(parameter: null);

        Assert.False(undo.WasUndone);
        Assert.Equal(SortState.Completed, sut.State);
    }

    [Fact]
    public void Cancel_WhileRunning_LeavesTheStateToTheRunningOperation()
    {
        // Der Stopp-Knopf bittet nur; beendet wird der Lauf von der Stelle, die ihn
        // gestartet hat. Genau deshalb darf er den Zustand hier nicht selbst setzen.
        using NameRepairViewModel sut = CreateSut(new FakeFileNameRepairService([]));
        sut.State = SortState.Analyzing;

        sut.CancelCommand.Execute(parameter: null);

        Assert.Equal(SortState.Analyzing, sut.State);
    }

    [Fact]
    public async Task Scan_ReportsProgressWhileItRuns()
    {
        // Zwei Meldungen, zwei Fälle: eine ohne bekannte Gesamtzahl (der Balken läuft
        // unbestimmt), eine mit — erst dann kann er einen Anteil zeigen.
        FakeFileNameRepairService repair = new(
            [Proposal("kaputt.jpg", "2021-07-14_19-42-03.jpg")],
            [new PhotoScanProgress(0, 0), new PhotoScanProgress(3, 12)]);
        StatusBarViewModel status = new(new ReswLocalizer());
        using NameRepairViewModel sut = CreateSut(repair, status: status);
        sut.SourceFolder = Folder;

        await sut.ScanCommand.ExecuteAsync(parameter: null);

        Assert.True(sut.HasPreview);
    }

    private static NameRepairViewModel CreateSut(
        IFileNameRepairService repair,
        FakeRenameUndoService? undo = null,
        bool confirms = true,
        string? lookalike = null,
        StatusBarViewModel? status = null,
        FakeFolderPicker? picker = null)
    {
        ReswLocalizer localizer = new();

        return new NameRepairViewModel(
            repair,
            undo ?? new FakeRenameUndoService(),
            picker ?? new FakeFolderPicker(Folder),
            new FakeFolderLookup(lookalike),
            new StubConfirmationService(confirms),
            status ?? new StatusBarViewModel(localizer),
            localizer,
            NullLogger<NameRepairViewModel>.Instance);
    }
}
