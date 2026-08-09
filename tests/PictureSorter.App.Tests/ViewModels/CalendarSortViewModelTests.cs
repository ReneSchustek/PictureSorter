using Microsoft.Extensions.Logging.Abstractions;
using PictureSorter.App.Tests.Fakes;
using PictureSorter.App.ViewModels;
using PictureSorter.Core.Entities;
using PictureSorter.Core.Enums;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.App.Tests.ViewModels;

/// <summary>
/// Tests der Ablage nach Aufnahmedatum: Stufe wählen, Vorschau erstellen, ablegen.
/// </summary>
public sealed class CalendarSortViewModelTests
{
    private const string SourceFolder = @"C:\fotos";
    private const string TargetRoot = @"D:\archiv";

    [Fact]
    public void Granularity_StartsAtMonthAndTheRadioButtonsAgree()
    {
        using CalendarSortViewModel viewModel = CreateViewModel([]);

        Assert.Equal(CalendarGranularity.Month, viewModel.Granularity);
        Assert.True(viewModel.IsMonth);
        Assert.False(viewModel.IsYear);
        Assert.False(viewModel.IsDay);
    }

    [Theory]
    [InlineData(CalendarGranularity.Year, "2021")]
    [InlineData(CalendarGranularity.Month, "2021-07")]
    [InlineData(CalendarGranularity.Day, "2021-07-15")]
    public void FolderExample_ShowsHowTheFoldersWillBeNamed(CalendarGranularity granularity, string expected)
    {
        using CalendarSortViewModel viewModel = CreateViewModel([]);

        viewModel.Granularity = granularity;

        Assert.Equal(expected, viewModel.FolderExample);
    }

    [Fact]
    public void PickingTheYearOption_SwitchesTheOthersOff()
    {
        using CalendarSortViewModel viewModel = CreateViewModel([]);

        viewModel.IsYear = true;

        Assert.Equal(CalendarGranularity.Year, viewModel.Granularity);
        Assert.False(viewModel.IsMonth);
    }

    [Fact]
    public void UncheckingAnOption_DoesNotChangeAnything()
    {
        // Ein Optionsfeld meldet auch das Abwählen. Würde das durchschlagen, bliebe die
        // Ansicht ohne jede gewählte Stufe zurück — ein Zustand, den es nicht gibt.
        using CalendarSortViewModel viewModel = CreateViewModel([]);

        viewModel.IsMonth = false;

        Assert.Equal(CalendarGranularity.Month, viewModel.Granularity);
    }

    [Fact]
    public void CanAnalyze_RequiresBothFolders()
    {
        using CalendarSortViewModel viewModel = CreateViewModel([]);

        Assert.False(viewModel.CanAnalyze);

        viewModel.SourceFolder = SourceFolder;
        Assert.False(viewModel.CanAnalyze);

        viewModel.TargetRoot = TargetRoot;
        Assert.True(viewModel.CanAnalyze);
    }

    [Fact]
    public async Task PickingTheSourceFolder_SuggestsItAsTargetToo()
    {
        // Der häufigste Fall: Die Ordner entstehen im selben Verzeichnis. Vorgeschlagen,
        // nicht erzwungen.
        using CalendarSortViewModel viewModel = CreateViewModel([]);

        await viewModel.PickSourceFolderCommand.ExecuteAsync(parameter: null);

        Assert.Equal(SourceFolder, viewModel.SourceFolder);
        Assert.Equal(SourceFolder, viewModel.TargetRoot);
    }

    [Fact]
    public async Task PickingTheSourceFolder_LeavesAnAlreadyChosenTargetAlone()
    {
        using CalendarSortViewModel viewModel = CreateViewModel([]);
        viewModel.TargetRoot = TargetRoot;

        await viewModel.PickSourceFolderCommand.ExecuteAsync(parameter: null);

        Assert.Equal(TargetRoot, viewModel.TargetRoot);
    }

    [Fact]
    public async Task Analyze_ShowsTheProposalsAndPassesTheChosenStep()
    {
        FakePhotoSorter sorter = new([Proposal("sommer.jpg", "2021-07")]);
        using CalendarSortViewModel viewModel = CreateViewModel(sorter);
        viewModel.SourceFolder = SourceFolder;
        viewModel.TargetRoot = TargetRoot;
        viewModel.Granularity = CalendarGranularity.Day;

        await viewModel.AnalyzeCommand.ExecuteAsync(parameter: null);

        Assert.Equal(SortState.Preview, viewModel.State);
        _ = Assert.Single(viewModel.Proposals.Items);
        Assert.True(viewModel.HasPreview);
        Assert.Equal(TargetRoot, sorter.LastCalendarRoot);
        Assert.Equal(CalendarGranularity.Day, sorter.LastGranularity);
    }

