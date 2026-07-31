using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PictureSorter.App.Tests.Fakes;
using PictureSorter.App.ViewModels;
using PictureSorter.Application.Sorting;
using PictureSorter.Core.Entities;
using PictureSorter.Core.Enums;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.App.Tests.ViewModels;

/// <summary>
/// Tests der Auswahl- und Anwende-Logik der Sortier-Vorschau: Nur ausgewählte
/// Vorschläge werden verschoben, abgewählte werden dauerhaft gemerkt.
/// </summary>
public sealed class SortViewModelTests
{
    private const string SourceFolder = @"C:\fotos";

    [Fact]
    public async Task Analyze_MakesAllProposalsSelectedByDefault()
    {
        FakePhotoSorter sorter = new(CreateProposals(3));
        using SortViewModel sut = CreateSut(sorter);

        await AnalyzeAsync(sut);

        Assert.Equal(3, sut.Proposals.Count);
        Assert.All(sut.Proposals, proposal => Assert.True(proposal.IsSelected));
        Assert.Equal(3, sut.SelectedProposalCount);
        Assert.True(sut.CanApply);
    }

    [Fact]
    public async Task Apply_WithDeselectedProposal_MovesOnlySelectedOnes()
    {
        FakePhotoSorter sorter = new(CreateProposals(3));
        using SortViewModel sut = CreateSut(sorter);
        await AnalyzeAsync(sut);

        sut.Proposals[1].IsSelected = false;

        await sut.ApplyCommand.ExecuteAsync(parameter: null);

        Assert.Equal(2, sorter.Applied.Count);
        Assert.DoesNotContain(sorter.Applied, proposal => proposal.Photo.FileName == "foto1.jpg");
    }

    [Fact]
    public async Task Apply_ByDefault_MovesInsteadOfCopying()
    {
        // Die Voreinstellung darf sich durch die neue Wahlmöglichkeit nicht ändern –
        // wer nichts umstellt, bekommt weiterhin das gewohnte Verschieben.
        FakePhotoSorter sorter = new(CreateProposals(1));
        using SortViewModel sut = CreateSut(sorter);
        await AnalyzeAsync(sut);

        await sut.ApplyCommand.ExecuteAsync(parameter: null);

        Assert.False(sut.CopyInsteadOfMove);
        Assert.Equal(FileOperationMode.Move, sorter.LastOperation);
    }

    [Fact]
    public async Task Apply_WithCopyChosen_RunsAsCopy()
    {
        FakePhotoSorter sorter = new(CreateProposals(1));
        using SortViewModel sut = CreateSut(sorter);
        await AnalyzeAsync(sut);

        sut.CopyInsteadOfMove = true;

        await sut.ApplyCommand.ExecuteAsync(parameter: null);

        Assert.Equal(FileOperationMode.Copy, sorter.LastOperation);
    }

    [Fact]
    public async Task Apply_WithDeselectedProposal_RemembersItAsIgnored()
    {
        FakePhotoSorter sorter = new(CreateProposals(3));
        using SortViewModel sut = CreateSut(sorter);
        await AnalyzeAsync(sut);

        sut.Proposals[1].IsSelected = false;

        await sut.ApplyCommand.ExecuteAsync(parameter: null);

        // Abgewählte Vorschläge müssen gemerkt werden, sonst erscheinen sie erneut.
        SortProposal ignored = Assert.Single(sorter.Ignored);
        Assert.Equal("foto1.jpg", ignored.Photo.FileName);
    }

    [Fact]
    public async Task Apply_WithNothingSelected_IsNotPossible()
    {
        FakePhotoSorter sorter = new(CreateProposals(2));
        using SortViewModel sut = CreateSut(sorter);
        await AnalyzeAsync(sut);

        foreach (ProposalViewModel proposal in sut.Proposals)
        {
            proposal.IsSelected = false;
        }

        Assert.Equal(0, sut.SelectedProposalCount);
        Assert.False(sut.CanApply);
        Assert.False(sut.ApplyCommand.CanExecute(parameter: null));
    }

    [Fact]
    public async Task ToggleAll_WhenSomeDeselected_SelectsEverything()
    {
        FakePhotoSorter sorter = new(CreateProposals(3));
        using SortViewModel sut = CreateSut(sorter);
        await AnalyzeAsync(sut);
        sut.Proposals[0].IsSelected = false;

        sut.ToggleAllCommand.Execute(parameter: null);

        Assert.Equal(3, sut.SelectedProposalCount);
    }

