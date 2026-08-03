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

        Assert.Equal(3, sut.Proposals.Items.Count);
        Assert.All(sut.Proposals.Items, proposal => Assert.True(proposal.IsSelected));
        Assert.Equal(3, sut.Proposals.SelectedCount);
        Assert.True(sut.CanApply);
    }

    [Fact]
    public async Task Apply_WithDeselectedProposal_MovesOnlySelectedOnes()
    {
        FakePhotoSorter sorter = new(CreateProposals(3));
        using SortViewModel sut = CreateSut(sorter);
        await AnalyzeAsync(sut);

        sut.Proposals.Items[1].IsSelected = false;

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

        sut.Proposals.Items[1].IsSelected = false;

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

        foreach (ProposalViewModel proposal in sut.Proposals.Items)
        {
            proposal.IsSelected = false;
        }

        Assert.Equal(0, sut.Proposals.SelectedCount);
        Assert.False(sut.CanApply);
        Assert.False(sut.ApplyCommand.CanExecute(parameter: null));
    }

    [Fact]
    public async Task ToggleAll_WhenSomeDeselected_SelectsEverything()
    {
        FakePhotoSorter sorter = new(CreateProposals(3));
        using SortViewModel sut = CreateSut(sorter);
        await AnalyzeAsync(sut);
        sut.Proposals.Items[0].IsSelected = false;

        sut.Proposals.ToggleAllCommand.Execute(parameter: null);

        Assert.Equal(3, sut.Proposals.SelectedCount);
    }

    [Fact]
    public async Task ToggleAll_WhenAllSelected_DeselectsEverything()
    {
        FakePhotoSorter sorter = new(CreateProposals(3));
        using SortViewModel sut = CreateSut(sorter);
        await AnalyzeAsync(sut);

        sut.Proposals.ToggleAllCommand.Execute(parameter: null);

        Assert.Equal(0, sut.Proposals.SelectedCount);
    }

    [Fact]
    public async Task SelectionSummary_ReflectsSelectedCount()
    {
        FakePhotoSorter sorter = new(CreateProposals(3));
        using SortViewModel sut = CreateSut(sorter);
        await AnalyzeAsync(sut);

        sut.Proposals.Items[0].IsSelected = false;

        Assert.Equal("2 von 3 ausgewählt", sut.Proposals.SelectionSummary);
    }

    // Durchläuft den echten Ablauf bis zur Vorschau: passende Bilder vorschlagen
    // lassen, Profil lernen, analysieren.
    private static async Task AnalyzeAsync(SortViewModel sut)
    {
        sut.SourceFolder = SourceFolder;
        sut.CategoryName = "Familie";

        await sut.SuggestPositivesCommand.ExecuteAsync(parameter: null).ConfigureAwait(false);

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
    public async Task SuggestPositives_AsksOnlyForAsManyPhotosAsThereIsRoomFor()
    {
        // Ohne Höchstzahl las die Auswahl den gesamten Ordner ein und schnitt erst danach
        // ab. Weil für jedes Foto die Datei geöffnet wird, lud sie damit bei einem
        // Cloud-Ordner (iCloud-Fotos unter Windows) die ganze Mediathek herunter.
        FakePhotoSource source = new([]);
        using SortViewModel sut = CreateSut(new FakePhotoSorter(CreateProposals(1)), photoSource: source);
        sut.SourceFolder = SourceFolder;

        await sut.SuggestPositivesCommand.ExecuteAsync(parameter: null).ConfigureAwait(true);

        // Ohne Höchstzahl (null) fiele der Wert auf int.MaxValue und der Test durch.
        Assert.Equal(sut.PositiveExamples.Capacity, source.LastMaxCount ?? int.MaxValue);
    }

    [Fact]
    public async Task Examples_StartEmptyOnBothSides()
    {
        // Vorher standen dreißig zufällige Bilder des Ordners bereits drin, von denen zu
        // einem bestimmten Thema oft kaum eines passte – der Platz war trotzdem belegt.
        using SortViewModel sut = CreateSut(new FakePhotoSorter(CreateProposals(1)));
        sut.SourceFolder = SourceFolder;

        sut.Wizard.GoToStep(0);

        Assert.True(sut.PositiveExamples.IsEmpty);
        Assert.True(sut.NegativeExamples.IsEmpty);
        await Task.CompletedTask.ConfigureAwait(true);
    }

    [Fact]
    public async Task SuggestNegatives_DoesNotOfferImagesAlreadyChosenAsMatching()
    {
        // Dasselbe Foto darf nicht gleichzeitig als passend und als Gegenbeispiel
        // dastehen – das Profil widerspräche sich selbst.
        FakePhotoSource source = new([.. Enumerable.Range(0, 3).Select(index => new Photo
        {
            FullPath = Path.Combine(SourceFolder, $"bild-{index}.jpg"),
            FileName = $"bild-{index}.jpg",
        })]);
        using SortViewModel sut = CreateSut(new FakePhotoSorter(CreateProposals(1)), photoSource: source);
        sut.SourceFolder = SourceFolder;

        await sut.SuggestPositivesCommand.ExecuteAsync(parameter: null).ConfigureAwait(true);
        await sut.SuggestNegativesCommand.ExecuteAsync(parameter: null).ConfigureAwait(true);

        string[] matching = [.. sut.PositiveExamples.Items.Select(item => item.FilePath)];
        Assert.DoesNotContain(sut.NegativeExamples.Items, item => matching.Contains(item.FilePath));
    }

    [Fact]
    public async Task SuggestPositives_TwiceInARow_AsksFurtherAlongTheFolder()
    {
        // Wer ein bestimmtes Thema sucht, findet unter den ersten Bildern eines
        // gemischten Ordners oft kaum eines, das passt. Ohne weiteren Schwung bliebe
        // nur, den Ordner zu wechseln.
        FakePhotoSource source = new([.. Enumerable.Range(0, 45).Select(index => new Photo
        {
            FullPath = Path.Combine(SourceFolder, $"bild-{index}.jpg"),
            FileName = $"bild-{index}.jpg",
        })]);
        using SortViewModel sut = CreateSut(new FakePhotoSorter(CreateProposals(1)), photoSource: source);
        sut.SourceFolder = SourceFolder;

        await sut.SuggestPositivesCommand.ExecuteAsync(parameter: null).ConfigureAwait(true);
        int first = source.LastSkip;
        sut.PositiveExamples.Clear();
        await sut.SuggestPositivesCommand.ExecuteAsync(parameter: null).ConfigureAwait(true);

        Assert.Equal(0, first);
        Assert.True(source.LastSkip > 0, "Der zweite Schwung muss hinter dem ersten beginnen.");
    }

    [Fact]
    public void AddDroppedImages_TakesImagesAndIgnoresOtherFiles()
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

            sut.AddDroppedImages(isPositive: true, [image, document]);

            ExampleCandidateViewModel candidate = Assert.Single(sut.PositiveExamples.Items);
            Assert.Equal(image, candidate.FilePath);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void AddDroppedImages_RespectsTheCapacityOfTheSide()
    {
        // Ohne diese Grenze erfährt der Nutzer erst beim Anlernen, dass es zu viele
        // Bilder waren – dort läuft je Bild ein vollständiger Aufruf der Bilderkennung.
        using SortViewModel sut = CreateSut(new FakePhotoSorter(CreateProposals(1)));
        string folder = Path.Combine(Path.GetTempPath(), "PictureSorterTests", Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(folder);
        try
        {
            int tooMany = sut.PositiveExamples.Capacity + 5;
            List<string> images = [];
            for (int index = 0; index < tooMany; index++)
            {
                string path = Path.Combine(folder, $"bild-{index}.jpg");
                File.WriteAllBytes(path, [1, 2, 3]);
                images.Add(path);
            }

            sut.AddDroppedImages(isPositive: true, images);

            Assert.Equal(sut.PositiveExamples.Capacity, sut.PositiveExamples.Items.Count);
            Assert.True(sut.PositiveExamples.IsFull);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void AddDroppedImages_IgnoresTheSameFileTwice()
    {
        using SortViewModel sut = CreateSut(new FakePhotoSorter(CreateProposals(1)));
        string folder = Path.Combine(Path.GetTempPath(), "PictureSorterTests", Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(folder);
        try
        {
            string image = Path.Combine(folder, "eigenes.jpg");
            File.WriteAllBytes(image, [1, 2, 3]);

            sut.AddDroppedImages(isPositive: true, [image]);
            sut.AddDroppedImages(isPositive: true, [image]);

            _ = Assert.Single(sut.PositiveExamples.Items);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void AddDroppedImages_OnTheNegativeSide_KeepsBothSidesApart()
    {
        using SortViewModel sut = CreateSut(new FakePhotoSorter(CreateProposals(1)));
        string folder = Path.Combine(Path.GetTempPath(), "PictureSorterTests", Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(folder);
        try
        {
            string image = Path.Combine(folder, "gegenbeispiel.jpg");
            File.WriteAllBytes(image, [1, 2, 3]);

            sut.AddDroppedImages(isPositive: false, [image]);

            _ = Assert.Single(sut.NegativeExamples.Items);
            Assert.True(sut.PositiveExamples.IsEmpty);
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
            new TripDetectionService(Options.Create(new TripDetectionOptions())),
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