    [Fact]
    public async Task Analyze_WithoutAnyProposal_SaysSoInsteadOfShowingAnEmptyPreview()
    {
        using CalendarSortViewModel viewModel = CreateViewModel([]);
        viewModel.SourceFolder = SourceFolder;
        viewModel.TargetRoot = TargetRoot;

        await viewModel.AnalyzeCommand.ExecuteAsync(parameter: null);

        Assert.Equal(SortState.Completed, viewModel.State);
        Assert.False(viewModel.HasPreview);
    }

    [Fact]
    public async Task Apply_MovesTheSelectedPictures()
    {
        FakePhotoSorter sorter = new([Proposal("a.jpg", "2021-07"), Proposal("b.jpg", "2021-08")]);
        using CalendarSortViewModel viewModel = CreateViewModel(sorter);
        viewModel.SourceFolder = SourceFolder;
        viewModel.TargetRoot = TargetRoot;
        await viewModel.AnalyzeCommand.ExecuteAsync(parameter: null);

        await viewModel.ApplyCommand.ExecuteAsync(parameter: null);

        Assert.Equal(2, sorter.Applied.Count);
        Assert.Equal(FileOperationMode.Move, sorter.LastOperation);
        Assert.Equal(SortState.Completed, viewModel.State);
        Assert.False(viewModel.HasPreview);
    }

    [Fact]
    public async Task Apply_WithCopyChosen_CopiesInsteadOfMoving()
    {
        FakePhotoSorter sorter = new([Proposal("a.jpg", "2021-07")]);
        using CalendarSortViewModel viewModel = CreateViewModel(sorter);
        viewModel.SourceFolder = SourceFolder;
        viewModel.TargetRoot = TargetRoot;
        viewModel.CopyInsteadOfMove = true;
        await viewModel.AnalyzeCommand.ExecuteAsync(parameter: null);

        await viewModel.ApplyCommand.ExecuteAsync(parameter: null);

        Assert.Equal(FileOperationMode.Copy, sorter.LastOperation);
    }

    [Fact]
    public async Task Apply_WhenTheQuestionIsDeclined_TouchesNothing()
    {
        FakePhotoSorter sorter = new([Proposal("a.jpg", "2021-07")]);
        using CalendarSortViewModel viewModel = CreateViewModel(sorter, confirmed: false);
        viewModel.SourceFolder = SourceFolder;
        viewModel.TargetRoot = TargetRoot;
        await viewModel.AnalyzeCommand.ExecuteAsync(parameter: null);

        await viewModel.ApplyCommand.ExecuteAsync(parameter: null);

        Assert.Empty(sorter.Applied);
        Assert.Equal(SortState.Preview, viewModel.State);
    }

    [Fact]
    public async Task Apply_WithNothingSelected_IsNotOffered()
    {
        FakePhotoSorter sorter = new([Proposal("a.jpg", "2021-07")]);
        using CalendarSortViewModel viewModel = CreateViewModel(sorter);
        viewModel.SourceFolder = SourceFolder;
        viewModel.TargetRoot = TargetRoot;
        await viewModel.AnalyzeCommand.ExecuteAsync(parameter: null);

        viewModel.Proposals.Items[0].IsSelected = false;

        Assert.False(viewModel.CanApply);
    }

    [Fact]
    public void WhileRunning_TheViewIsLockedAndCanBeStopped()
    {
        FakePhotoSorter sorter = new([Proposal("a.jpg", "2021-07")]);
        using CalendarSortViewModel viewModel = CreateViewModel(sorter);

        viewModel.State = SortState.Analyzing;

        Assert.False(viewModel.IsInteractive);
        Assert.True(viewModel.CanCancel);
        Assert.False(viewModel.CanAnalyze);
    }

    [Fact]
    public async Task Analyze_WhenTheServiceFails_SaysSoAndStaysUsable()
    {
        using CalendarSortViewModel viewModel = CreateFailing(new InvalidOperationException("Laufwerk weg"));
        viewModel.SourceFolder = SourceFolder;
        viewModel.TargetRoot = TargetRoot;

        await viewModel.AnalyzeCommand.ExecuteAsync(parameter: null);

        Assert.Equal(SortState.Error, viewModel.State);
        Assert.True(viewModel.IsInteractive);
    }

