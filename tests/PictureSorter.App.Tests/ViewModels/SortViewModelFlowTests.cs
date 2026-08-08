using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PictureSorter.App.Services;
using PictureSorter.App.Tests.Fakes;
using PictureSorter.App.ViewModels;
using PictureSorter.Application.Services;
using PictureSorter.Application.Sorting;
using PictureSorter.Core.Entities;
using PictureSorter.Core.Enums;
using PictureSorter.Core.Exceptions;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.App.Tests.ViewModels;

/// <summary>
/// Tests des Ablaufs rund um die Sortier-Ansicht: Schritte des Assistenten, Neustart,
/// Ordnerwahl, Beispielbeschaffung und vor allem die Abbruch- und Fehlerzweige. Sie
/// sind der eigentliche Zweck des expliziten <see cref="SortState"/> — geht etwas
/// schief, muss die Ansicht wieder bedienbar sein und die Statusleiste den Grund nennen.
/// </summary>
public sealed class SortViewModelFlowTests : IDisposable
{
    private const string SourceFolder = @"C:\fotos";

    // Die Beispielauswahl nimmt nur Dateien an, die es wirklich gibt – sie liest Größe
    // und Datum aus dem Dateisystem. Für diese Tests braucht es also echte Dateien.
    private readonly string _imageFolder =
        Path.Combine(Path.GetTempPath(), "PictureSorterTests", Guid.NewGuid().ToString("N"));

    public SortViewModelFlowTests() => Directory.CreateDirectory(_imageFolder);

    public void Dispose() => Directory.Delete(_imageFolder, recursive: true);

    // ── Assistent ──────────────────────────────────────────────────────────────

    [Fact]
    public void Wizard_WithoutFolder_BlocksTheFirstStep()
    {
        using SortViewModel sut = CreateSut();

        Assert.False(sut.Wizard.CanPrimaryAction);

        sut.SourceFolder = SourceFolder;

        Assert.True(sut.Wizard.CanPrimaryAction);
    }

    [Fact]
    public async Task Wizard_RunsAllSixStepsAndSortsAtTheEnd()
    {
        FakePhotoSorter sorter = new(CreateProposals(2));
        using SortViewModel sut = CreateSut(sorter);
        sut.Wizard.IsGuided = true;

        sut.SourceFolder = SourceFolder;
        await sut.Wizard.PrimaryActionCommand.ExecuteAsync(parameter: null);
        Assert.True(sut.Wizard.IsStep2);

        sut.CategoryName = "Familie";
        await sut.Wizard.PrimaryActionCommand.ExecuteAsync(parameter: null);
        Assert.True(sut.Wizard.IsStep3);

        await sut.SuggestPositivesCommand.ExecuteAsync(parameter: null);
        await sut.Wizard.PrimaryActionCommand.ExecuteAsync(parameter: null);
        Assert.True(sut.Wizard.IsStep4);

        // Lernen, Analysieren, Sortieren — die drei Schritte mit eigener Verarbeitung.
        await sut.Wizard.PrimaryActionCommand.ExecuteAsync(parameter: null);
        Assert.True(sut.Wizard.IsStep5);

        await sut.Wizard.PrimaryActionCommand.ExecuteAsync(parameter: null);
        Assert.True(sut.Wizard.IsStep6);
        Assert.Equal(SortState.Preview, sut.State);

        await sut.Wizard.PrimaryActionCommand.ExecuteAsync(parameter: null);

        Assert.Equal(SortState.Completed, sut.State);
        Assert.Equal(2, sorter.Applied.Count);

        // Der letzte Schritt blättert bewusst nicht weiter — es gibt keinen siebten.
        Assert.True(sut.Wizard.IsStep6);
    }

    [Fact]
    public async Task Wizard_WhenLearningFails_StaysOnTheLearningStep()
    {
        using SortViewModel sut = CreateSut(trainer: new FailingCategoryTrainer(new AiUnavailableException()));
        sut.Wizard.IsGuided = true;
        sut.SourceFolder = SourceFolder;
        sut.CategoryName = "Familie";
        await sut.SuggestPositivesCommand.ExecuteAsync(parameter: null);
        sut.Wizard.MaxReachedStep = 3;
        sut.Wizard.GoToStep(3);

        await sut.Wizard.PrimaryActionCommand.ExecuteAsync(parameter: null);

        Assert.True(sut.Wizard.IsStep4);
        Assert.Equal(SortState.Error, sut.State);
    }

    [Fact]
    public async Task Wizard_WhenAnalysisFails_StaysOnTheAnalysisStep()
    {
        using SortViewModel sut = CreateSut(new FailingPhotoSorter(new IOException("Laufwerk weg")));
        sut.Wizard.IsGuided = true;
        await PrepareLearnedCategoryAsync(sut);
        sut.Wizard.MaxReachedStep = 4;
        sut.Wizard.GoToStep(4);

        await sut.Wizard.PrimaryActionCommand.ExecuteAsync(parameter: null);

        Assert.True(sut.Wizard.IsStep5);
        Assert.Equal(SortState.Error, sut.State);
    }