    [Fact]
    public async Task ToggleAll_WhenAllSelected_DeselectsEverything()
    {
        FakePhotoSorter sorter = new(CreateProposals(3));
        using SortViewModel sut = CreateSut(sorter);
        await AnalyzeAsync(sut);

        sut.ToggleAllCommand.Execute(parameter: null);

        Assert.Equal(0, sut.SelectedProposalCount);
    }

    [Fact]
    public async Task SelectionSummary_ReflectsSelectedCount()
    {
        FakePhotoSorter sorter = new(CreateProposals(3));
        using SortViewModel sut = CreateSut(sorter);
        await AnalyzeAsync(sut);

        sut.Proposals[0].IsSelected = false;

        Assert.Equal("2 von 3 ausgewählt", sut.SelectionSummary);
    }

    // Durchläuft den echten Ablauf bis zur Vorschau: Beispiele laden, eines als
    // passend markieren, Profil lernen, analysieren.
    private static async Task AnalyzeAsync(SortViewModel sut)
    {
        sut.SourceFolder = SourceFolder;
        sut.CategoryName = "Familie";

        await sut.LoadExamplesCommand.ExecuteAsync(parameter: null).ConfigureAwait(false);
        sut.ExampleCandidates[0].IsPositive = true;

        await sut.LearnCommand.ExecuteAsync(parameter: null).ConfigureAwait(false);
        await sut.AnalyzeCommand.ExecuteAsync(parameter: null).ConfigureAwait(false);

        Assert.Equal(SortState.Preview, sut.State);
    }

    [Fact]
    public async Task RefreshUndoState_WithoutAnyRun_OffersNoUndo()
    {
        using SortViewModel sut = CreateSut(new FakePhotoSorter(CreateProposals(1)), new FakeSortUndoService());

        await sut.RefreshUndoStateCommand.ExecuteAsync(parameter: null);

        Assert.False(sut.HasUndoableRun);
        Assert.False(sut.UndoCommand.CanExecute(parameter: null));
    }

