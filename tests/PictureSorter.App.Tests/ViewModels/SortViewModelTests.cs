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

    private static SortViewModel CreateSut(FakePhotoSorter sorter)
    {
        Photo examplePhoto = new()
        {
            FullPath = Path.Combine(SourceFolder, "beispiel.jpg"),
            FileName = "beispiel.jpg",
        };

        return new SortViewModel(
            sorter,
            new FakePhotoSource([examplePhoto]),
            new FakeCategoryTrainer(CreateCategory()),
            new FakeCategoryRepository(),
            new FakeFolderPicker(SourceFolder),
            new StubConfirmationService(result: true),
            new StatusBarViewModel(),
            Options.Create(new SortingOptions()),
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