    [Fact]
    public async Task Restart_ClearsEverythingAndReturnsToTheStart()
    {
        FakePhotoSorter sorter = new(CreateProposals(2));
        using SortViewModel sut = CreateSut(sorter);
        await PrepareLearnedCategoryAsync(sut);
        await sut.AnalyzeCommand.ExecuteAsync(parameter: null);
        sut.IncludeSubfolders = true;
        sut.IsEventCategory = true;
        sut.CategoryDescription = "Bilder meiner Familie";

        Assert.NotEmpty(sut.ActiveCategoryName);

        sut.Wizard.RestartCommand.Execute(parameter: null);

        Assert.Empty(sut.SourceFolder);
        Assert.Empty(sut.CategoryName);
        Assert.Empty(sut.CategoryDescription);
        Assert.Empty(sut.ActiveCategoryName);
        Assert.False(sut.IncludeSubfolders);
        Assert.False(sut.IsEventCategory);
        Assert.Empty(sut.PositiveExamples.Items);
        Assert.Empty(sut.NegativeExamples.Items);
        Assert.Empty(sut.Proposals.Items);
        Assert.Equal(SortState.Idle, sut.State);
        Assert.Equal(0, sut.Wizard.CurrentStep);
        Assert.False(sut.CanAnalyze);
    }

    // ── Ordner- und Beispielwahl ───────────────────────────────────────────────

    [Fact]
    public async Task Browse_WithChosenFolder_TakesItOver()
    {
        using SortViewModel sut = CreateSut();

        await sut.BrowseCommand.ExecuteAsync(parameter: null);

        Assert.Equal(SourceFolder, sut.SourceFolder);
    }

    [Fact]
    public async Task Browse_WhenCanceled_KeepsThePreviousFolder()
    {
        using SortViewModel sut = CreateSut(folderPicker: new FakeFolderPicker(folder: null));
        sut.SourceFolder = @"C:\alt";

        await sut.BrowseCommand.ExecuteAsync(parameter: null);

        Assert.Equal(@"C:\alt", sut.SourceFolder);
    }

    [Fact]
    public async Task PickPositives_TakesOverTheChosenImages()
    {
        FakeFolderPicker picker = new(SourceFolder, [CreateImage("eigen.jpg")]);
        using SortViewModel sut = CreateSut(folderPicker: picker);

        await sut.PickPositivesCommand.ExecuteAsync(parameter: null);

        _ = Assert.Single(sut.PositiveExamples.Items);
    }

    [Fact]
    public async Task PickNegatives_WithoutSelection_ChangesNothing()
    {
        using SortViewModel sut = CreateSut(folderPicker: new FakeFolderPicker(SourceFolder, images: []));

        await sut.PickNegativesCommand.ExecuteAsync(parameter: null);

        Assert.Empty(sut.NegativeExamples.Items);
    }

    [Fact]
    public void AddDroppedImages_TakesOverBothSides()
    {
        using SortViewModel sut = CreateSut();

        sut.AddDroppedImages(isPositive: true, [CreateImage("a.jpg")]);
        sut.AddDroppedImages(isPositive: false, [CreateImage("b.jpg")]);

        _ = Assert.Single(sut.PositiveExamples.Items);
        _ = Assert.Single(sut.NegativeExamples.Items);
    }

    [Fact]
    public void AddDroppedImages_WithoutPaths_Throws() =>
        Assert.Throws<ArgumentNullException>(() =>
        {
            using SortViewModel sut = CreateSut();
            sut.AddDroppedImages(isPositive: true, paths: null!);
        });

    [Fact]
    public async Task SuggestNegatives_SkipsPhotosAlreadyOnTheOtherSide()
    {
        // Dasselbe Foto darf nicht gleichzeitig als passend und als Gegenbeispiel
        // dastehen — sonst lernt die Kategorie sich selbst weg.
        Photo photo = new() { FullPath = Path.Combine(SourceFolder, "beispiel.jpg"), FileName = "beispiel.jpg" };
        using SortViewModel sut = CreateSut(photoSource: new FakePhotoSource([photo]));
        sut.SourceFolder = SourceFolder;

        await sut.SuggestPositivesCommand.ExecuteAsync(parameter: null);
        await sut.SuggestNegativesCommand.ExecuteAsync(parameter: null);

        _ = Assert.Single(sut.PositiveExamples.Items);
        Assert.Empty(sut.NegativeExamples.Items);
    }

    [Fact]
    public async Task SuggestPositives_AtTheEndOfTheFolder_StartsOverFromTheBeginning()
    {
        // Ohne den Rücksprung führte wiederholtes Nachfordern in eine leere Auswahl,
        // aus der nur ein Ordnerwechsel herausführte.
        Photo photo = new() { FullPath = Path.Combine(SourceFolder, "einziges.jpg"), FileName = "einziges.jpg" };
        FakePhotoSource source = new([photo]);
        using SortViewModel sut = CreateSut(photoSource: source);
        sut.SourceFolder = SourceFolder;

        await sut.SuggestPositivesCommand.ExecuteAsync(parameter: null);
        sut.PositiveExamples.Clear();
        await sut.SuggestPositivesCommand.ExecuteAsync(parameter: null);

        Assert.Equal(0, source.LastSkip);
        _ = Assert.Single(sut.PositiveExamples.Items);
    }

    [Fact]
    public async Task SuggestPositives_WhenTheSetIsFull_DoesNotAskTheFolderAgain()
    {
        FakePhotoSource source = new([]);
        using SortViewModel sut = CreateSut(photoSource: source, maxExamplesPerSide: 1);
        sut.SourceFolder = SourceFolder;
        sut.AddDroppedImages(isPositive: true, [CreateImage("voll.jpg")]);

        await sut.SuggestPositivesCommand.ExecuteAsync(parameter: null);

        Assert.Null(source.LastMaxCount);
        _ = Assert.Single(sut.PositiveExamples.Items);
    }