    [Fact]
    public async Task RefreshUndoState_WithARecordedRun_OffersUndoAndNamesTheCategory()
    {
        // Der Lauf steht dauerhaft im Protokoll: Auch nach einem Neustart – wenn also
        // keine Vorschläge mehr in der Ansicht liegen – muss der Hinweis erscheinen.
        FakeSortUndoService undo = new();
        undo.SetUndoableRun(fileCount: 3);
        using SortViewModel sut = CreateSut(new FakePhotoSorter(CreateProposals(1)), undo);

        await sut.RefreshUndoStateCommand.ExecuteAsync(parameter: null);

        Assert.True(sut.HasUndoableRun);
        Assert.True(sut.UndoCommand.CanExecute(parameter: null));
        Assert.Contains("Familie", sut.UndoSummary, StringComparison.Ordinal);
        Assert.Contains("3", sut.UndoSummary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Apply_MakesTheRunUndoable()
    {
        FakeSortUndoService undo = new();
        using SortViewModel sut = CreateSut(new FakePhotoSorter(CreateProposals(2)), undo);
        await AnalyzeAsync(sut);

        // Der Sortierdienst ist hier eine Attrappe; der Lauf entstünde in Wirklichkeit
        // beim Verschieben. Entscheidend ist, dass die Ansicht danach nachsieht.
        undo.SetUndoableRun(fileCount: 2);
        await sut.ApplyCommand.ExecuteAsync(parameter: null);

        Assert.True(sut.HasUndoableRun);
    }

    [Fact]
    public async Task Undo_WhenConfirmed_RestoresAndClearsTheOffer()
    {
        FakeSortUndoService undo = new();
        undo.SetUndoableRun(fileCount: 2);
        using SortViewModel sut = CreateSut(new FakePhotoSorter(CreateProposals(1)), undo);
        await sut.RefreshUndoStateCommand.ExecuteAsync(parameter: null);

        await sut.UndoCommand.ExecuteAsync(parameter: null);

        Assert.Equal(1, undo.UndoCount);
        Assert.False(sut.HasUndoableRun);
        Assert.Equal(SortState.Idle, sut.State);
    }

    [Fact]
    public async Task Undo_WhenDeclined_ChangesNothing()
    {
        // Ohne Rückfrage würde ein Fehlklick alle Fotos zurückschieben.
        FakeSortUndoService undo = new();
        undo.SetUndoableRun(fileCount: 2);
        using SortViewModel sut = CreateSut(new FakePhotoSorter(CreateProposals(1)), undo, confirms: false);
        await sut.RefreshUndoStateCommand.ExecuteAsync(parameter: null);

        await sut.UndoCommand.ExecuteAsync(parameter: null);

        Assert.Equal(0, undo.UndoCount);
        Assert.True(sut.HasUndoableRun);
    }

    [Fact]
    public async Task LoadExamples_AsksOnlyForAsManyPhotosAsItShows()
    {
        // Ohne Höchstzahl las die Auswahl den gesamten Ordner ein und schnitt erst danach
        // ab. Weil für jedes Foto die Datei geöffnet wird, lud sie damit bei einem
        // Cloud-Ordner (iCloud-Fotos unter Windows) die ganze Mediathek herunter, um
        // dreißig Bilder zu zeigen.
        FakePhotoSource source = new([]);
        using SortViewModel sut = CreateSut(new FakePhotoSorter(CreateProposals(1)), photoSource: source);
        sut.SourceFolder = SourceFolder;

        await sut.LoadExamplesCommand.ExecuteAsync(parameter: null).ConfigureAwait(true);

        // Ohne Höchstzahl (null) fiele der Wert auf int.MaxValue und der Test durch.
        Assert.InRange(source.LastMaxCount ?? int.MaxValue, 1, 100);
    }

    [Fact]
    public async Task LoadMoreExamples_AsksForTheNextBatch()
    {
        // Bei einem gemischten Ordner ist unter den ersten dreißig Bildern oft kaum
        // eines, das zum gesuchten Thema passt. Ohne einen weiteren Schwung bliebe nur,
        // den Ordner zu wechseln.
        // Genug Bilder für zwei Schwünge: Ist der zweite leer, beginnt die Auswahl
        // bewusst wieder von vorn – dann bliebe der Startpunkt bei null.
        FakePhotoSource source = new([.. Enumerable.Range(0, 45).Select(index => new Photo
        {
            FullPath = Path.Combine(SourceFolder, $"bild-{index}.jpg"),
            FileName = $"bild-{index}.jpg",
        })]);
        using SortViewModel sut = CreateSut(new FakePhotoSorter(CreateProposals(1)), photoSource: source);
        sut.SourceFolder = SourceFolder;

        await sut.LoadExamplesCommand.ExecuteAsync(parameter: null).ConfigureAwait(true);
        int first = source.LastSkip;
        await sut.LoadMoreExamplesCommand.ExecuteAsync(parameter: null).ConfigureAwait(true);

        Assert.Equal(0, first);
        Assert.True(source.LastSkip > 0, "Der zweite Schwung muss hinter dem ersten beginnen.");
    }

    [Fact]
    public void AddExamples_TakesImagesAndIgnoresOtherFiles()
    {
        using SortViewModel sut = CreateSut(new FakePhotoSorter(CreateProposals(1)));
        string folder = Path.Combine(Path.GetTempPath(), "PictureSorterTests", Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(folder);
        try
        {
            string image = Path.Combine(folder, "eigenes.jpg");
            string document = Path.Combine(folder, "notiz.txt");
            File.WriteAllBytes(image, [1, 2, 3]);
            File.WriteAllBytes(document, [1, 2, 3]);

            sut.AddExamples([image, document]);

            ExampleCandidateViewModel candidate = Assert.Single(sut.ExampleCandidates);
            Assert.Equal(image, candidate.FilePath);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void AddExamples_IgnoresTheSameFileTwice()
    {
        using SortViewModel sut = CreateSut(new FakePhotoSorter(CreateProposals(1)));
        string folder = Path.Combine(Path.GetTempPath(), "PictureSorterTests", Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(folder);
        try
        {
            string image = Path.Combine(folder, "eigenes.jpg");
            File.WriteAllBytes(image, [1, 2, 3]);

            sut.AddExamples([image]);
            sut.AddExamples([image]);

            _ = Assert.Single(sut.ExampleCandidates);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static SortViewModel CreateSut(
        FakePhotoSorter sorter,
        FakeSortUndoService? undo = null,
        bool confirms = true,
        FakePhotoSource? photoSource = null)
    {
        Photo examplePhoto = new()
        {
            FullPath = Path.Combine(SourceFolder, "beispiel.jpg"),
            FileName = "beispiel.jpg",
        };

        ReswLocalizer localizer = new();

        return new SortViewModel(
            sorter,
            undo ?? new FakeSortUndoService(),
            photoSource ?? new FakePhotoSource([examplePhoto]),
            new FakeCategoryTrainer(CreateCategory()),
            new FakeCategoryRepository(),
            new FakeFolderPicker(SourceFolder),
            new StubConfirmationService(confirms),
            new StatusBarViewModel(localizer),
            Options.Create(new SortingOptions()),
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
