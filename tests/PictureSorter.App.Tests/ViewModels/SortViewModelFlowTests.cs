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

    // ── Testhilfen ─────────────────────────────────────────────────────────────

    /// <summary>Blockiert die Analyse, bis der Abbruch angefordert wird.</summary>
    private sealed class BlockingPhotoSorter : IPhotoSorter
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

    private static async Task PrepareLearnedCategoryAsync(SortViewModel sut)
    {
        sut.SourceFolder = SourceFolder;
        sut.CategoryName = "Familie";
        await sut.SuggestPositivesCommand.ExecuteAsync(parameter: null).ConfigureAwait(false);
        await sut.LearnCommand.ExecuteAsync(parameter: null).ConfigureAwait(false);
    }

    private static SortViewModel CreateSut(
        IPhotoSorter? sorter = null,
        ISortUndoService? undo = null,
        IPhotoSource? photoSource = null,
        ICategoryTrainer? trainer = null,
        IFolderPicker? folderPicker = null,
        bool confirms = true,
        int bulkThreshold = 50,
        int maxExamplesPerSide = 15)
    {
        Photo examplePhoto = new()
        {
            FullPath = Path.Combine(SourceFolder, "beispiel.jpg"),
            FileName = "beispiel.jpg",
        };

        ReswLocalizer localizer = new();

        return new SortViewModel(
            sorter ?? new FakePhotoSorter([]),
            undo ?? new FakeSortUndoService(),
            photoSource ?? new FakePhotoSource([examplePhoto]),
            new TripDetectionService(Options.Create(new TripDetectionOptions())),
            trainer ?? new FakeCategoryTrainer(CreateCategory()),
            new FakeCategoryRepository(),
            folderPicker ?? new FakeFolderPicker(SourceFolder),
            new StubConfirmationService(confirms),
            new StatusBarViewModel(localizer),
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