    [Fact]
    public async Task PickPositives_WhenTheSetIsFull_DoesNotOpenTheDialog()
    {
        FakeFolderPicker picker = new(SourceFolder, [CreateImage("weiteres.jpg")]);
        using SortViewModel sut = CreateSut(folderPicker: picker, maxExamplesPerSide: 1);
        sut.AddDroppedImages(isPositive: true, [CreateImage("voll.jpg")]);

        await sut.PickPositivesCommand.ExecuteAsync(parameter: null);

        _ = Assert.Single(sut.PositiveExamples.Items);
    }

    [Theory]
    [InlineData(typeof(DirectoryNotFoundException))]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    [InlineData(typeof(OperationCanceledException))]
    public async Task SuggestPositives_WhenTheFolderIsUnreadable_StaysOperable(Type failureType)
    {
        Exception failure = (Exception)Activator.CreateInstance(failureType)!;
        using SortViewModel sut = CreateSut(photoSource: new FailingPhotoSource(failure));
        sut.SourceFolder = SourceFolder;

        await sut.SuggestPositivesCommand.ExecuteAsync(parameter: null);

        Assert.Empty(sut.PositiveExamples.Items);
        Assert.True(sut.IsInteractive);
    }

    // ── Abbruch- und Fehlerzweige der Use-Cases ────────────────────────────────

    [Fact]
    public async Task Learn_WhenTheAiIsUnavailable_EndsInTheErrorStateButStaysOperable()
    {
        using SortViewModel sut = CreateSut(trainer: new FailingCategoryTrainer(new AiUnavailableException()));
        sut.SourceFolder = SourceFolder;
        sut.CategoryName = "Familie";
        await sut.SuggestPositivesCommand.ExecuteAsync(parameter: null);

        await sut.LearnCommand.ExecuteAsync(parameter: null);

        Assert.Equal(SortState.Error, sut.State);
        Assert.True(sut.IsInteractive);
        Assert.Empty(sut.ActiveCategoryName);
    }

    [Fact]
    public async Task Learn_WhenCanceled_ReturnsToTheIdleState()
    {
        using SortViewModel sut = CreateSut(trainer: new FailingCategoryTrainer(new OperationCanceledException()));
        sut.SourceFolder = SourceFolder;
        sut.CategoryName = "Familie";
        await sut.SuggestPositivesCommand.ExecuteAsync(parameter: null);

        await sut.LearnCommand.ExecuteAsync(parameter: null);

        Assert.Equal(SortState.Idle, sut.State);
        Assert.Empty(sut.ActiveCategoryName);
    }

    [Theory]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    public async Task Analyze_WhenTheFolderIsUnreadable_EndsInTheErrorState(Type failureType)
    {
        Exception failure = (Exception)Activator.CreateInstance(failureType)!;
        using SortViewModel sut = CreateSut(new FailingPhotoSorter(failure));
        await PrepareLearnedCategoryAsync(sut);

        await sut.AnalyzeCommand.ExecuteAsync(parameter: null);

        Assert.Equal(SortState.Error, sut.State);
        Assert.Empty(sut.Proposals.Items);
    }