    [Fact]
    public async Task Analyze_WhenStopped_ReturnsToTheStartingPoint()
    {
        using CalendarSortViewModel viewModel = CreateFailing(new OperationCanceledException());
        viewModel.SourceFolder = SourceFolder;
        viewModel.TargetRoot = TargetRoot;

        await viewModel.AnalyzeCommand.ExecuteAsync(parameter: null);

        Assert.Equal(SortState.Idle, viewModel.State);
        Assert.True(viewModel.CanAnalyze);
    }

    [Fact]
    public async Task Apply_WhenTheMoveFails_KeepsThePreview()
    {
        // Der Vorschlag bleibt stehen: Wer den Fehler behoben hat, soll es noch einmal
        // versuchen können, ohne den ganzen Ordner neu zu lesen.
        FailingApplySorter sorter = new([Proposal("a.jpg", "2021-07")], new IOException("Ziel voll"));
        using CalendarSortViewModel viewModel = CreateViewModel(sorter);
        viewModel.SourceFolder = SourceFolder;
        viewModel.TargetRoot = TargetRoot;
        await viewModel.AnalyzeCommand.ExecuteAsync(parameter: null);

        await viewModel.ApplyCommand.ExecuteAsync(parameter: null);

        Assert.Equal(SortState.Error, viewModel.State);
        _ = Assert.Single(viewModel.Proposals.Items);
    }

    [Fact]
    public async Task PickingTheTargetRoot_TakesTheChosenFolder()
    {
        using CalendarSortViewModel viewModel = CreateViewModel([]);

        await viewModel.PickTargetRootCommand.ExecuteAsync(parameter: null);

        Assert.Equal(SourceFolder, viewModel.TargetRoot);
    }

    [Fact]
    public void Cancel_WhileRunning_IsPassedOn()
    {
        using CalendarSortViewModel viewModel = CreateViewModel([]);
        viewModel.State = SortState.Analyzing;

        viewModel.CancelCommand.Execute(parameter: null);

        // Ohne laufenden Vorgang gibt es nichts abzubrechen; der Aufruf muss dennoch
        // ohne Ausnahme durchgehen.
        Assert.Equal(SortState.Analyzing, viewModel.State);
    }

    private static CalendarSortViewModel CreateFailing(Exception failure)
    {
        ReswLocalizer localizer = new();
        FailingPhotoSorter sorter = new(failure);

        return new CalendarSortViewModel(
            sorter,
            sorter,
            new FakeFolderPicker(SourceFolder),
            new StubConfirmationService(true),
            new StatusBarViewModel(localizer),
            localizer,
            NullLogger<CalendarSortViewModel>.Instance);
    }

    private static CalendarSortViewModel CreateViewModel(
        FailingApplySorter sorter)
    {
        ReswLocalizer localizer = new();

        return new CalendarSortViewModel(
            sorter,
            sorter,
            new FakeFolderPicker(SourceFolder),
            new StubConfirmationService(true),
            new StatusBarViewModel(localizer),
            localizer,
            NullLogger<CalendarSortViewModel>.Instance);
    }

    private static SortProposal Proposal(string fileName, string targetFolder) => new()
    {
        Photo = new Photo
        {
            FullPath = Path.Combine(SourceFolder, fileName),
            FileName = fileName,
        },
        CategoryName = targetFolder,
        SourceFolder = SourceFolder,
        TargetFolderPath = Path.Combine(TargetRoot, targetFolder),
        Confidence = 1.0,
        Method = ClassificationMethod.CaptureDate,
    };

    private static CalendarSortViewModel CreateViewModel(
        IReadOnlyList<SortProposal> proposals,
        bool confirmed = true) =>
        CreateViewModel(new FakePhotoSorter(proposals), confirmed);

    private static CalendarSortViewModel CreateViewModel(
        FakePhotoSorter sorter,
        bool confirmed = true)
    {
        ReswLocalizer localizer = new();

        return new CalendarSortViewModel(
            sorter,
            sorter,
            new FakeFolderPicker(SourceFolder),
            new StubConfirmationService(confirmed),
            new StatusBarViewModel(localizer),
            localizer,
            NullLogger<CalendarSortViewModel>.Instance);
    }
}