    [Theory]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(TimeoutException))]
    [InlineData(typeof(OutOfMemoryException))]
    public async Task Analyze_WhenSomethingUnexpectedGoesWrong_LeavesTheViewUsable(Type failureType)
    {
        // Ein Lauf über tausende Bilder darf unter keinen Umständen stumm stehenbleiben.
        // Gefangen wurden früher nur der Abbruch und zwei Datei-Ausnahmen — jede andere
        // ließ den Zustand auf „läuft" stehen. Damit war jeder Knopf gesperrt, die
        // Statusleiste blieb beim letzten Zählstand hängen, und der Stopp-Knopf löste
        // nichts mehr aus. Es gab keinen Weg zurück außer dem Beenden des Programms.
        Exception failure = (Exception)Activator.CreateInstance(failureType)!;
        StatusBarViewModel status = new(new ReswLocalizer());
        using SortViewModel sut = CreateSut(new FailingPhotoSorter(failure), status: status);
        await PrepareLearnedCategoryAsync(sut);

        await sut.AnalyzeCommand.ExecuteAsync(parameter: null);

        Assert.Equal(SortState.Error, sut.State);
        Assert.True(sut.IsInteractive);

        // Und die Statusleiste sagt, was los ist, statt bei „läuft" stehenzubleiben.
        Assert.False(status.IsBusy);
        Assert.Equal(StatusSeverity.Error, status.Severity);
    }

    [Fact]
    public async Task Analyze_WhenCanceled_ReturnsToTheIdleState()
    {
        using SortViewModel sut = CreateSut(new FailingPhotoSorter(new OperationCanceledException()));
        await PrepareLearnedCategoryAsync(sut);

        await sut.AnalyzeCommand.ExecuteAsync(parameter: null);

        Assert.Equal(SortState.Idle, sut.State);
    }

    [Fact]
    public async Task Analyze_WithoutMatchingPhotos_ShowsThePreviewAllTheSame()
    {
        // Die Vorschau muss auch leer erscheinen — sonst bliebe unklar, ob die Analyse
        // überhaupt gelaufen ist.
        using SortViewModel sut = CreateSut(new FakePhotoSorter([]));
        await PrepareLearnedCategoryAsync(sut);

        await sut.AnalyzeCommand.ExecuteAsync(parameter: null);

        Assert.Equal(SortState.Preview, sut.State);
        Assert.Empty(sut.Proposals.Items);
        Assert.False(sut.CanApply);
    }

    [Theory]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    public async Task Apply_WhenTheFilesCannotBeMoved_EndsInTheErrorState(Type failureType)
    {
        Exception failure = (Exception)Activator.CreateInstance(failureType)!;
        using SortViewModel sut = CreateSut(new FailingApplySorter(CreateProposals(2), failure));
        await PrepareLearnedCategoryAsync(sut);
        await sut.AnalyzeCommand.ExecuteAsync(parameter: null);

        await sut.ApplyCommand.ExecuteAsync(parameter: null);

        Assert.Equal(SortState.Error, sut.State);
    }

    [Fact]
    public async Task Apply_WhenCanceled_ReturnsToThePreviewSoNothingIsLost()
    {
        using SortViewModel sut = CreateSut(
            new FailingApplySorter(CreateProposals(2), new OperationCanceledException()));
        await PrepareLearnedCategoryAsync(sut);
        await sut.AnalyzeCommand.ExecuteAsync(parameter: null);

        await sut.ApplyCommand.ExecuteAsync(parameter: null);

        Assert.Equal(SortState.Preview, sut.State);
        Assert.Equal(2, sut.Proposals.Items.Count);
    }

    [Fact]
    public async Task Apply_WithManyProposals_AsksBackAndRespectsARefusal()
    {
        FakePhotoSorter sorter = new(CreateProposals(3));
        using SortViewModel sut = CreateSut(sorter, confirms: false, bulkThreshold: 2);
        await PrepareLearnedCategoryAsync(sut);
        await sut.AnalyzeCommand.ExecuteAsync(parameter: null);

        await sut.ApplyCommand.ExecuteAsync(parameter: null);

        Assert.Empty(sorter.Applied);
        Assert.Equal(SortState.Preview, sut.State);
    }

    [Fact]
    public async Task Cancel_DuringAnalysis_IsOfferedAndRequestsTheStop()
    {
        BlockingPhotoSorter sorter = new();
        using SortViewModel sut = CreateSut(sorter);
        await PrepareLearnedCategoryAsync(sut);

        Task analyzing = sut.AnalyzeCommand.ExecuteAsync(parameter: null);
        await sorter.Started.WaitAsync(TestContext.Current.CancellationToken);

        Assert.True(sut.CanCancel);
        Assert.False(sut.IsInteractive);

        sut.CancelCommand.Execute(parameter: null);
        await analyzing.ConfigureAwait(true);

        Assert.Equal(SortState.Idle, sut.State);
        Assert.True(sut.IsInteractive);
    }

    // ── Rückgängig ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Undo_WithoutARun_ReportsItInsteadOfActingSilently()
    {
        FakeSortUndoService undo = new();
        using SortViewModel sut = CreateSut(undo: undo);
        undo.SetUndoableRun(2);
        await sut.RefreshUndoStateCommand.ExecuteAsync(parameter: null);
        Assert.True(sut.HasUndoableRun);

        // Der Lauf verschwindet zwischen Anzeige und Klick — etwa, weil ihn ein
        // zweites Fenster bereits zurückgenommen hat.
        _ = await undo.UndoLastRunAsync(TestContext.Current.CancellationToken);

        await sut.UndoCommand.ExecuteAsync(parameter: null);

        Assert.False(sut.HasUndoableRun);
        Assert.Empty(sut.UndoSummary);
    }

    [Fact]
    public async Task Undo_WhenRefused_LeavesTheRunUntouched()
    {
        FakeSortUndoService undo = new();
        undo.SetUndoableRun(3);
        using SortViewModel sut = CreateSut(undo: undo, confirms: false);
        await sut.RefreshUndoStateCommand.ExecuteAsync(parameter: null);

        await sut.UndoCommand.ExecuteAsync(parameter: null);

        Assert.Equal(0, undo.UndoCount);
        Assert.True(sut.HasUndoableRun);
    }

    [Fact]
    public async Task Undo_WithSkippedPhotos_NamesThemInsteadOfHidingThem()
    {
        FakeSortUndoService undo = new();
        undo.SetUndoableRun(4);
        undo.Result = new UndoResult { Restored = 3, Skipped = 1 };
        using SortViewModel sut = CreateSut(undo: undo);
        await sut.RefreshUndoStateCommand.ExecuteAsync(parameter: null);

        await sut.UndoCommand.ExecuteAsync(parameter: null);

        Assert.Equal(1, undo.UndoCount);
        Assert.Equal(SortState.Idle, sut.State);
        Assert.False(sut.HasUndoableRun);
    }

    [Theory]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    public async Task Undo_WhenTheFilesCannotBeReturned_EndsInTheErrorState(Type failureType)
    {
        Exception failure = (Exception)Activator.CreateInstance(failureType)!;
        using SortViewModel sut = CreateSut(undo: new FailingSortUndoService(failure));
        await sut.RefreshUndoStateCommand.ExecuteAsync(parameter: null);

        await sut.UndoCommand.ExecuteAsync(parameter: null);

        Assert.Equal(SortState.Error, sut.State);
        Assert.True(sut.IsInteractive);
    }

    [Fact]
    public async Task Undo_WhenCanceled_StaysUndoableSoItCanBeRepeated()
    {
        // Bereits zurückgeholte Fotos bleiben zurückgeholt; der Rest muss weiter
        // erreichbar sein.
        using SortViewModel sut = CreateSut(undo: new FailingSortUndoService(new OperationCanceledException()));
        await sut.RefreshUndoStateCommand.ExecuteAsync(parameter: null);

        await sut.UndoCommand.ExecuteAsync(parameter: null);

        Assert.Equal(SortState.Idle, sut.State);
        Assert.True(sut.HasUndoableRun);
    }

    // ── Sortieren allein nach Aufnahmedatum ────────────────────────────────────

    [Fact]
    public void SortByDate_WithoutADateRange_StaysBlocked()
    {
        using SortViewModel sut = CreateSut();
        sut.SortByDateOnly = true;
        sut.SourceFolder = SourceFolder;
        sut.CategoryName = "Urlaub";

        // Ohne Zeitraum gäbe es kein Kriterium — jedes Foto des Ordners stünde zum
        // Verschieben bereit. Genau deshalb bleibt der Knopf gesperrt.
        Assert.False(sut.CanSortByDate);

        sut.DateFrom = new DateTimeOffset(2026, 7, 12, 0, 0, 0, TimeSpan.Zero);

        Assert.True(sut.CanSortByDate);
    }

    [Fact]
    public void SortByDate_WithAReversedRange_StaysBlocked()
    {
        using SortViewModel sut = CreateSut();
        sut.SortByDateOnly = true;
        sut.SourceFolder = SourceFolder;
        sut.CategoryName = "Urlaub";
        sut.DateFrom = new DateTimeOffset(2026, 7, 21, 0, 0, 0, TimeSpan.Zero);
        sut.DateTo = new DateTimeOffset(2026, 7, 12, 0, 0, 0, TimeSpan.Zero);

        Assert.False(sut.CanSortByDate);
    }

    [Fact]
    public void SortByDateOnly_TellsTheWizardToSkipTheExampleSteps()
    {
        using SortViewModel sut = CreateSut();

        Assert.False(sut.Wizard.SkipsExampleSteps);

        sut.SortByDateOnly = true;

        Assert.True(sut.Wizard.SkipsExampleSteps);
    }

    [Fact]
    public async Task SortByDate_WithARange_ShowsThePreviewAndUsesTheNameAsTargetFolder()
    {
        FakePhotoSorter sorter = new(CreateProposals(1));
        using SortViewModel sut = CreateSut(sorter);
        sut.SortByDateOnly = true;
        sut.SourceFolder = SourceFolder;
        sut.CategoryName = "  Urlaub Norwegen  ";
        sut.DateFrom = new DateTimeOffset(2026, 7, 12, 0, 0, 0, TimeSpan.Zero);
        sut.DateTo = new DateTimeOffset(2026, 7, 21, 0, 0, 0, TimeSpan.Zero);

        await sut.SortByDateCommand.ExecuteAsync(parameter: null);

        Assert.Equal(SortState.Preview, sut.State);
        _ = Assert.Single(sut.Proposals.Items);

        // Der eingetippte Name wird beschnitten weitergereicht — sonst entstünde ein
        // Ordner mit führendem Leerzeichen.
        Assert.Equal("Urlaub Norwegen", sorter.LastDateTargetFolder);
    }

    [Fact]
    public async Task SortByDate_WhenNothingIsFound_ReturnsToPreviewWithAWarning()
    {
        FakePhotoSorter sorter = new([]);
        using SortViewModel sut = CreateSut(sorter);
        sut.SortByDateOnly = true;
        sut.SourceFolder = SourceFolder;
        sut.CategoryName = "Urlaub";
        sut.DateFrom = new DateTimeOffset(2026, 7, 12, 0, 0, 0, TimeSpan.Zero);

        await sut.SortByDateCommand.ExecuteAsync(parameter: null);

        Assert.Equal(SortState.Preview, sut.State);
        Assert.Empty(sut.Proposals.Items);
    }

    [Fact]
    public async Task SortByDate_WhenTheFolderCannotBeRead_ReportsTheErrorAndStaysUsable()
    {
        using SortViewModel sut = CreateSut(
            new FailingPhotoSorter(new UnauthorizedAccessException("kein Zugriff")));
        sut.SortByDateOnly = true;
        sut.SourceFolder = SourceFolder;
        sut.CategoryName = "Urlaub";
        sut.DateFrom = new DateTimeOffset(2026, 7, 12, 0, 0, 0, TimeSpan.Zero);

        await sut.SortByDateCommand.ExecuteAsync(parameter: null);

        Assert.Equal(SortState.Error, sut.State);
        Assert.True(sut.IsInteractive);
    }

    [Fact]
    public async Task SortByDate_WhenCanceled_ReturnsToIdle()
    {
        BlockingPhotoSorter sorter = new();
        using SortViewModel sut = CreateSut(sorter);
        sut.SortByDateOnly = true;
        sut.SourceFolder = SourceFolder;
        sut.CategoryName = "Urlaub";
        sut.DateFrom = new DateTimeOffset(2026, 7, 12, 0, 0, 0, TimeSpan.Zero);

        Task running = sut.SortByDateCommand.ExecuteAsync(parameter: null);
        await sorter.Started.WaitAsync(TestContext.Current.CancellationToken);

        Assert.True(sut.CanCancel);

        sut.CancelCommand.Execute(parameter: null);
        await running.ConfigureAwait(true);

        Assert.Equal(SortState.Idle, sut.State);
    }

    // ── Testhilfen ─────────────────────────────────────────────────────────────

    /// <summary>Blockiert die Analyse, bis der Abbruch angefordert wird.</summary>
    private sealed class BlockingPhotoSorter : ITestSorter
    {
        public SemaphoreSlim Started { get; } = new(0);

        public async Task<IReadOnlyList<SortProposal>> CreateProposalsAsync(
            string sourceFolder,
            Category category,
            bool includeSubfolders,
            DateRange dateRange,
            IProgress<SortProgress>? progress,
            CancellationToken cancellationToken)
        {
            _ = Started.Release();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            return [];
        }

        public async Task<IReadOnlyList<SortProposal>> CreateDateProposalsAsync(
            string sourceFolder,
            string targetFolderName,
            bool includeSubfolders,
            DateRange dateRange,
            IProgress<SortProgress>? progress,
            CancellationToken cancellationToken)
        {
            _ = Started.Release();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            return [];
        }

        public async Task<IReadOnlyList<SortProposal>> ResumeAsync(
            AnalysisRun run,
            Category? category,
            IProgress<SortProgress>? progress,
            CancellationToken cancellationToken)
        {
            _ = Started.Release();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            return [];
        }

        public Task<int> ApplyProposalsAsync(
            IReadOnlyList<SortProposal> toApply,
            FileOperationMode operation,
            bool dryRun,
            CancellationToken cancellationToken) => Task.FromResult(0);

        public Task IgnoreProposalsAsync(IReadOnlyList<SortProposal> toIgnore, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    // Legt eine echte, wenn auch winzige Bilddatei an.
    private string CreateImage(string fileName)
    {
        string path = Path.Combine(_imageFolder, fileName);
        File.WriteAllBytes(path, [1, 2, 3]);
        return path;
    }

    // ── Anhalten, Fortsetzen, Zurückholen ──────────────────────────────────────

    [Fact]
    public async Task RefreshResumeState_WithAPausedRun_OffersToContinue()
    {
        FakeAnalysisJournal journal = new();
        journal.Seed(PausedRun());
        using SortViewModel sut = CreateSut(journal: journal);

        await sut.Resume.RefreshCommand.ExecuteAsync(parameter: null);

        Assert.True(sut.Resume.HasRun);
        Assert.Contains("3472", sut.Resume.Summary, StringComparison.Ordinal);
        Assert.NotEmpty(sut.Resume.ActionLabel);
    }

    [Fact]
    public async Task RefreshResumeState_WithoutAnyRun_OffersNothing()
    {
        using SortViewModel sut = CreateSut();

        await sut.Resume.RefreshCommand.ExecuteAsync(parameter: null);

        Assert.False(sut.Resume.HasRun);
        Assert.Empty(sut.Resume.Summary);
    }

    [Fact]
    public async Task Resume_TakesOverTheSettingsOfTheRunAndShowsTheResult()
    {
        FakeAnalysisJournal journal = new();
        journal.Seed(PausedRun());
        FakePhotoSorter sorter = new(CreateProposals(3));
        using SortViewModel sut = CreateSut(sorter, journal: journal);

        // Die Gruppe ist angelernt und gespeichert — so wie sie es beim ursprünglichen
        // Lauf war. Ohne sie ließe sich nicht fortsetzen, das prüft der nächste Test.
        await PrepareLearnedCategoryAsync(sut);
        await sut.Resume.RefreshCommand.ExecuteAsync(parameter: null);

        await sut.Resume.ResumeCommand.ExecuteAsync(parameter: null);

        // Die Angaben des Laufs gelten, nicht die der Oberfläche: Ein fortgesetzter Lauf
        // muss dieselbe Frage beantworten wie der unterbrochene.
        Assert.Equal(SourceFolder, sut.SourceFolder);
        Assert.Equal("Familie", sut.CategoryName);
        Assert.NotNull(sorter.ResumedRun);
        Assert.Equal(SortState.Preview, sut.State);
        Assert.Equal(3, sut.Proposals.Items.Count);

        // Und die Nutzerin landet direkt bei der Vorschau — sie hat um das Ergebnis
        // gebeten, nicht um den Weg dorthin.
        Assert.True(sut.Wizard.IsStep6);
    }

    [Fact]
    public async Task Resume_WhenTheCategoryIsGone_SaysSoInsteadOfGuessing()
    {
        FakeAnalysisJournal journal = new();
        journal.Seed(PausedRun() with { CategoryName = "Gibt es nicht mehr" });
        using SortViewModel sut = CreateSut(journal: journal);
        await sut.Resume.RefreshCommand.ExecuteAsync(parameter: null);

        await sut.Resume.ResumeCommand.ExecuteAsync(parameter: null);

        Assert.Equal(SortState.Idle, sut.State);
        Assert.Empty(sut.Proposals.Items);
    }

    [Fact]
    public async Task DiscardRun_RemovesTheOfferAfterConfirmation()
    {
        FakeAnalysisJournal journal = new();
        journal.Seed(PausedRun());
        using SortViewModel sut = CreateSut(journal: journal);
        await sut.Resume.RefreshCommand.ExecuteAsync(parameter: null);

        await sut.Resume.DiscardCommand.ExecuteAsync(parameter: null);

        _ = Assert.Single(journal.Discarded);
        Assert.False(sut.Resume.HasRun);
    }

    [Fact]
    public async Task DiscardRun_WhenTheUserSaysNo_KeepsTheRun()
    {
        FakeAnalysisJournal journal = new();
        journal.Seed(PausedRun());
        using SortViewModel sut = CreateSut(journal: journal, confirms: false);
        await sut.Resume.RefreshCommand.ExecuteAsync(parameter: null);

        await sut.Resume.DiscardCommand.ExecuteAsync(parameter: null);

        Assert.Empty(journal.Discarded);
        Assert.True(sut.Resume.HasRun);
    }

    [Fact]
    public async Task RefreshResumeState_WhenTheJournalIsBroken_OpensThePageAnyway()
    {
        // Der Hinweis wird beim Anzeigen der Seite geholt. Eine gesperrte Datenbank darf
        // die Sortierseite nicht mitreißen.
        using SortViewModel sut = CreateSut(journal: new BrokenAnalysisJournal());

        await sut.Resume.RefreshCommand.ExecuteAsync(parameter: null);

        Assert.False(sut.Resume.HasRun);
    }

    [Fact]
    public void RecoverFromMemory_WithoutFolderOrName_StaysDisabled()
    {
        using SortViewModel sut = CreateSut();

        Assert.False(sut.CanRecoverFromMemory);

        sut.SourceFolder = SourceFolder;
        Assert.False(sut.CanRecoverFromMemory);

        sut.CategoryName = "Familie";
        Assert.True(sut.CanRecoverFromMemory);
    }

    [Fact]
    public async Task RecoverFromMemory_WithNothingRemembered_ReturnsToTheIdleState()
    {
        using SortViewModel sut = CreateSut();
        sut.SourceFolder = SourceFolder;
        sut.CategoryName = "Familie";

        await sut.Resume.RecoverFromMemoryCommand.ExecuteAsync(parameter: null);

        Assert.Equal(SortState.Idle, sut.State);
        Assert.Empty(sut.Proposals.Items);
        Assert.True(sut.IsInteractive);
    }

    [Fact]
    public async Task RecoverFromMemory_BringsBackTheProposalsOfAnEarlierRun()
    {
        // Der Weg für Läufe, zu denen es kein Protokoll gibt — etwa aus einer älteren
        // Fassung oder nach einem Absturz. Die Urteile stehen im Gedächtnis, und von dort
        // kommen die Vorschläge zurück, ohne dass ein Bild erneut bewertet wird.
        string imagePath = CreateImage("gerettet.jpg");
        FakeSortMemory memory = new();
        memory.Records.Add(RememberedProposal(imagePath));

        using SortViewModel sut = CreateSut(recovery: RecoveryFactory.Create(memory));
        sut.SourceFolder = _imageFolder;
        sut.CategoryName = "Familie";

        await sut.Resume.RecoverFromMemoryCommand.ExecuteAsync(parameter: null);

        Assert.Equal(SortState.Preview, sut.State);
        _ = Assert.Single(sut.Proposals.Items);
        Assert.True(sut.Wizard.IsStep6);
    }

    [Fact]
    public async Task Analyze_WhenTheAiIsUnavailable_SaysSoInsteadOfBlamingTheFolder()
    {
        // Bekannte Störungen bekommen den Text, der die Nutzerin weiterbringt. Vorher
        // stand bei jedem Fehler dasselbe da: „Analyse fehlgeschlagen".
        StatusBarViewModel status = new(new ReswLocalizer());
        using SortViewModel sut = CreateSut(
            new FailingPhotoSorter(new AiUnavailableException("Ollama antwortet nicht")),
            status: status);
        await PrepareLearnedCategoryAsync(sut);

        await sut.AnalyzeCommand.ExecuteAsync(parameter: null);

        Assert.Equal(SortState.Error, sut.State);
        Assert.Contains("Ollama", status.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Analyze_WhenTheFolderIsGone_NamesTheFolder()
    {
        StatusBarViewModel status = new(new ReswLocalizer());
        using SortViewModel sut = CreateSut(
            new FailingPhotoSorter(new DirectoryNotFoundException("weg")),
            status: status);
        await PrepareLearnedCategoryAsync(sut);

        await sut.AnalyzeCommand.ExecuteAsync(parameter: null);

        Assert.Equal(SortState.Error, sut.State);
        Assert.Contains("Ordner", status.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Cancel_DuringAnalysis_SaysPausingInsteadOfCanceling()
    {
        // Seit jedes Ergebnis im Protokoll steht, geht beim Anhalten nichts verloren —
        // und die Meldung muss das sagen, sonst traut sich niemand.
        StatusBarViewModel status = new(new ReswLocalizer());
        BlockingPhotoSorter sorter = new();
        using SortViewModel sut = CreateSut(sorter, status: status);
        await PrepareLearnedCategoryAsync(sut);

        Task analysis = sut.AnalyzeCommand.ExecuteAsync(parameter: null);
        await sorter.Started.WaitAsync(TestContext.Current.CancellationToken);

        sut.CancelCommand.Execute(parameter: null);
        await analysis.ConfigureAwait(true);

        Assert.Equal(SortState.Idle, sut.State);
        Assert.False(status.IsBusy);
    }

    [Fact]
    public async Task SuggestTrips_WithClusteredPhotos_OffersThePeriod()
    {
        // Die Urlaubssuche liest die Aufnahmedaten aller Bilder — dieselbe Arbeit wie der
        // Ladeteil einer Analyse. Danach steht ein Zeitraum zur Auswahl, und ein Klick
        // übernimmt ihn.
        IReadOnlyList<Photo> photos = [.. Enumerable.Range(0, 10).Select(index => new Photo
        {
            FullPath = Path.Combine(SourceFolder, $"urlaub{index}.jpg"),
            FileName = $"urlaub{index}.jpg",
            CapturedAt = new DateTimeOffset(2026, 7, 12, 10, 0, 0, TimeSpan.Zero).AddDays(index),
        })];
        using SortViewModel sut = CreateSut(photoSource: new FakePhotoSource(photos));
        sut.SourceFolder = SourceFolder;

        await sut.SuggestTripsCommand.ExecuteAsync(parameter: null);

        TripSuggestionViewModel suggestion = Assert.Single(sut.TripSuggestions);
        Assert.Empty(sut.TripHint);

        sut.UseTripSuggestionCommand.Execute(suggestion);

        Assert.Equal(new DateOnly(2026, 7, 12), DateOnly.FromDateTime(sut.DateFrom!.Value.LocalDateTime));
        Assert.Equal(new DateOnly(2026, 7, 21), DateOnly.FromDateTime(sut.DateTo!.Value.LocalDateTime));
    }

    [Fact]
    public async Task SuggestTrips_WithoutAnyCluster_ExplainsTheEmptyList()
    {
        // Ohne Erklärung stünde die Nutzerin vor einer leeren Liste und wüsste nicht, ob
        // die Suche überhaupt gelaufen ist.
        using SortViewModel sut = CreateSut(photoSource: new FakePhotoSource([]));
        sut.SourceFolder = SourceFolder;

        await sut.SuggestTripsCommand.ExecuteAsync(parameter: null);

        Assert.Empty(sut.TripSuggestions);
        Assert.NotEmpty(sut.TripHint);
    }

    [Fact]
    public void UseTripSuggestion_WithoutSuggestion_ChangesNothing()
    {
        using SortViewModel sut = CreateSut();

        sut.UseTripSuggestionCommand.Execute(parameter: null);

        Assert.Null(sut.DateFrom);
        Assert.Null(sut.DateTo);
    }

    private SortMemoryRecord RememberedProposal(string imagePath)
    {
        FileInfo info = new(imagePath);
        Photo photo = new()
        {
            FullPath = info.FullName,
            FileName = info.Name,
            SizeBytes = info.Length,
            CapturedAt = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero),
        };

        return new SortMemoryRecord
        {
            FolderPath = _imageFolder,
            FileSignature = photo.ComputeSignature(),
            PhotoPath = photo.FullPath,
            CategoryName = "Familie",
            Status = SortMemoryStatus.Proposed,
            Confidence = 0.88,
            UpdatedAt = new DateTimeOffset(2026, 8, 6, 21, 0, 0, TimeSpan.Zero),
        };
    }

    private static AnalysisRun PausedRun() => new()
    {
        Id = new Guid("44444444-4444-4444-4444-444444444444"),
        SourceFolder = SourceFolder,
        CategoryName = "Familie",
        ByDateOnly = false,
        IncludeSubfolders = false,
        State = AnalysisRunState.Paused,
        StartedAt = new DateTimeOffset(2026, 8, 6, 20, 0, 0, TimeSpan.Zero),
        LastProgressAt = new DateTimeOffset(2026, 8, 6, 23, 30, 0, TimeSpan.Zero),
        TotalPhotos = 4130,
        DecidedPhotos = 3472,
    };

    private static async Task PrepareLearnedCategoryAsync(SortViewModel sut)
    {
        sut.SourceFolder = SourceFolder;
        sut.CategoryName = "Familie";
        await sut.SuggestPositivesCommand.ExecuteAsync(parameter: null).ConfigureAwait(false);
        await sut.LearnCommand.ExecuteAsync(parameter: null).ConfigureAwait(false);
    }

    private static SortViewModel CreateSut(
        ITestSorter? sorter = null,
        ISortUndoService? undo = null,
        IPhotoSource? photoSource = null,
        ICategoryTrainer? trainer = null,
        IFolderPicker? folderPicker = null,
        bool confirms = true,
        int bulkThreshold = 50,
        int maxExamplesPerSide = 15,
        IAnalysisJournal? journal = null,
        SortMemoryRecovery? recovery = null,
        StatusBarViewModel? status = null)
    {
        Photo examplePhoto = new()
        {
            FullPath = Path.Combine(SourceFolder, "beispiel.jpg"),
            FileName = "beispiel.jpg",
        };

        ReswLocalizer localizer = new();

        ITestSorter doppel = sorter ?? new FakePhotoSorter([]);

        return new SortViewModel(
            doppel,
            doppel,
            undo ?? new FakeSortUndoService(),
            journal ?? new FakeAnalysisJournal(),
            recovery ?? RecoveryFactory.Create(),
            photoSource ?? new FakePhotoSource([examplePhoto]),
            new TripDetectionService(Options.Create(new TripDetectionOptions())),
            trainer ?? new FakeCategoryTrainer(CreateCategory()),
            new FakeCategoryRepository(),
            folderPicker ?? new FakeFolderPicker(SourceFolder),
            new StubConfirmationService(confirms),
            status ?? new StatusBarViewModel(localizer),
            Options.Create(new SortingOptions
            {
                BulkConfirmationThreshold = bulkThreshold,
                MaxExamplesPerSide = maxExamplesPerSide,
            }),
            localizer,
            NullLogger<SortViewModel>.Instance);
    }

    private static Category CreateCategory()
    {
        Category category = new("Familie", "Bilder meiner Familie", CategoryKind.Topic);
        category.AddExample(new CategoryExample
        {
            PhotoPath = @"C:\fotos\beispiel.jpg",
            IsPositive = true,
            Embedding = new ImageEmbedding([1.0f, 0.0f], "fake"),
        });
        return category;
    }

    private static IReadOnlyList<SortProposal> CreateProposals(int count) =>
        [.. Enumerable.Range(0, count).Select(index => new SortProposal
        {
            Photo = new Photo
            {
                FullPath = Path.Combine(SourceFolder, $"foto{index}.jpg"),
                FileName = $"foto{index}.jpg",
            },
            CategoryName = "Familie",
            SourceFolder = SourceFolder,
            TargetFolderPath = Path.Combine(SourceFolder, "Familie"),
            Confidence = 0.9,
            Method = ClassificationMethod.Embedding,
        })];
}
